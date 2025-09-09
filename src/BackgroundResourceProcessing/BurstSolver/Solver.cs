using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using BackgroundResourceProcessing.Collections.Burst;
using BackgroundResourceProcessing.Core;
using BackgroundResourceProcessing.Tracing;
using BackgroundResourceProcessing.Utils;
using Unity.Burst;
using static BackgroundResourceProcessing.Collections.KeyValuePairExt;
using InventoryState = BackgroundResourceProcessing.Solver.InventoryState;
using SolverSolution = BackgroundResourceProcessing.Solver.SolverSolution;
using UnsolvableProblemException = BackgroundResourceProcessing.Solver.UnsolvableProblemException;

namespace BackgroundResourceProcessing.BurstSolver;

[BurstCompile]
internal static class Solver
{
    public static unsafe SolverSolution ComputeInventoryRates(ResourceProcessor processor)
    {
        using var span = new TraceSpan("ResourceProcessor.ComputeInventoryRates");

        var graph = new ResourceGraph(processor);
        int converterCount = processor.converters.Count;
        int inventoryCount = processor.inventories.Count;

        var inventoryRates = new double[inventoryCount];
        var converterRates = new double[converterCount];

        fixed (
            double* inv = inventoryRates,
                conv = converterRates
        )
        {
            bool res = ComputeInventoryRates(ref graph, inv, conv);
            if (!res)
                throw new UnsolvableProblemException();
        }

        return new() { converterRates = converterRates, inventoryRates = inventoryRates };
    }

