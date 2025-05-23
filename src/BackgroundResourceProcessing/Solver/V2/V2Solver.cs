using System;
using System.Collections.Generic;
using System.Linq;
using BackgroundResourceProcessing.Collections;
using BackgroundResourceProcessing.Core;
using BackgroundResourceProcessing.Solver.Graph;
using BackgroundResourceProcessing.Solver.Simplex;
using BackgroundResourceProcessing.Solver.V1;

namespace BackgroundResourceProcessing.Solver.V2
{
    /// <summary>
    /// The V2 resource rate solver.
    /// </summary>
    ///
    /// <remarks>
    /// <para>
    ///   This is a somewhat more sophisticated resource rate solver. It works
    ///   by first merging compatible inventories and converters. It then
    ///   topologically sorts the converters and repeatedly iterates over them
    ///   in both forward and reverse order, bumping up the converter rates if
    ///   there is headroom.
    /// </para>
    ///
    /// <para>
    ///   Like the V1 solver, this one also has a number of issues:
    ///   <list type="bullet">
    ///     <item>
    ///       It completely ignores resource flow rules, so, for example, drills
    ///       can consume mass from asteroids other than the one they are drilling.
    ///     </item>
    /// </para>
    /// </remarks>
    public class V2Solver : ISolver
    {
        public Dictionary<InventoryId, double> ComputeInventoryRates(ResourceProcessor processor)
        {
            var graph = new ResourceGraph(processor);

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

            // We don't handle this, fall back to the V1 solver.
            if (graph.HasSplitResourceEdges())
                return new V1Solver().ComputeInventoryRates(processor);

            IntMap<int> converterMap = new(processor.converters.Count);
            double[] func = new double[graph.converters.Count];
            int index = 0;
            foreach (var (converterId, converter) in graph.converters)
            {
                var varId = index++;
                converterMap[converterId] = varId;

                foreach (var src in converter.converters)
                    func[varId] += GetPriorityWeight(src.behaviour.Priority);
            }

            LinearProblem problem = new(func);

            // Constrain converter rates to be <= 1.
            //
            // We don't need to add >= 0 since they are implicit in the
            // in the representation for the problem.
            for (int i = 0; i < func.Length; ++i)
            {
                double[] coefs = new double[func.Length];
                coefs[i] = 1.0;
                problem.AddLEqualConstraint(coefs, 1.0);
            }

            foreach (var (inventoryId, inventory) in graph.inventories)
            {
                // Inventories that are neither full nor empty result in no constraints
                if (inventory.state == InventoryState.Unconstrained)
                    continue;

                if ((inventory.state & InventoryState.Full) == InventoryState.Full)
                {
                    double[] coefs = new double[graph.converters.Count];

                    foreach (var converterId in graph.inputs.GetInventoryEntry(inventoryId))
                    {
                        var converter = graph.converters[converterId];
                        var input = converter.inputs[inventory.resourceName];
                        var varId = converterMap[converterId];

                        coefs[varId] -= input.rate;
                    }

                    foreach (var converterId in graph.outputs.GetInventoryEntry(inventoryId))
                    {
                        var converter = graph.converters[converterId];
                        var output = converter.outputs[inventory.resourceName];
                        var varId = converterMap[converterId];

                        if (output.dumpExcess)
                            continue;

                        coefs[varId] += output.rate;
                    }

                    problem.AddLEqualConstraint(coefs, 0.0);
                }

                if ((inventory.state & InventoryState.Empty) == InventoryState.Empty)
                {
                    double[] coefs = new double[graph.converters.Count];

                    foreach (var converterId in graph.inputs.GetInventoryEntry(inventoryId))
                    {
                        var converter = graph.converters[converterId];
                        var input = converter.inputs[inventory.resourceName];
                        var varId = converterMap[converterId];

                        coefs[varId] += input.rate;
                    }

                    foreach (var converterId in graph.outputs.GetInventoryEntry(inventoryId))
                    {
                        var converter = graph.converters[converterId];
                        var output = converter.outputs[inventory.resourceName];
                        var varId = converterMap[converterId];

                        coefs[varId] -= output.rate;
                    }

                    problem.AddLEqualConstraint(coefs, 0.0);
                }
            }

            LogUtil.Log('\n', problem);
            var rates = problem.Solve();
            LogUtil.Log($"rates: {string.Join(", ", rates)}");

            IntMap<double> ratesById = new(converterMap.Capacity);
            foreach (var (converterId, varId) in converterMap)
                ratesById.Add(converterId, rates[varId]);

            return graph.ComputeInventoryRates(ratesById, processor);
        }

        static double GetPriorityWeight(int priority)
        {
            // This is chosen so that B^10 ~= 1e6, which should be sufficiently
            // small that the simplex solver remains well-conditioned.
            const double B = 3.98107;

            priority = Math.Max(Math.Min(priority, 10), -10);

            return Math.Pow(B, priority);
        }
    }
}
