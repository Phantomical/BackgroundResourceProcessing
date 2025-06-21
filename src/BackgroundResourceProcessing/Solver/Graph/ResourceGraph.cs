using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using BackgroundResourceProcessing.Collections;
using BackgroundResourceProcessing.Core;
using BackgroundResourceProcessing.Tracing;
using BackgroundResourceProcessing.Utils;
using UnityEngine.UI;

namespace BackgroundResourceProcessing.Solver.Graph
{
    public class InvalidMergeException(string message) : Exception(message) { }

    internal static class AdjacencyMatrixExt
    {
        public static AdjacencyMatrix Create(int inventoryCount, int converterCount)
        {
            return new(converterCount, inventoryCount);
        }

        public static BitSliceX GetConverterEntry(this AdjacencyMatrix matrix, int converter)
        {
            return matrix.GetRow(converter);
        }

        public static BitSliceY GetInventoryEntry(this AdjacencyMatrix matrix, int inventory)
        {
            return matrix.GetColumn(inventory);
        }

        public static Edges ConverterToInventoryEdges(this AdjacencyMatrix matrix)
        {
            return new(matrix);
        }

        public ref struct Edges(AdjacencyMatrix matrix) : IEnumerator<Edge>
        {
            AdjacencyMatrix.RowEnumerator rows = new(matrix);
            BitSliceX.Enumerator inner = default;

            public readonly Edge Current => new(rows.Index, inner.Current);

            readonly object IEnumerator.Current => Current;

            public readonly Edges GetEnumerator()
            {
                return this;
            }

            public bool MoveNext()
            {
                while (true)
                {
                    if (inner.MoveNext())
                        return true;

                    if (!rows.MoveNext())
                        return false;

                    inner = new(rows.Current);
                }
            }

            public void Reset()
            {
                rows.Reset();
                inner = default;
            }

            public void Dispose() { }
        }

        [DebuggerDisplay("Converter = {Converter}, Inventory = {Inventory}")]
        public struct Edge(int converter, int inventory)
        {
            public int Converter = converter;
            public int Inventory = inventory;

            public readonly void Deconstruct(out int converter, out int inventory)
            {
                converter = Converter;
                inventory = Inventory;
            }
        }
    }

    [DebuggerDisplay("[{state}, {resourceName}, Count = {ids.Count}]")]
    internal class GraphInventory
    {
        public InventoryState state;
        public string resourceName;

        public HashSet<int> ids;

        public GraphInventory(ResourceInventory inventory, int id)
        {
            resourceName = inventory.resourceName;
            ids = [id];

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
            ids.UnionWith(other.ids);
        }

