using System;
using System.Collections.Generic;
using System.Linq;
using BackgroundResourceProcessing.Collections;
using BackgroundResourceProcessing.Core;
using BackgroundResourceProcessing.Tracing;
using BackgroundResourceProcessing.Utils;

namespace BackgroundResourceProcessing.Solver;

internal struct SolverSolution
{
    public double[] inventoryRates;
    public double[] converterRates;
}

internal class Solver
{
    public SolverSolution ComputeInventoryRates(ResourceProcessor processor)
    {
        using var span = new TraceSpan("ResourceProcessor.ComputeInventoryRates");
        var graph = new ResourceGraph(processor);

        var summaries = new InventorySummary[processor.inventories.Count];
        for (int i = 0; i < summaries.Length; ++i)
            summaries[i] = new(processor.inventories[i]);

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

        IntMap<int> converterMap = new(processor.converters.Count);
        int index = 0;
        foreach (var converterId in graph.converters.Keys)
            converterMap[converterId] = index++;

        var problem = new LinearProblem();
        var rates = problem.CreateVariables(converterMap.Count);

        foreach (var rate in rates)
            problem.AddConstraint(rate <= 1.0);

        var inventoryCount = processor.inventories.Count;

        // The equation for the actual rates of the inventory
        IntMap<LinearEquation> iRates = new(inventoryCount);

        // The equation for the inventory rate, but not including any
        // outputs with dumpExcess = true
        IntMap<LinearEquation> dRates = new(inventoryCount);

        // Scratch space used for building constraint totals.
        Dictionary<string, LinearEquation> constraintEqs = [];

        List<int> connected = [];

        // The object function that we are optimizing.
        LinearEquation func = [];

        foreach (var (converterId, converter) in graph.converters)
        {
            var varId = converterMap[converterId];
            var alpha = rates[varId];

            func.Add(alpha * converter.weight);

            var inputInvs = graph.inputs.GetConverterEntry(converterId);
            var outputInvs = graph.outputs.GetConverterEntry(converterId);

            foreach (var (resource, input) in converter.inputs)
            {
                var rate = input.rate * alpha;

                connected.Clear();

                foreach (var invId in inputInvs)
                {
                    if (graph.inventories[invId].resourceName != resource)
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
                    var invId = connected.First();
                    var invRate = iRates.GetOrAdd(invId, () => new());
                    invRate.Sub(rate);

                    if (dRates.TryGetValue(invId, out var dRate))
                        dRate.Sub(rate);
                }
                else
                {
                    // This input is pulling from multiple different inventories.
                    // We introduce variables representing the rates from each
                    // inventory and allow the solver to determine where they
                    // should flow.
                    var rateVars = problem.CreateVariables(connected.Count);
                    var rateEq = new LinearEquation(-rate);

                    for (int i = 0; i < connected.Count; ++i)
                    {
                        var invId = connected[i];
                        var invRate = iRates.GetOrAdd(invId, () => new());
                        var rateVar = rateVars[i];

                        rateEq.Add(rateVar);
                        invRate.Sub(rateVar);

                        if (dRates.TryGetValue(invId, out var dRate))
                            dRate.Sub(rateVar);
                    }

                    // sum(rateVars) == rate
                    problem.AddConstraint(rateEq == 0.0);
                }
            }

            foreach (var (resource, output) in converter.outputs)
            {
                var rate = output.rate * alpha;

                connected.Clear();

                foreach (var invId in outputInvs)
                {
                    if (graph.inventories[invId].resourceName != resource)
                        continue;
                    connected.Add(invId);
                }

                if (connected.Count == 0)
                {
                    if (!output.dumpExcess)
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
                    var invId = connected.First();
                    var invRate = iRates.GetOrAdd(invId, () => new());

                    if (output.dumpExcess)
                        dRates.GetOrAdd(invId, () => invRate.Clone());
                    else if (dRates.TryGetValue(invId, out var dRate))
                        dRate.Add(rate);

                    invRate.Add(rate);
                }
                else
                {
                    // This output is pushing to multiple different inventories.
                    // We introduce variables representing the rates into each
                    // inventory and allow the solver to determine where they
                    // should flow.
                    var rateVars = problem.CreateVariables(connected.Count);
                    var rateEq = new LinearEquation(-rate);

                    for (int i = 0; i < connected.Count; ++i)
                    {
                        var invId = connected[i];
                        var invRate = iRates.GetOrAdd(invId, () => new());
                        var rateVar = rateVars[i];

                        rateEq.Add(rateVar);

                        if (output.dumpExcess)
                            dRates.GetOrAdd(invId, () => invRate.Clone());
                        else if (dRates.TryGetValue(invId, out var dRate))
                            dRate.Add(rateVar);

                        invRate.Add(rateVar);
                    }

                    // sum(rateVars) == rate
                    problem.AddConstraint(rateEq == 0.0);
                }
            }
        }

        foreach (var (converterId, converter) in graph.converters)
        {
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
                if (!converter.constraints.ContainsKey(inventory.resourceName))
                    continue;
                if (!iRates.TryGetValue(inventoryId, out var irate))
                    continue;

                var eq = constraintEqs.GetOrAdd(inventory.resourceName, () => new());
                eq += irate;
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
        }

        foreach (var (inventoryId, inventory) in graph.inventories)
        {
            // The inventory is unconstrained. There is nothing we need to
            // do here, any rate is acceptable.
            if (inventory.state == InventoryState.Unconstrained)
                continue;

            // This inventory has no converters interacting with it. It will
            // have a rate of 0 and that is always acceptable.
            if (!iRates.TryGetValue(inventoryId, out var iRate))
                continue;

            if (!dRates.TryGetValue(inventoryId, out var dRate))
                dRate = null;

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

        var soln = problem.Maximize(func);

        double[] inventoryRates = new double[inventoryCount];
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

            // Otherwise, if the rate is small enough we can just truncate
            // it to 0. At 1e-9 it would take 127 kerbin years just to
            // create a single unit of a resource.
            //
            // KSP converters decide to truncate to 0 at this point so this
            // is likely OK.
            if (Math.Abs(rate) < 1e-9)
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

            foreach (var realId in inventory.ids)
            {
                var summary = summaries[realId];
                double frac = 0.0;
                if (!MathUtil.IsFinite(total))
                    frac = 1.0 / inventory.ids.Count;
                else if (frac < 0.0)
                    frac = summary.amount / total;
                else
                    frac = (summary.maxAmount - summary.amount) / total;

                inventoryRates[realId] = rate * frac;
            }
        }

        double[] converterRates = new double[converterMap.Capacity];
        foreach (var (convId, varId) in converterMap)
        {
            var rate = soln[rates[varId]];
            var converter = graph.converters[convId];
            foreach (var realId in converter.ids)
                converterRates[realId] = rate;
        }

        return new() { inventoryRates = inventoryRates, converterRates = converterRates };
    }

    private struct InventorySummary(ResourceInventory inventory)
    {
        public double amount = inventory.amount;
        public double maxAmount = inventory.maxAmount;
    }
}
