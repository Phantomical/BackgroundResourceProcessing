using System;
using System.Collections.Generic;
using System.Linq;
using BackgroundResourceProcessing.Collections;
using BackgroundResourceProcessing.Core;
using BackgroundResourceProcessing.Utils;
using Smooth.Collections;
using Steamworks;

namespace BackgroundResourceProcessing.Solver.Graph
{
    public class InvalidMergeException(string message) : Exception(message) { }

    internal class GraphInventory
    {
        public InventoryState state;
        public string resourceName;

        public List<InventoryId> ids;

        public GraphInventory(ResourceInventory inventory)
        {
            resourceName = inventory.resourceName;
            ids = [inventory.Id];

            state = InventoryState.Unconstrained;
            if (inventory.Full)
                state |= InventoryState.Full;
            if (inventory.Empty)
                state |= InventoryState.Empty;
        }

        public void Merge(GraphInventory other)
        {
            if (resourceName != other.resourceName)
                throw new InvalidMergeException(
                    $"Attempted to merge two inventories with different resources ('{resourceName}' != '{other.resourceName}')"
                );

            state &= other.state;
            ids.AddRange(other.ids);
        }
    }

    internal class GraphConverter
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
        public List<Converter> converters;

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

        private GraphConverter(int id, Converter converter)
        {
            ids = [id];
            converters = [converter];
        }

        public static GraphConverter Build(
            int id,
            Converter converter,
            Dictionary<string, double> totals
        )
        {
            var conv = new GraphConverter(id, converter);

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

        public void Merge(GraphConverter other)
        {
            foreach (var (resource, input) in other.inputs.KSPEnumerate())
                if (!inputs.TryAddExt(resource, input))
                    inputs[resource] = inputs[resource].Merge(input);

            foreach (var (resource, output) in other.outputs.KSPEnumerate())
                if (!outputs.TryAddExt(resource, output))
                    outputs[resource] = outputs[resource].Merge(output);

            ids.AddRange(other.ids);
            converters.AddRange(other.converters);
            constraints.AddAll(other.constraints);
        }

        public bool CanMergeWith(GraphConverter other)
        {
            if (!inputs.KeysEqual(other.inputs))
                return false;
            if (!outputs.KeysEqual(other.outputs))
                return false;

            return constraints.SetEquals(other.constraints);
        }

        /// <summary>
        /// Combine resources that are present in both inputs and outputs into
        /// a net input or net output.
        /// </summary>
        public void CombineDuplicates()
        {
            List<string> removed = [];

            foreach (var (name, _input) in inputs.KSPEnumerate())
            {
                var input = _input;
                if (!outputs.TryGetValue(name, out var output))
                    continue;

                if (output.rate < input.rate)
                {
                    input.rate -= output.rate;
                    outputs.Remove(name);
                }
                else if (output.rate > input.rate)
                {
                    output.rate -= input.rate;
                    removed.Add(name);
                }
                else
                {
                    outputs.Remove(name);
                    inputs.Remove(name);
                }
            }

            foreach (var name in removed)
                inputs.Remove(name);
        }
    }

    internal class ResourceGraph
    {
        public IntMap<GraphInventory> inventories;
        public IntMap<GraphConverter> converters;

        public EdgeMap<int, int> inputs = new();
        public EdgeMap<int, int> outputs = new();

        public UnionFind inventoryIds;
        public UnionFind converterIds;