        public override string ToString()
        {
            return $"[{state}, {resourceName}, Count={ids.Count}]";
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

            public override readonly string ToString()
            {
                return $"{{rate={rate}}}";
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

            public override readonly string ToString()
            {
                return $"{{rate={rate},dumpExcess={dumpExcess}}}";
            }
        }

        public List<int> ids;
        public List<Core.ResourceConverter> converters;
        public double weight = 0.0;

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
        public Dictionary<string, Constraint> constraints = [];

        private GraphConverter(int id, Core.ResourceConverter converter)
        {
            ids = [id];
            converters = [converter];
            weight = GetPriorityWeight(converter.behaviour.Priority);
        }

        public static GraphConverter Build(
            int id,
            Core.ResourceConverter converter,
            Dictionary<string, double> totals
        )
        {
            using var span = new TraceSpan("GraphConverter.Build");

            var conv = new GraphConverter(id, converter);

            foreach (var (resource, required) in converter.required)
            {
                var total = totals.GetValueOr(resource, 0.0);
                if (MathUtil.ApproxEqual(required.Amount, total, ResourceProcessor.ResourceEpsilon))
                {
                    conv.constraints.Add(resource, required.Constraint);
                    continue;
                }

                switch (required.Constraint)
                {
                    case Constraint.AT_LEAST:
                        if (required.Amount > total)
                            return null;
                        break;
                    case Constraint.AT_MOST:
                        if (required.Amount < total)
                            return null;
                        break;
                    default:
                        LogUtil.Warn(
                            $"Got unexpected constraint {required.Constraint} on resource {required.ResourceName}. This converter will be ignored."
                        );
                        return null;
                }
            }

            foreach (var (resource, input) in converter.inputs)
                conv.inputs.Add(resource, new Input(input.Ratio));

            foreach (var (resource, output) in converter.outputs)
                conv.outputs.Add(resource, new Output(output.Ratio, output.DumpExcess));

            return conv;
        }

        public void Merge(GraphConverter other)
        {
            foreach (var (resource, input) in other.inputs)
                if (!inputs.TryAddExt(resource, input))
                    inputs[resource] = inputs[resource].Merge(input);

            foreach (var (resource, output) in other.outputs)
                if (!outputs.TryAddExt(resource, output))
                    outputs[resource] = outputs[resource].Merge(output);

            ids.AddRange(other.ids);
            converters.AddRange(other.converters);
            weight += other.weight;

            foreach (var (resource, constraint) in other.constraints)
            {
#if DEBUG
                if (constraints.TryGetValue(resource, out var existing))
                    Debug.Assert(
                        existing == constraint,
                        $"Merged GraphConverters with different constraints for resource {resource}"
                    );
#endif

                constraints[resource] = constraint;
            }
        }

        public bool CanMergeWith(GraphConverter other)
        {
            if (!inputs.KeysEqual(other.inputs))
                return false;
            if (!outputs.KeysEqual(other.outputs))
                return false;

            return DictionaryExtensions.DictEqual(constraints, other.constraints);
        }

        /// <summary>
        /// Combine resources that are present in both inputs and outputs into
        /// a net input or net output.
        /// </summary>
        public void CombineDuplicates()
        {
            List<string> removed = [];

            foreach (var (name, _input) in inputs)
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

        private static double GetPriorityWeight(int priority)
        {
            // This is chosen so that B^10 ~= 1e6, which should be sufficiently
            // small that the simplex solver remains well-conditioned.
            const double B = 3.98107;

            priority = Math.Max(Math.Min(priority, 10), -10);

            return Math.Pow(B, priority);
        }

        public override string ToString()
        {
            return $"{string.Join(",", inputs.Keys)} => {string.Join(",", outputs.Keys)}";
        }
    }

    internal class ResourceGraph
    {
        public IntMap<GraphInventory> inventories;
        public IntMap<GraphConverter> converters;

        // - X-axis is inventory ID
        // - Y-axis is converter ID
        public AdjacencyMatrix inputs;
        public AdjacencyMatrix outputs;

        public UnionFind inventoryIds;
        public UnionFind converterIds;

        public ResourceGraph(ResourceProcessor processor)
        {
            using var _span = new TraceSpan("new ResourceGraph");

            var nInventories = processor.inventories.Count;
            var nConverters = processor.converters.Count;

            inventoryIds = new UnionFind(nInventories);
            converterIds = new UnionFind(nConverters);

            inventories = new(nInventories);
            converters = new(nConverters);

            Dictionary<string, double> totals = [];
            Dictionary<InventoryId, int> idMap = [];

            inputs = AdjacencyMatrixExt.Create(nInventories, nConverters);
            outputs = AdjacencyMatrixExt.Create(nInventories, nConverters);

            for (int i = 0; i < nInventories; ++i)
            {
                var inventory = processor.inventories[i];
                inventories.Add(i, new(inventory, i));

                if (!totals.TryAddExt(inventory.resourceName, inventory.amount))
                    totals[inventory.resourceName] += inventory.amount;
            }

            for (int i = 0; i < nConverters; ++i)
            {
                var converter = processor.converters[i];
                var conv = GraphConverter.Build(i, converter, totals);
                if (conv == null)
                    continue;

                converters.Add(i, conv);

                foreach (var (resourceName, inventories) in converter.pull)
                {
                    if (!converter.inputs.ContainsKey(resourceName))
                        continue;

                    var row = inputs.GetConverterEntry(i);
                    foreach (var inventoryId in inventories)
                        row[inventoryId] = true;
                }

                foreach (var (resourceName, inventories) in converter.push)
                {
                    if (!converter.outputs.ContainsKey(resourceName))
                        continue;

                    var row = outputs.GetConverterEntry(i);
                    foreach (var inventoryId in inventories)
                        row[inventoryId] = true;
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
            using var span = new TraceSpan("ResourceGraph.MergeEquivalentInventories");
            BitSet removed = new(inputs.Columns);
            BitSet equal = new(inputs.Columns);

            // We need to save this at the start as we'll be removing
            // inventories as we go.
            var inventoryCount = inventories.Count;

            foreach (var (inventoryId, inventory) in inventories)
            {
                // This ensures that the only bits are set are those that:
                // - Are in the range (inventoryId, inventoryCount), and,
                // - Have not already been marked for removal
                equal.CopyInverseFrom(removed);
                equal.ClearUpTo(inventoryId + 1);
                equal.ClearUpFrom(inventoryCount);

                // We then unset any indices whose connections (both inputs and
                // outputs) are not exactly the same as the column we are
                // currently checking.
                inputs.RemoveUnequalColumns(equal, inventoryId);
                outputs.RemoveUnequalColumns(equal, inventoryId);

                foreach (var otherId in equal)
                {
                    if (!inventories.TryGetValue(otherId, out var otherInv))
                        continue;
                    if (inventory.resourceName != otherInv.resourceName)
                        continue;

                    removed[otherId] = true;

                    inventory.Merge(otherInv);
                    inventoryIds.Union(inventoryId, otherId);
                    inventories.Remove(otherId);
                }
            }

            var src = removed.Bits;
            for (int r = 0; r < inputs.Rows; r++)
            {
                var iDst = inputs.GetRow(r).Bits;
                var oDst = outputs.GetRow(r).Bits;

                for (int w = 0; w < src.Length; ++w)
                {
                    iDst[w] &= ~src[w];
                    oDst[w] &= ~src[w];
                }
            }
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
            using var span = new TraceSpan("ResourceGraph.MergeEquivalentConverters");

            foreach (var (converterId, converter) in converters)
            {
                var inputEdges = inputs.GetConverterEntry(converterId);
                var outputEdges = outputs.GetConverterEntry(converterId);

                foreach (
                    var (otherId, otherConverter) in converters.GetEnumeratorAt(converterId + 1)
                )
                {
                    if (!converter.CanMergeWith(otherConverter))
                        continue;

                    var otherInputs = inputs.GetConverterEntry(otherId);
                    var otherOutputs = outputs.GetConverterEntry(otherId);

                    if (inputEdges != otherInputs)
                        continue;
                    if (outputEdges != otherOutputs)
                        continue;

                    otherInputs.Zero();
                    otherOutputs.Zero();

                    converter.Merge(otherConverter);
                    converterIds.Union(converterId, otherId);
                    converters.Remove(otherId);
                }
            }
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
            using var _span = new TraceSpan("ResourceGraph.HasSplitResourceEdges");

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
        public IntMap<double> ComputeLogicalInventoryRates(
            IEnumerable<KeyValuePair<int, double>> rates
        )
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
                    inventoryRates[inventoryId] -= input.rate * rate;
                }

                foreach (var id2 in outputs.GetConverterEntry(converterId))
                {
                    var inventoryId = inventoryIds.Find(id2);
                    var inventory = inventories[inventoryId];

                    var output = converter.outputs[inventory.resourceName];

                    inventoryRates.TryAdd(inventoryId, 0.0);
                    inventoryRates[inventoryId] += output.rate * rate;
                }
            }

            return inventoryRates;
        }

        public IntMap<double> ExpandInventoryRates(
            IEnumerable<KeyValuePair<int, double>> inventoryRates,
            ResourceProcessor processor
        )
        {
            IntMap<double> result = new(processor.inventories.Count);
            List<int> available = [];
            foreach (var (id, rate) in inventoryRates)
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
                        if (real.Full)
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

        public IntMap<double> ComputeInventoryRates(
            IEnumerable<KeyValuePair<int, double>> rates,
            ResourceProcessor processor
        )
        {
            var logicalRates = ComputeLogicalInventoryRates(rates);
            return ExpandInventoryRates(logicalRates, processor);
        }
    }
}
