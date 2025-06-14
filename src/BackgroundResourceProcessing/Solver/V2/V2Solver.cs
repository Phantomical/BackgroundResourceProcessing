using System;
using System.Collections.Generic;
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
    ///   This solver converts the resource graph into a linear programming
    ///   problem and then solves that problem by maximizing the number of
    ///   converters that are active. Converter priorities are handled here
    ///   by increasing their weight in the objective function.
    /// </para>
    ///
    /// <para>
    /// This solver has a few caveats:
    /// <list type="bullet">
    ///   <item>
    ///     It doesn't work when there are "split edges". That is when a
    ///     converter outputs a single resource to multiple inventories and
    ///     those inventories have different sets of converters pulling from
    ///     them. In this case it falls back to the V1 solver.
    ///   </item>
    ///   <item>
    ///     The solver behaves somewhat unintuitively in when converter
    ///     outputs have <c>dumpExcess = true</c>. It will maximize the number
    ///     of active converters even when that doesn't necessarily match what
    ///     would happen in KSP.
    ///   </item>
    /// </list>
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

            Simplex.LinearProblem problem = new(func);

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
                AddInventoryConstraint(problem, inventoryId, inventory, graph, converterMap);
            }

            var rates = problem.Solve();

            IntMap<double> ratesById = new(converterMap.Capacity);
            foreach (var (converterId, varId) in converterMap)
                ratesById.Add(converterId, rates[varId]);

            return graph.ComputeInventoryRates(ratesById, processor);
        }

        static void AddInventoryConstraint(
            Simplex.LinearProblem problem,
            int inventoryId,
            GraphInventory inventory,
            ResourceGraph graph,
            IntMap<int> converterMap
        )
        {
            // Unconstrained inventories have, well, no constraints
            if (inventory.state == InventoryState.Unconstrained)
                return;

            // Empty inventories are constrained to have their rate >= 0
            if ((inventory.state & InventoryState.Empty) == InventoryState.Empty)
            {
                double[] coefs = new double[graph.converters.Count];
                bool dumpExcess = false;

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
                        dumpExcess = true;

                    coefs[varId] += output.rate;
                }

                // We can special-case zero-sized inventories to make them easier
                // to solve via the simplex algorithm.
                //
                // This only applies if none of the converter outputs involved
                // have dumpExcess set to true, as in that case the rate can actually
                // vary.
                if (!dumpExcess && inventory.state == InventoryState.Zero)
                {
                    problem.AddEqualityConstraint(coefs, 0.0);

                    // Since we've added an equality constraint we can skip
                    // adding other constraints.
                    return;
                }

                problem.AddGEqualConstraint(coefs, 0.0);
            }

            // Full inventories are constrained to have their rate <= 0
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
