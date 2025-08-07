using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using BackgroundResourceProcessing.Collections;
using BackgroundResourceProcessing.Core;
using BackgroundResourceProcessing.Tracing;
using BackgroundResourceProcessing.Utils;

namespace BackgroundResourceProcessing.Solver;

public class InvalidMergeException(string message) : Exception(message) { }

internal static class AdjacencyMatrixExt
{
    public static AdjacencyMatrix Create(int inventoryCount, int converterCount)
    {
        return new(converterCount, inventoryCount);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static BitSliceX GetConverterEntry(this AdjacencyMatrix matrix, int converter)
    {
        return matrix.GetRow(converter);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
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
internal struct GraphInventory
{
    public InventoryState state;
    public string resourceName;
    public int resourceId;

    public double amount;
    public double maxAmount;

    public int baseId;

    public GraphInventory(ResourceInventory inventory, int id)
    {
        resourceName = inventory.resourceName;
        resourceId = resourceName.GetHashCode();
        baseId = id;
        amount = inventory.amount;
        maxAmount = inventory.maxAmount;

        state = InventoryState.Unconstrained;
        if (inventory.Full)
            state |= InventoryState.Full;
        if (inventory.Empty)
            state |= InventoryState.Empty;
    }

    public void Merge(GraphInventory other)
    {
        if (resourceId != other.resourceId)
            ThrowInvalidMergeException(other.resourceName);
        state &= other.state;
        amount += other.amount;
        maxAmount += other.maxAmount;
        if (baseId > other.baseId)
            baseId = other.baseId;
    }

    public override readonly string ToString()
    {
        return $"[{state}, {resourceName}]";
    }

    private void ThrowInvalidMergeException(string otherResourceName)
    {
        throw new InvalidMergeException(
            $"Attempted to merge two inventories with different resources ('{resourceName}' != '{otherResourceName}')"
        );
    }
}

internal struct GraphConverter
{
    private static readonly SortedMap<int, Constraint> Empty = new(0);

    public int baseId;
    public double weight = 0.0;

    /// <summary>
    /// The input resources for this converter and their rates.
    /// </summary>
    public SortedMap<int, ResourceRatio> inputs;

    /// <summary>
    /// The output resources for this converter and their rates.
    /// </summary>
    public SortedMap<int, ResourceRatio> outputs;

    /// <summary>
    /// Resources for which this converter is at capacity.
    /// </summary>
    public SortedMap<int, Constraint> constraints;

    private bool owned = false;

    public GraphConverter(int id, Core.ResourceConverter converter)
    {
        // using var span = new TraceSpan("new GraphConverter");

        baseId = id;
        weight = GetPriorityWeight(converter.priority);

        // inputs = new(converter.inputs.Count);
        // outputs = new(converter.outputs.Count);
        if (converter.required.Count == 0)
            constraints = Empty;
        else
            constraints = new(converter.required.Count);

        inputs = converter.inputs;
        outputs = converter.outputs;
    }

    public void Merge(GraphConverter other)
    {
        if (inputs.Count != other.inputs.Count)
            throw new InvalidMergeException(
                "Attempted to merge converters with different input resources"
            );
        if (outputs.Count != other.outputs.Count)
            throw new InvalidMergeException(
                "Attempted to merge converters with different output resources"
            );

        if (!owned)
        {
            inputs = inputs.Clone();
            outputs = outputs.Clone();
            owned = true;
        }

        for (int i = 0; i < inputs.Count; ++i)
        {
            ref var entry1 = ref inputs.Entries[i];
            ref var entry2 = ref other.inputs.Entries[i];

            if (entry1.Key != entry2.Key)
                throw new InvalidMergeException(
                    "Attempted to merge converters with different input resources"
                );

            Merge(ref entry1.Value, entry2.Value);
        }

        for (int i = 0; i < outputs.Count; ++i)
        {
            ref var entry1 = ref outputs.Entries[i];
            ref var entry2 = ref other.outputs.Entries[i];

            if (entry1.Key != entry2.Key)
                throw new InvalidMergeException(
                    "Attempted to merge converters with different output resources"
                );

            Merge(ref entry1.Value, entry2.Value);
        }

        weight += other.weight;
        if (baseId > other.baseId)
            baseId = other.baseId;

#if DEBUG
        foreach (var (resource, constraint) in other.constraints)
        {
            if (constraints.TryGetValue(resource, out var existing))
                Debug.Assert(
                    existing == constraint,
                    $"Merged GraphConverters with different constraints for resource {resource}"
                );
        }
#endif
    }

    public bool CanMergeWith(GraphConverter other)
    {
        if (!inputs.KeysEqual(other.inputs))
            return false;
        if (!outputs.KeysEqual(other.outputs))
            return false;

        return constraints.Equals(other.constraints);
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
        var inputs = this.inputs.Select((entry) => entry.Value.ResourceName);
        var outputs = this.outputs.Select((entry) => entry.Value.ResourceName);

        return $"{string.Join(",", inputs)} => {string.Join(",", outputs)}";
    }

    internal static void Merge(ref ResourceRatio a, ResourceRatio b)
    {
        a.Ratio += b.Ratio;
    }
}

internal class ResourceGraph
{
    public RefIntMap<GraphInventory> inventories;
    public RefIntMap<GraphConverter> converters;

    // - X-axis is inventory ID
    // - Y-axis is converter ID
    public AdjacencyMatrix inputs;
    public AdjacencyMatrix outputs;
    public AdjacencyMatrix constraints;

    public int[] inventoryIds;
    public int[] converterIds;

    public ResourceGraph(ResourceProcessor processor)
    {
        using var _span = new TraceSpan("new ResourceGraph");

        var nInventories = processor.inventories.Count;
        var nConverters = processor.converters.Count;

        inventoryIds = new int[nInventories];
        for (int i = 0; i < nInventories; ++i)
            inventoryIds[i] = i;

        converterIds = new int[nConverters];
        for (int i = 0; i < nConverters; ++i)
            converterIds[i] = i;

        inventories = new(nInventories);
        converters = new(nConverters);

        Dictionary<InventoryId, int> idMap = [];

        inputs = AdjacencyMatrixExt.Create(nInventories, nConverters);
        outputs = AdjacencyMatrixExt.Create(nInventories, nConverters);
        constraints = AdjacencyMatrixExt.Create(nInventories, nConverters);

        var ispan = new TraceSpan("Inventories");
        for (int i = 0; i < nInventories; ++i)
        {
            var inventory = processor.inventories[i];
            inventories.Add(i, new(inventory, i));
        }
        ispan.Dispose();

        var cspan = new TraceSpan("Converters");
        for (int i = 0; i < nConverters; ++i)
        {
            var converter = processor.converters[i];
            var conv = new GraphConverter(i, converter);

            if (!SatisfiesConstraints(processor, converter, ref conv))
                continue;

            converters.Add(i, conv);
            inputs.GetConverterEntry(i).OrWith(converter.Pull.SubSlice(nInventories));
            outputs.GetConverterEntry(i).OrWith(converter.Push.SubSlice(nInventories));
            constraints.GetConverterEntry(i).OrWith(converter.Constraint.SubSlice(nInventories));
        }
        cspan.Dispose();
    }

    private bool SatisfiesConstraints(
        ResourceProcessor processor,
        Core.ResourceConverter rconv,
        ref GraphConverter gconv
    )
    {
        using var cbuilder = gconv.constraints.CreateBuilder();

        foreach (var (resource, required) in rconv.required)
        {
            double total = 0.0;

            foreach (var invId in rconv.Constraint)
            {
                var inventory = processor.inventories[invId];
                if (inventory.resourceName == required.ResourceName)
                    total += inventory.amount;
            }

            if (MathUtil.ApproxEqual(required.Amount, total, ResourceProcessor.ResourceEpsilon))
            {
                cbuilder.AddUnchecked(resource, required.Constraint);
                continue;
            }

            switch (required.Constraint)
            {
                case Constraint.AT_LEAST:
                    if (total < required.Amount)
                        return false;
                    break;
                case Constraint.AT_MOST:
                    if (total > required.Amount)
                        return false;
                    break;
                default:
                    LogUtil.Warn(
                        $"Got unexpected constraint {required.Constraint} on resource {required.ResourceName}. This converter will be ignored."
                    );
                    return false;
            }
        }

        return true;
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
        var inventoryCount = inventoryIds.Length;

        foreach (var inventoryId in inventories.Keys)
        {
            if (!inventories.ContainsKey(inventoryId))
                continue;
            ref var inventory = ref inventories[inventoryId];

            // This ensures that the only bits are set are those that:
            // - Are in the range (inventoryId, inventoryCount), and,
            // - Have not already been marked for removal
            equal.CopyInverseFrom(removed);
            equal.ClearOutsideRange(inventoryId + 1, inventoryCount);

            // We then unset any indices whose connections (both inputs and
            // outputs) are not exactly the same as the column we are
            // currently checking.
            inputs.RemoveUnequalColumns(equal, inventoryId);
            outputs.RemoveUnequalColumns(equal, inventoryId);
            constraints.RemoveUnequalColumns(equal, inventoryId);

            foreach (var otherId in equal)
            {
                var otherInv = inventories[otherId];
                if (inventory.resourceId != otherInv.resourceId)
                {
                    equal[otherId] = false;
                    continue;
                }

                removed[otherId] = true;

                inventory.Merge(otherInv);
                inventoryIds[otherId] = inventoryId;
                inventories.Remove(otherId);
            }
        }

        var src = removed.Bits;
        for (int r = 0; r < inputs.Rows; r++)
        {
            var iDst = inputs.GetRow(r).Bits;
            var oDst = outputs.GetRow(r).Bits;
            var cDst = constraints.GetRow(r).Bits;

            for (int w = 0; w < src.Length; ++w)
            {
                iDst[w] &= ~src[w];
                oDst[w] &= ~src[w];
                cDst[w] &= ~src[w];
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

        foreach (var converterId in converters.Keys)
        {
            if (!converters.ContainsKey(converterId))
                continue;
            ref var converter = ref converters[converterId];

            var inputEdges = inputs.GetConverterEntry(converterId);
            var outputEdges = outputs.GetConverterEntry(converterId);
            var constraintEdges = constraints.GetConverterEntry(converterId);

            foreach (var (otherId, otherConverter) in converters.GetEnumeratorAt(converterId + 1))
            {
                var otherInputs = inputs.GetConverterEntry(otherId);
                var otherOutputs = outputs.GetConverterEntry(otherId);
                var otherConstraints = constraints.GetConverterEntry(otherId);

                if (inputEdges != otherInputs)
                    continue;
                if (outputEdges != otherOutputs)
                    continue;
                if (constraintEdges != otherConstraints)
                    continue;

                if (!converter.CanMergeWith(otherConverter))
                    continue;

                otherInputs.Zero();
                otherOutputs.Zero();
                otherConstraints.Zero();

                converter.Merge(otherConverter);
                converterIds[otherId] = converterId;
                converters.Remove(otherId);
            }
        }
    }
}