    [BurstCompile]
    private static unsafe bool ComputeInventoryRates(
        ref ResourceGraph graph,
        double* _inventoryRates,
        double* _converterRates
    )
    {
        var summaries = Summarize(in graph);

        int converterCount = graph.converters.Count;
        int inventoryCount = graph.inventories.Count;

        MemorySpan<double> inventoryRates = new(_inventoryRates, inventoryCount);
        MemorySpan<double> converterRates = new(_converterRates, converterCount);

        // Pre-processing: We can make final problem smaller (and thus
        // cheaper to solve) by combining together converters and
        // inventories that share the same connections.
        //
        // Since there are frequently multiple identical inventories
        // (e.g. all the batteries) and multiple identical converters
        // (e.g. solar panels) this tends to significantly reduce the number
        // of variables that we need to feed to the simplex solver.
        graph.MergeEquivalentInventories();
        graph.MergeEquivalentConverters();

        var converterMap = new RawIntMap<int>(converterCount);
        int index = 0;
        foreach (var converterId in graph.converters.Keys)
            converterMap.Add(converterId, index++);

        var problem = new LinearProblem();
        var rates = problem.CreateVariables(graph.converters.Count);

        foreach (var rate in rates)
            problem.AddConstraint(rate <= 1.0);

        // The equation for the actual rates of the inventory
        RawIntMap<LinearEquation> iRates = new(inventoryCount);

        // The equation for the inventory rate, but not including any
        // outputs with dumpExcess = true
        RawIntMap<LinearEquation> dRates = new(inventoryCount);

        // Scratch space used for building constraint totals.
        LinearMap<int, LinearEquation> constraintEqs = [];

        RawList<int> connected = new(16);

        // The object function that we are optimizing.
        LinearEquation func = new(problem.VariableCount);

        foreach (var converterId in graph.converters.Keys)
        {
            ref var converter = ref graph.converters[converterId];
            var varId = converterMap[converterId];
            var alpha = rates[varId];

            func.Add(alpha * converter.weight);

            var inputInvs = graph.inputs.GetConverterEntry(converterId);
            var outputInvs = graph.outputs.GetConverterEntry(converterId);

            foreach (var (resource, input) in converter.inputs)
            {
                var rate = input.Ratio * alpha;

                connected.Clear();

                foreach (var invId in inputInvs)
                {
                    if (graph.inventories[invId].resourceId != resource)
                        continue;
                    connected.Add(invId);
                }

                if (connected.Count == 0)
                {
                    // The converter has no inventories attached to this
                    // input. This means its rate must be 0.
                    problem.AddConstraint(alpha == 0);
                }
                else if (connected.Count == 1)
                {
                    // The converter has exactly one inventory attached to
                    // this input. The rate contributed by this converter to
                    // the inventory is exactly alpha*rate and we don't need
                    // to introduce any new variables.
                    var invId = connected[0];
                    if (!iRates.ContainsKey(invId))
                        iRates.Add(invId, new LinearEquation(problem.VariableCount * 2));
                    iRates[invId].Sub(rate);

                    if (dRates.ContainsKey(invId))
                        dRates[invId].Sub(rate);
                }
                else
                {
                    // This input is pulling from multiple different inventories.
                    // We introduce variables representing the rates from each
                    // inventory and allow the solver to determine where they
                    // should flow.
                    var rateVars = problem.CreateVariables(connected.Count);
                    var rateEq = new LinearEquation(problem.VariableCount);
                    rateEq.Sub(rate);

                    for (int i = 0; i < connected.Count; ++i)
                    {
                        var invId = connected[i];
                        if (!iRates.ContainsKey(invId))
                            iRates.Add(invId, new(problem.VariableCount * 2));
                        ref var invRate = ref iRates[invId];
                        var rateVar = rateVars[i];

                        rateEq.Add(rateVar);
                        invRate.Sub(rateVar);

                        if (dRates.ContainsKey(invId))
                            dRates[invId].Sub(rateVar);
                    }

                    // sum(rateVars) == rate
                    problem.AddConstraint(rateEq == 0.0);
                }
            }

            foreach (var (resource, output) in converter.outputs)
            {
                var rate = output.Ratio * alpha;

                connected.Clear();

                foreach (var invId in outputInvs)
                {
                    if (graph.inventories[invId].resourceId != resource)
                        continue;
                    connected.Add(invId);
                }

                if (connected.Count == 0)
                {
                    if (!output.DumpExcess)
                    {
                        // The converter has no inventories attached to this
                        // output. This means its rate must be 0 unless
                        // dumpExcess is true.
                        problem.AddConstraint(rate == 0);
                    }
                }
                else if (connected.Count == 1)
                {
                    // The converter has exactly one inventory attached to
                    // this output. The rate contributed by this converter to
                    // the inventory is exactly alpha*rate and we don't need
                    // to introduce any new variables.
                    var invId = connected[0];
                    if (!iRates.ContainsKey(invId))
                        iRates.Add(invId, new(problem.VariableCount * 2));
                    ref var invRate = ref iRates[invId];
                    if (output.DumpExcess)
                    {
                        if (!dRates.ContainsKey(invId))
                            dRates.Add(invId, invRate.Clone());
                    }
                    else if (dRates.ContainsKey(invId))
                        dRates[invId].Add(rate);

                    invRate.Add(rate);
                }
                else
                {
                    // This output is pushing to multiple different inventories.
                    // We introduce variables representing the rates into each
                    // inventory and allow the solver to determine where they
                    // should flow.
                    var rateVars = problem.CreateVariables(connected.Count);
                    var rateEq = new LinearEquation(problem.VariableCount);
                    rateEq.Sub(rate);

                    for (int i = 0; i < connected.Count; ++i)
                    {
                        var invId = connected[i];
                        if (!iRates.ContainsKey(invId))
                            iRates.Add(invId, new(problem.VariableCount * 2));
                        ref var invRate = ref iRates[invId];
                        var rateVar = rateVars[i];

                        rateEq.Add(rateVar);

                        if (output.DumpExcess)
                        {
                            if (!dRates.ContainsKey(invId))
                                dRates.Add(invId, invRate.Clone());
                        }
                        else if (dRates.ContainsKey(invId))
                            dRates[invId].Add(rate);

                        invRate.Add(rateVar);
                    }

                    // sum(rateVars) == rate
                    problem.AddConstraint(rateEq == 0.0);
                }
            }
        }

        foreach (var converterId in graph.converters.Keys)
        {
            ref var converter = ref graph.converters[converterId];
            if (converter.constraints.Count == 0)
                continue;

            var varId = converterMap[converterId];
            var alpha = rates[varId];

            // We handle required resources as an OR constraint.
            // Basically, we end up with either:
            // * alpha == 0, OR,
            // * total >= 0
            //
            // i.e. either the converter must be disabled or the net
            // resource production of the constraining resource must be
            // positive.
            //
            // Note that converters can have flow modes associated with
            // their constraints so we need to build up equations for the
            // relevant constraints.

            constraintEqs.Clear();
            foreach (var inventoryId in graph.constraints.GetConverterEntry(converterId))
            {
                var inventory = graph.inventories[inventoryId];
                if (!converter.constraints.ContainsKey(inventory.resourceId))
                    continue;
                if (!iRates.TryGetValue(inventoryId, out var irate))
                    continue;

                if (!constraintEqs.TryGetIndex(inventory.resourceId, out var idx))
                {
                    idx = constraintEqs.Count;
                    constraintEqs.AddUnchecked(inventory.resourceId, new(problem.VariableCount));
                }

                ref var eq = ref constraintEqs.GetAtIndex(idx);
                eq.Add(irate);
            }

            foreach (var (resource, constraint) in converter.constraints)
            {
                // If the constrained resource does not have a total rate
                // then its rate is 0 and we can skip emitting the constraint
                // since it will always be true.
                if (!constraintEqs.TryGetValue(resource, out var total))
                    continue;

                switch (constraint)
                {
                    case Constraint.AT_LEAST:
                        problem.AddOrConstraint(alpha <= 0.0, total >= 0.0);
                        break;
                    case Constraint.AT_MOST:
                        problem.AddOrConstraint(alpha <= 0.0, total <= 0.0);
                        break;
                }
            }

            foreach (var inventoryId in graph.inventories.Keys)
            {
                ref var inventory = ref graph.inventories[inventoryId];

                // The inventory is unconstrained. There is nothing we need to
                // do here, any rate is acceptable.
                if (inventory.state == InventoryState.Unconstrained)
                    continue;

                // This inventory has no converters interacting with it. It will
                // have a rate of 0 and that is always acceptable.
                if (!iRates.TryGetValue(inventoryId, out var iRate))
                    continue;

                LinearEquation? dRate = null;
                if (dRates.TryGetValue(inventoryId, out var _dRate))
                    dRate = _dRate;

                if (dRate == null && inventory.state == InventoryState.Zero)
                {
                    // We can special-case zero-sized inventories to make them
                    // easier to solve via the simplex algorithm.
                    //
                    // This only applies if none of the outputs involved have
                    // dumpExcess set to true, as in that case the rate can
                    // actually vary.
                    problem.AddConstraint(iRate == 0.0);
                    continue;
                }

                // Full inventories are constrained to have their rate <= 0
                //
                // However, if certain converter outputs have dumpExcess == true
                // then that is not included in the constraint.
                if ((inventory.state & InventoryState.Full) == InventoryState.Full)
                    problem.AddConstraint((dRate ?? iRate) <= 0.0);

                // Empty inventories are constrained to have their rate >= 0.
                //
                // However, if certain converter outputs have dumpExcess == true
                // then that is not included in the constraint.
                if ((inventory.state & InventoryState.Empty) == InventoryState.Empty)
                    problem.AddConstraint(iRate >= 0.0);
            }
        }

        var nsoln = problem.Maximize(func);
        if (nsoln is null)
            return false;
        var soln = nsoln.Value;

        foreach (var (invId, iRate) in iRates)
        {
            double rate = 0.0;
            double norm = 0.0;

            foreach (var var in iRate)
            {
                var eval = soln.Evaluate(var);

                rate += eval;
                norm += Math.Abs(eval);
            }

            // This is a bit of a hack.
            //
            // It is easy to end up very small residual rates when there
            // are several converters producing/consuming large amounts
            // of resources. We don't want that because its somewhat messy.
            // However, we also don't want to truncate all small rates
            // because there are legitimate reasons to have a converter
            // that produces small amount of resources.
            //
            // What we do here is to truncate small rates to 0 only if the
            // rate divided by the norm of its rates is sufficiently small.
            // That way, if you have a few slow converters it does not get
            // truncated but if you have some large consumer/producers then
            // we do truncate.
            //
            // In practice this seems to work well enough.
            if (Math.Abs(rate) < 1e-6 && Math.Abs(rate) / norm < 1e-6)
                rate = 0.0;

            // The array defaults to 0, so no need to actually do the work
            // to set things if the rate is always 0.
            if (rate == 0.0)
                continue;

            var inventory = graph.inventories[invId];
            var total = rate < 0.0 ? inventory.amount : inventory.maxAmount - inventory.amount;

            // This is possible when solvers have DumpExcess = true.
            // In that case we just leave the inventory rates at 0.
            if (total == 0.0)
                continue;

            int? count = null;
            foreach (var realId in new ClassEnumerator(graph.inventoryIds, inventory.baseId))
            {
                var summary = summaries[realId];
                double frac = 0.0;
                if (!MathUtil.IsFinite(total))
                {
                    count ??= new ClassEnumerator(graph.inventoryIds, inventory.baseId).Count();
                    frac = 1.0 / count.Value;
                }
                else if (rate < 0.0)
                    frac = summary.amount / total;
                else
                    frac = (summary.maxAmount - summary.amount) / total;

                inventoryRates[realId] = rate * frac;
            }
        }

        foreach (var (convId, varId) in converterMap)
        {
            var rate = soln[rates[varId]];
            var converter = graph.converters[convId];
            foreach (var realId in new ClassEnumerator(graph.converterIds, converter.baseId))
                converterRates[realId] = rate;
        }

        return true;
    }

