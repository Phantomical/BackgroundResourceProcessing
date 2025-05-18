using System;
using System.Collections.Generic;
using BackgroundResourceProcessing.Collections;
using BackgroundResourceProcessing.Core;
using BackgroundResourceProcessing.Utils;

namespace BackgroundResourceProcessing.Solver.Graph
{
    public class InvalidMergeException(string message) : Exception(message) { }

    internal class Inventory
    {
        public InventoryState state;
        public string resourceName;

        public List<InventoryId> ids;

        public Inventory(ResourceInventory inventory)
        {
            resourceName = inventory.resourceName;
            ids = [inventory.Id];

            state = InventoryState.Unconstrained;
            if (inventory.Full)
                state |= InventoryState.Full;
            if (inventory.Empty)
                state |= InventoryState.Empty;
        }

        public void Merge(Inventory other)
        {
            if (resourceName != other.resourceName)
                throw new InvalidMergeException(
                    $"Attempted to merge two inventories with different resources ('{resourceName}' != '{other.resourceName}')"
                );

            state &= other.state;
            ids.AddRange(other.ids);
        }
    }

    internal class Converter
    {
        public struct Input(double rate)
        {
            public double rate = rate;

            public Input Merge(Input other)
            {
                return new() { rate = rate + other.rate };
            }
        }

        public struct Output(double rate, bool dumpExcess)
        {
            public double rate = rate;

            public bool dumpExcess = dumpExcess;

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

        public List<int> ids;

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

        private Converter(int id)
        {
            ids = [id];
        }

        public static Converter Build(
            int id,
            Core.Converter converter,
            Dictionary<string, double> totals
        )
        {
            var conv = new Converter(id);

            foreach (var (resource, required) in converter.required.KSPEnumerate())
            {
                if (MathUtil.ApproxEqual(required.Ratio, totals[resource]))
                    conv.constraints.Add(resource);

                if (required.Ratio > totals[resource])
                    return null;
            }

            foreach (var (resource, input) in converter.inputs.KSPEnumerate())
                conv.inputs.Add(resource, new Input(input.Ratio));

            foreach (var (resource, output) in converter.outputs.KSPEnumerate())
                conv.outputs.Add(resource, new Output(output.Ratio, output.DumpExcess));

            return conv;
        }

        public void Merge(Converter other)
        {
            foreach (var (resource, input) in other.inputs.KSPEnumerate())
                if (!inputs.TryAddExt(resource, input))
                    inputs[resource] = inputs[resource].Merge(input);

            foreach (var (resource, output) in other.outputs.KSPEnumerate())
                if (!outputs.TryAddExt(resource, output))
                    outputs[resource] = outputs[resource].Merge(output);

            ids.AddRange(other.ids);
        }

        public bool CanMerge(Converter other)
        {
            return constraints == other.constraints;
        }
    }

    internal class ResourceGraph
    {
        public Dictionary<int, Inventory> inventories = [];
        public Dictionary<int, Converter> converters = [];

        public EdgeMap<int, int> inputs = new();
        public EdgeMap<int, int> outputs = new();

        public UnionFind inventoryIds;
        public UnionFind converterIds;

        public ResourceGraph(ResourceProcessor processor)
        {
            inventoryIds = new UnionFind(processor.inventories.Count);
            converterIds = new UnionFind(processor.converters.Count);

            Dictionary<string, double> totals = [];
            Dictionary<InventoryId, int> idMap = [];

            var index = 0;
            foreach (var (_, inventory) in processor.inventories.KSPEnumerate())
            {
                var id = index++;

                if (!totals.TryAddExt(inventory.resourceName, inventory.amount))
                    totals[inventory.resourceName] += inventory.amount;

                inventories.Add(id, new Inventory(inventory));
                idMap.Add(inventory.Id, id);
            }

            index = 0;
            foreach (var converter in processor.converters)
            {
                var id = index++;
                var conv = Converter.Build(id, converter, totals);
                converters.Add(id, conv);

                if (conv == null)
                    continue;

                foreach (var (resourceName, inventories) in converter.pull.KSPEnumerate())
                {
                    if (!converter.inputs.ContainsKey(resourceName))
                        continue;

                    foreach (var partId in inventories)
                        inputs.Add(id, idMap[new InventoryId(partId, resourceName)]);
                }

                foreach (var (resourceName, inventories) in converter.push.KSPEnumerate())
                {
                    if (!converter.outputs.ContainsKey(resourceName))
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

                    var otherInputs = inputs.GetInventoryEntry(otherId);
                    var otherOutputs = outputs.GetInventoryEntry(otherId);

                    if (!inputEdges.SetEquals(otherInputs))
                        continue;
                    if (!outputEdges.SetEquals(otherOutputs))
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
    }
}
