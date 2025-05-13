using System;
using System.Collections.Generic;
using Smooth.Collections;
using UnifiedBackgroundProcessing.Collections;
using UnifiedBackgroundProcessing.Core;

namespace UnifiedBackgroundProcessing.Solver
{
    public enum InventoryState
    {
        /// <summary>
        /// There is no constraint on flow through this inventory.
        /// </summary>
        Unconstrained = 0,

        /// <summary>
        /// This inventory is empty. Inflow must be greater than or equal to outflow.
        /// </summary>
        Empty = 1,

        /// <summary>
        /// This inventory is full. Inflow must be greater than or equal to inflow.
        /// </summary>
        Full = 2,

        /// <summary>
        /// This inventory has size zero. Inflow must equal outflow.
        /// </summary>
        Zero = 3,
    }

    /// <summary>
    /// A graph
    /// </summary>
    public class ResourceGraph
    {
        public class InvalidMergeException(string message) : Exception(message) { }

        public class Inventory
        {
            public InventoryState state;
            public string resourceName;

            public Inventory(ResourceInventory inventory)
            {
                resourceName = inventory.resourceName;
                state = InventoryState.Unconstrained;
                if (inventory.Full)
                    state |= InventoryState.Full;
                if (inventory.Empty)
                    state |= InventoryState.Empty;
            }

            public void Merge(Inventory other)
            {
                if (resourceName != other.resourceName)
                {
                    throw new InvalidMergeException(
                        $"Attempted to merge two inventories with different resources ('{resourceName}' != '{other.resourceName}')"
                    );
                }

                state &= other.state;
            }
        }

        public class Converter
        {
            public struct Input
            {
                public double rate;

                public Input Merge(Input other)
                {
                    return new() { rate = rate + other.rate };
                }
            }

            public struct Output
            {
                public double rate;
                public bool dumpExcess;

                public Output Merge(Output other)
                {
                    if (dumpExcess != other.dumpExcess)
                    {
                        throw new InvalidMergeException(
                            "attempted to merge two outputs that had different dumpExcess values"
                        );
                    }

                    return new() { rate = rate + other.rate, dumpExcess = dumpExcess };
                }
            }

            /// <summary>
            /// The set of "real" converters that have been merged into this
            /// logical converter.
            /// </summary>
            public HashSet<int> ids = [];

            /// <summary>
            /// The input resources for this converter and their rates.
            /// </summary>
            public Dictionary<string, Input> inputs = [];

            /// <summary>
            /// The output resources for this converter and their rates.
            /// </summary>
            public Dictionary<string, Output> outputs = [];

            /// <summary>
            /// Resources for which this converter is at capacity.
            /// </summary>
            public HashSet<string> constraints = [];

            private Converter(
                int id,
                Core.Converter converter,
                Dictionary<string, double> totals,
                out bool disabled
            )
            {
                ids.Add(id);
                disabled = false;

                foreach (var (resource, required) in converter.required)
                {
                    if (ApproxEqual(required.Ratio, totals[resource]))
                        constraints.Add(resource);

                    if (required.Ratio > totals[resource])
                        disabled = true;
                }

                foreach (var (resource, input) in converter.inputs)
                    inputs.Add(resource, new Input() { rate = input.Ratio });

                foreach (var (resource, output) in converter.outputs)
                {
                    outputs.Add(
                        resource,
                        new Output() { rate = output.Ratio, dumpExcess = output.DumpExcess }
                    );
                }
            }

            public static Converter Build(
                int id,
                Core.Converter converter,
                Dictionary<string, double> totals
            )
            {
                var built = new Converter(id, converter, totals, out bool disabled);
                if (!disabled)
                    return null;
                return built;
            }

            public void Merge(Converter other)
            {
                ids.AddAll(other.ids);

                foreach (var (resource, input) in other.inputs)
                {
                    if (!inputs.TryAdd(resource, input))
                        inputs[resource] = inputs[resource].Merge(input);
                }

                foreach (var (resource, output) in other.outputs)
                {
                    if (!outputs.TryAdd(resource, output))
                        outputs[resource] = outputs[resource].Merge(output);
                }
            }