        public ResourceGraph(ResourceProcessor processor)
        {
            inventoryIds = new UnionFind(processor.inventories.Count);
            converterIds = new UnionFind(processor.converters.Count);

            inventories = new(inventoryIds.Count);
            converters = new(converterIds.Count);

            Dictionary<string, double> totals = [];
            Dictionary<InventoryId, int> idMap = [];

            var index = 0;
            foreach (var (_, inventory) in processor.inventories.KSPEnumerate())
            {
                var id = index++;

                if (!totals.TryAddExt(inventory.resourceName, inventory.amount))
                    totals[inventory.resourceName] += inventory.amount;

                inventories.Add(id, new GraphInventory(inventory));
                idMap.Add(inventory.Id, id);
            }

            index = 0;
            foreach (var converter in processor.converters)
            {
                var id = index++;
                var conv = GraphConverter.Build(id, converter, totals);
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

            foreach (var (inventoryId, inventory) in inventories)
            {
                if (removed.Contains(inventoryId))
                    continue;

                var inputEdges = inputs.GetInventoryEntry(inventoryId);
                var outputEdges = outputs.GetInventoryEntry(inventoryId);

                foreach (var (otherId, otherInv) in inventories)
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

                    LogUtil.Debug(() => $"Merged inventories {inventoryId} and {otherId}");

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

        /// <summary>
        /// Merge together all converters that have identical edge-sets.
        /// </summary>
        ///
        /// <remarks>
        /// <para>
        ///   If two converters take/emit the same sets of resources to the same
        ///   sets of inventories then they can be merged together into a single
        ///   "logical" converter provided that they share the same set of required
        ///   resource constraints.
        /// </para>
        ///
        /// <para>
        ///   This helps decouple the graph because merging together a constrained
        ///   (empty or full) inventory with an unconstrained inventory results in
        ///   an unconstrained inventory.
        /// </para>
        /// </remarks>
        public void MergeEquivalentConverters()
        {
            HashSet<int> removed = [];

            foreach (var (converterId, converter) in converters)
            {
                if (removed.Contains(converterId))
                    continue;

                var inputEdges = inputs.GetConverterEntry(converterId);
                var outputEdges = outputs.GetConverterEntry(converterId);

                foreach (var (otherId, otherConverter) in converters)
                {
                    if (otherId <= converterId)
                        continue;
                    if (removed.Contains(otherId))
                        continue;
                    if (!converter.CanMergeWith(otherConverter))
                        continue;

                    var otherInputs = inputs.GetConverterEntry(otherId);
                    var otherOutputs = outputs.GetConverterEntry(otherId);

                    if (!inputEdges.SetEquals(otherInputs))
                        continue;
                    if (!outputEdges.SetEquals(otherOutputs))
                        continue;

                    LogUtil.Debug(() => $"Merged converters {converterId} and {otherId}");

                    converter.Merge(otherConverter);
                    converterIds.Union(converterId, otherId);
                    inputs.RemoveConverter(otherId);
                    outputs.RemoveConverter(otherId);
                    removed.Add(otherId);
                }
            }

            foreach (var id in removed)
                converters.Remove(id);
        }

        /// <summary>
        /// Returns whether any of the converters in this resource graph have a
        /// resource input our output that is being split among multiple inventories.
        /// </summary>
        ///
        /// <remarks>
        /// Generally, this is the case for most ships in KSP. However, usually
        /// the flow rules are uniform enough this will no longer be true after
        /// <see cref="MergeEquivalentInventories"/> is called.
        /// </remarks>
        public bool HasSplitResourceEdges()
        {
            HashSet<string> present = [];
            foreach (var (converterId, converter) in converters)
            {
                present.Clear();
                foreach (var inventoryId in inputs.GetConverterEntry(converterId))
                {
                    var inventory = inventories[inventoryIds.Find(inventoryId)];
                    if (!present.Add(inventory.resourceName))
                        return true;
                }

                present.Clear();
                foreach (var inventoryId in outputs.GetConverterEntry(converterId))
                {
                    var inventory = inventories[inventoryIds.Find(inventoryId)];
                    if (!present.Add(inventory.resourceName))
                        return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Compute the rates of change in individual inventories given the
        /// provided activation rates for individual converters.
        /// </summary>
        /// <param name="rates"></param>
        /// <returns></returns>
        /// <exception cref="NotImplementedException"></exception>
        public IntMap<double> ComputeLogicalInventoryRates(IEnumerable<KVPair<int, double>> rates)
        {
            // Rates for the logical inventories
            IntMap<double> inventoryRates = new(inventoryIds.Count);

            foreach (var (id1, rate) in rates)
            {
                var converterId = converterIds.Find(id1);
                var converter = converters[converterId];

                foreach (var id2 in inputs.GetConverterEntry(converterId))
                {
                    var inventoryId = inventoryIds.Find(id2);
                    var inventory = inventories[inventoryId];

                    var input = converter.inputs[inventory.resourceName];

                    inventoryRates.TryAdd(inventoryId, 0.0);
                    inventoryRates[inventoryId] -= input.rate;
                }

                foreach (var id2 in outputs.GetConverterEntry(converterId))
                {
                    var inventoryId = inventoryIds.Find(id2);
                    var inventory = inventories[inventoryId];

                    var output = converter.outputs[inventory.resourceName];

                    inventoryRates.TryAdd(inventoryId, 0.0);
                    inventoryRates[inventoryId] += output.rate;
                }
            }

            return inventoryRates;
        }

        public Dictionary<InventoryId, double> ComputeInventoryRates(
            IEnumerable<KVPair<int, double>> rates,
            ResourceProcessor processor
        )
        {
            var logicalRates = ComputeLogicalInventoryRates(rates);

            Dictionary<InventoryId, double> result = [];
            List<InventoryId> available = [];
            foreach (var (id, rate) in logicalRates)
            {
                available.Clear();
                var inventory = inventories[id];
                var total = 0.0;

                if (rate < 0.0)
                {
                    foreach (var subid in inventory.ids)
                    {
                        var real = processor.inventories[subid];
                        if (real.Empty)
                        {
                            result.Add(subid, 0.0);
                            continue;
                        }

                        available.Add(subid);
                        total += real.amount;
                    }

                    foreach (var subid in available)
                    {
                        var real = processor.inventories[subid];
                        result.Add(subid, rate * (real.amount / total));
                    }
                }
                else if (rate > 0.0)
                {
                    foreach (var subid in inventory.ids)
                    {
                        var real = processor.inventories[subid];
                        if (real.Empty)
                        {
                            result.Add(subid, 0.0);
                            continue;
                        }

                        available.Add(subid);
                        total += real.maxAmount - real.amount;
                    }

                    foreach (var subid in available)
                    {
                        var real = processor.inventories[subid];
                        result.Add(subid, rate * ((real.maxAmount - real.amount) / total));
                    }
                }
                else
                {
                    foreach (var subid in inventory.ids)
                        result.Add(subid, 0.0);
                }
            }

            return result;
        }
    }
}