    private static RawArray<InventorySummary> Summarize(in ResourceGraph graph)
    {
        var summaries = new RawArray<InventorySummary>(graph.inventories.Capacity);
        foreach (var (id, inventory) in graph.inventories)
            summaries[id] = new(inventory);
        return summaries;
    }

    [DebuggerDisplay("{amount}/{maxAmount}")]
    private struct InventorySummary(GraphInventory inventory)
    {
        public double amount = inventory.amount;
        public double maxAmount = inventory.maxAmount;
    }

    internal static BitSpan GetConverterEntry(this ref AdjacencyMatrix matrix, int converter) =>
        matrix[converter];
}

internal struct ClassEnumerator(MemorySpan<int> classes, int cls) : IEnumerator<int>
{
    readonly MemorySpan<int> classes = classes;
    readonly int cls = cls;
    int index = -1;

    public readonly int Current => index;
    readonly object IEnumerator.Current => Current;

    public bool MoveNext()
    {
        while (true)
        {
            index += 1;
            if (index >= classes.Length)
                return false;
            if (classes[index] == cls)
                return true;
        }
    }

    public readonly void Dispose() { }

    public void Reset()
    {
        index = -1;
    }

    public readonly ClassEnumerator GetEnumerator()
    {
        return this;
    }

    public readonly int Count()
    {
        int count = 0;
        for (int i = index + 1; i < classes.Length; ++i)
            if (classes[i] == cls)
                count += 1;
        return count;
    }
}