            public bool CanMerge(Converter other)
            {
                return constraints == other.constraints;
            }
        }

        public Dictionary<int, Inventory> inventories = [];
        public Dictionary<int, Converter> converters = [];
        public EdgeMap<int, int> inputs = new();
        public EdgeMap<int, int> outputs = new();

        public UnionFind inventoryIds;
        public UnionFind converterIds;

        public ResourceGraph(ResourceProcessor processor)
        {
            int index;

            inventoryIds = new UnionFind(processor.inventories.Count);
            converterIds = new UnionFind(processor.converters.Count);

            Dictionary<string, double> totals = [];
            Dictionary<InventoryId, int> idMap = [];

            index = 0;
            foreach (var (_, inventory) in processor.inventories)
            {
                int id = index++;

                if (!totals.TryAdd(inventory.resourceName, inventory.amount))
                    totals[inventory.resourceName] += inventory.amount;

                inventories.Add(id, new Inventory(inventory));
                idMap.Add(inventory.Id, id);
            }

            index = 0;
            foreach (var converter in processor.converters)
            {
                var id = index++;
                var built = Converter.Build(id, converter, totals);
                if (built == null)
                    continue;

                converters.Add(id, built);

                foreach (var (resourceName, inventories) in converter.pull)
                {
                    if (!converter.inputs.ContainsKey(resourceName))
                        continue;

                    foreach (var partId in inventories)
                        inputs.Add(id, idMap[new InventoryId(partId, resourceName)]);
                }

                foreach (var (resourceName, inventories) in converter.push)
                {
                    if (!converter.inputs.ContainsKey(resourceName))
                        continue;

                    foreach (var partId in inventories)
                        outputs.Add(id, idMap[new InventoryId(partId, resourceName)]);
                }
            }
        }

        /// <summary>
        /// Merge together all inventories that have identical edge-sets.
        /// </summary>
        ///
        /// <remarks>
        /// If the inventories have the same sets of converters interacting
        /// with them (and contain the same resource) then they can be
        /// merged together into one "logical" inventory. This helps with
        /// constraints as well, because merging together a full inventory
        /// with an partially filled inventory results in a partially full
        /// inventory, which adds no constraints on resource flow.
        /// </remarks>
        public void MergeEquivalentInventories()
        {
            HashSet<int> removed = [];

            foreach (var (inventoryId, inventory) in inventories.KSPEnumerate())
            {
                if (removed.Contains(inventoryId))
                    continue;

                var inputEdges = inputs.GetInventoryEntry(inventoryId);
                var outputEdges = outputs.GetInventoryEntry(inventoryId);

                foreach (var (otherId, otherInv) in inventories.KSPEnumerate())
                {
                    if (otherId <= inventoryId)
                        continue;
                    if (inventory.resourceName != otherInv.resourceName)
                        continue;
                    if (removed.Contains(otherId))
                        continue;

                    if (inputEdges != inputs.GetInventoryEntry(otherId))
                        continue;
                    if (outputEdges != outputs.GetInventoryEntry(otherId))
                        continue;

                    inventory.Merge(otherInv);
                    inventoryIds.Union(inventoryId, otherId);
                    inputs.RemoveInventory(otherId);
                    outputs.RemoveInventory(otherId);
                    removed.Add(otherId);
                }
            }

            foreach (var id in removed)
                inventories.Remove(id);
        }

        // /// <summary>
        // /// Merge together all converters that have equivalent edge-sets.
        // /// </summary>
        // public void MergeEquivalentConverters()
        // {
        //     HashSet<int> removed = [];

        //     foreach (var (converterId, converter) in converters.KSPEnumerate())
        //     {
        //         if (removed.Contains(converterId))
        //             continue;

        //         var consumeEdges = inputs.GetConverterEntry(converterId);
        //         var produceEdges = outputs.GetConverterEntry(converterId);

        //         foreach(var (otherId, otherInv))
        //     }
        // }

        private static bool ApproxEqual(double a, double b, double epsilon = 1e-6)
        {
            return Math.Abs(a - b) < epsilon;
        }
    }
}
