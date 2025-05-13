using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using UnifiedBackgroundProcessing.Collections;
using UnifiedBackgroundProcessing.Core;
using UnifiedBackgroundProcessing.Utils;

namespace UnifiedBackgroundProcessing.Solver.V1
{
    /// <summary>
    /// A basic resource rate solver.
    /// </summary>
    ///
    /// <remarks>
    /// <para>
    /// This is a very simple resource solver. It works by iterating over the
    /// converters in (approximate, in case of cycles) topological order, and
    /// bumping up the converter rates if there is headroom.
    /// </para>
    ///
    /// It has a number of issues:
    /// <list type="bullet">
    ///   <item>
    ///     It is prone to oscillations: resource rates may bounce between full
    ///     and empty resulting in lots of spurious changepoints.
    ///   </item>
    ///   <item>
    ///     It completely ignores resource flow rules.
    ///   </item>
    /// </list>
    /// </remarks>
    public class V1Solver : ISolver
    {
        public Dictionary<InventoryId, double> ComputeInventoryRates(ResourceProcessor processor)
        {
            return new SolverInstance(processor).ComputeInventoryRates();
        }
    }

    public class SolverInstance
    {
        const int MaxIterations = 8;

        ResourceProcessor processor;

        Dictionary<string, InventoryState> constraints;
        Dictionary<string, double> resourceRates;
        List<double> converterRates;
        List<Converter> converters;

        public Dictionary<string, InventoryState> Constraints => constraints;
        public Dictionary<string, double> ResourceRates => resourceRates;

        /// <summary>
        /// Create a new solver instance and initialize all
        /// </summary>
        /// <param name="processor"></param>
        public SolverInstance(ResourceProcessor processor)
        {
            this.processor = processor;
            constraints = ComputeInventoryConstraints(processor);
            resourceRates =
            [
                .. constraints.Keys.Select(key => DictUtil.CreateKeyValuePair(key, 0.0)),
            ];
            converterRates = [.. processor.converters.Select(_ => 0.0)];
            converters = ComputeConverterTopologicalSort();
        }

        public Dictionary<InventoryId, double> ComputeInventoryRates()
        {
            ComputeConverterRates();

            Dictionary<string, List<InventoryId>> nonEmptyInventories = [];
            Dictionary<string, List<InventoryId>> nonFullInventories = [];
            Dictionary<string, double> totals = [];
            Dictionary<InventoryId, double> inventoryRates = [];

            foreach (var (id, inventory) in processor.inventories.KSPEnumerate())
            {
                if (!inventory.Full)
                    nonFullInventories.GetOrAdd(id.resourceName, []).Add(id);
                if (!inventory.Empty)
                    nonEmptyInventories.GetOrAdd(id.resourceName, []).Add(id);

                if (!totals.TryGetValue(id.resourceName, out var total))
                    total = 0.0;

                if (resourceRates[id.resourceName] <= 0.0)
                    total += inventory.amount;
                else
                    total += inventory.maxAmount - inventory.amount;
                totals[id.resourceName] = total;
            }

            foreach (var (id, inventory) in processor.inventories.KSPEnumerate())
            {
                var resourceRate = resourceRates[id.resourceName];
                var total = totals[id.resourceName];
                var frac = 0.0;

                if (resourceRate < 0.0)
                    frac = inventory.amount / total;
                else if (resourceRate > 0.0)
                    frac = (inventory.maxAmount - inventory.amount) / total;

                inventoryRates.Add(id, resourceRate * frac);
            }

            return inventoryRates;
        }

        /// <summary>
        /// Get converter rates by their converter IDs. This is intended for
        /// use in tests.
        /// </summary>
        /// <returns></returns>
        public Dictionary<int, double> GetConverterRatesById()
        {
            ComputeConverterRates();

            Dictionary<int, double> output = [];
            for (int i = 0; i < converters.Count; ++i)
                output[converters[i].id] = converterRates[i];

            return output;
        }

        private void ComputeConverterRates()
        {
            for (int i = 0; i < converterRates.Count; ++i)
                converterRates[i] = 0.0;

            for (int i = 0; i < MaxIterations; ++i)
            {
                bool fwdChange = AdjustRatesForward();
                bool revChange = AdjustRatesReverse();

                if (!fwdChange && !revChange)
                    break;
            }
        }

        private bool AdjustRatesForward()
        {
            bool madeChange = false;

            for (int i = 0; i < converters.Count; ++i)
            {
                double rate = converterRates[i];
                var converter = converters[i];

                // Converter is already maxxed out, nothing to do here.
                if (rate == 1.0)
                    continue;

                var newRate = Clamp(GetPermittedRate(converter), 0.0, 1.0);
                if (Math.Abs(newRate - rate) < 1e-9)
                    continue;

                var change = newRate - rate;
                madeChange = true;

                foreach (var (_, res) in converter.inputs.KSPEnumerate())
                    resourceRates[res.ResourceName] -= change * res.Ratio;
                foreach (var (_, res) in converter.outputs.KSPEnumerate())
                    resourceRates[res.ResourceName] += change * res.Ratio;
                converterRates[i] = newRate;
            }

            return madeChange;
        }

        private bool AdjustRatesReverse()
        {
            bool madeChange = false;

            for (int i = converters.Count - 1; i >= 0; --i)
            {
                double rate = converterRates[i];
                var converter = converters[i];

                // Converter is already maxxed out, nothing to do here.
                if (rate == 1.0)
                    continue;

                var newRate = Clamp(GetPermittedRate(converter), 0.0, 1.0);
                if (Math.Abs(newRate - rate) < 1e-9)
                    continue;

                var change = newRate - rate;
                madeChange = true;

                foreach (var (_, res) in converter.inputs.KSPEnumerate())
                    resourceRates[res.ResourceName] -= change * res.Ratio;
                foreach (var (_, res) in converter.outputs.KSPEnumerate())
                    resourceRates[res.ResourceName] += change * res.Ratio;

                Debug.Assert(MathUtil.IsFinite(newRate));
                converterRates[i] = newRate;
            }

            return madeChange;
        }

        private double GetPermittedRate(Converter converter)
        {
            double rate = 1.0;

            foreach (var (_, res) in converter.inputs.KSPEnumerate())
            {
                var resourceRate = resourceRates[res.ResourceName];
                var state = constraints[res.ResourceName];
                var newrate = 0.0;

                if ((state & InventoryState.Empty) != InventoryState.Unconstrained)
                    newrate = Clamp(resourceRate / res.Ratio, 0.0, 1.0);
                else
                    newrate = 1.0;

                rate = Math.Min(rate, newrate);
            }

            foreach (var (_, res) in converter.outputs.KSPEnumerate())
            {
                var resourceRate = resourceRates[res.ResourceName];
                var state = constraints[res.ResourceName];
                var newrate = 0.0;

                if (
                    (state & InventoryState.Full) != InventoryState.Unconstrained
                    && !res.DumpExcess
                )
                    newrate = Clamp(-resourceRate / res.Ratio, 0.0, 1.0);
                else
                    newrate = 1.0;

                rate = Math.Min(rate, newrate);
            }

            return rate;
        }

        private List<Converter> ComputeConverterTopologicalSort()
        {
            Dictionary<string, HashSet<int>> inputs = [];

            for (int i = 0; i < processor.converters.Count; ++i)
            {
                var converter = processor.converters[i];

                foreach (var (_, resource) in converter.inputs.KSPEnumerate())
                    inputs.GetOrAdd(resource.ResourceName, []).Add(i);
            }

            // Forward edges, from output -> connected inputs
            Dictionary<int, HashSet<int>> forward = [];
            Dictionary<int, HashSet<int>> reverse = [];

            for (int i = 0; i < processor.converters.Count; ++i)
            {
                var converter = processor.converters[i];

                foreach (var (_, resource) in converter.outputs.KSPEnumerate())
                {
                    if (!inputs.TryGetValue(resource.ResourceName, out var inEdges))
                        continue;

                    foreach (var dest in inEdges)
                    {
                        // Skip trivial self-cycles since they'll just make the
                        // topological sort do extra work breaking them for no
                        // ordering benefit.
                        if (dest == i)
                            continue;

                        forward.GetOrAdd(i, []).Add(dest);
                        reverse.GetOrAdd(dest, []).Add(i);
                    }
                }
            }

            // Normal Kahn's algorithm just uses a queue but we want to order
            // by priority (within the bounds of a topological sort) so we use
            // a priority queue.
            PriorityQueue<int, int> queue = new();
            List<Converter> converters = new(processor.converters.Count);
            foreach (var (dest, sources) in reverse.KSPEnumerate())
            {
                if (sources.Count != 0)
                    continue;

                queue.Enqueue(dest, processor.converters[dest].behaviour.Priority);
            }

            while (forward.Count != 0)
            {
                if (!queue.TryDequeue(out var element, out var _))
                {
                    // We've encountered a cycle. Normally a topological sort would
                    // stop here with an error, but we don't actually need a full
                    // topological sort so we just remove the first node and carry on.
                    element = forward.First().Key;
                }

                var dests = forward[element];
                forward.Remove(element);
                converters.Add(processor.converters[element]);

                foreach (var dest in dests)
                {
                    var sources = reverse[dest];
                    sources.Remove(element);

                    if (sources.Count == 0)
                        queue.Enqueue(dest, processor.converters[dest].behaviour.Priority);
                }
            }

            return converters;
        }

        private static Dictionary<string, InventoryState> ComputeInventoryConstraints(
            ResourceProcessor processor
        )
        {
            Dictionary<string, InventoryState> states = [];

            foreach (var (_, inventory) in processor.inventories.KSPEnumerate())
            {
                var state = InventoryState.Unconstrained;
                if (inventory.Full)
                    state |= InventoryState.Full;
                if (inventory.Empty)
                    state |= InventoryState.Empty;

                state &= states.GetValueOr(inventory.resourceName, InventoryState.Zero);
                states[inventory.resourceName] = state;
            }

            return states;
        }

        /// <summary>
        /// Check if computed resource rates satisfy resource constraints.
        /// </summary>
        ///
        /// <remarks>
        /// This is meant for testing only. Note that it can be false if there
        /// are converters with <c>DumpExcess = true</c> on some of their outputs.
        /// </remarks>
        /// <returns></returns>
        public bool RatesSatisfyConstraints()
        {
            foreach (var (resource, rate) in resourceRates.KSPEnumerate())
            {
                var constraint = constraints[resource];

                if ((constraint & InventoryState.Full) == InventoryState.Full && rate > 0.0)
                    return false;
                if ((constraint & InventoryState.Empty) == InventoryState.Empty && rate < 0.0)
                    return false;
            }

            return true;
        }

        static double Clamp(double value, double lo, double hi)
        {
            return Math.Max(Math.Min(value, hi), lo);
        }
    }
}
