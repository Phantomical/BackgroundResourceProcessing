using System;
using System.Collections.Generic;
using BackgroundResourceProcessing.Collections.Burst;
using BackgroundResourceProcessing.Core;
using BackgroundResourceProcessing.Tracing;
using Unity.Collections;
using static BackgroundResourceProcessing.Collections.KeyValuePairExt;

namespace BackgroundResourceProcessing.BurstSolver;

internal struct ResourceGraph
{
    public RawIntMap<GraphInventory> inventories;
    public RawIntMap<GraphConverter> converters;

    // - column index is inventory ID
    // - row index is converter ID
    public AdjacencyMatrix inputs;
    public AdjacencyMatrix outputs;
    public AdjacencyMatrix constraints;

    public RawArray<int> inventoryIds;
    public RawArray<int> converterIds;

    public ResourceGraph(ResourceProcessor processor)
    {
        using var _span = new TraceSpan("new ResourceGraph");

        var nInventories = processor.inventories.Count;
        var nConverters = processor.converters.Count;

        inventoryIds = new(nInventories);
        converterIds = new(nConverters);

        for (int i = 0; i < nInventories; ++i)
            inventoryIds[i] = i;
        for (int i = 0; i < nConverters; ++i)
            converterIds[i] = i;

        inventories = new(nInventories);
        converters = new(nConverters);

        inputs = new AdjacencyMatrix(nConverters, nInventories);
        outputs = new AdjacencyMatrix(nConverters, nInventories);
        constraints = new AdjacencyMatrix(nConverters, nInventories);

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

            if (!SatisfiesConstraints(converter, ref conv))
                continue;

            converters.Add(i, conv);
            inputs[i].OrWith(converter.Pull.SubSlice(nInventories));
            outputs[i].OrWith(converter.Push.SubSlice(nInventories));

            if (!conv.constraints.IsEmpty)
            {
                // We only want to keep constraint edges if they are relevant to an
                // active constraint. That way we can merge converters with different
                // constraints as long as those constraints are not currently active.
                var crow = constraints[i];
                foreach (int inventoryId in converter.Constraint)
                {
                    ref var inventory = ref inventories[inventoryId];
                    if (!conv.constraints.ContainsKey(inventory.resourceId))
                        continue;

                    crow[inventoryId] = true;
                }
            }
        }
        cspan.Dispose();
    }

    private readonly bool SatisfiesConstraints(
        Core.ResourceConverter rconv,
        ref GraphConverter gconv
    )
    {
        gconv.constraints.Reserve(rconv.Required.Count);

        foreach (var (resource, required) in rconv.Required)
        {
            var state = required.State;
            if (state == ConstraintState.UNSET)
                throw new Exception("Converter ConstraintState was UNSET");
            if (state == ConstraintState.DISABLED)
                return false;
            if (state == ConstraintState.BOUNDARY)
                gconv.constraints.AddUnchecked(resource, required.Constraint);
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
    public unsafe void MergeEquivalentInventories()
    {
        int words = inputs.ColumnWords;
        ulong* mem = stackalloc ulong[words * 2];

        BitSet removed = new(mem, words);
        BitSet equal = new(mem + words, words);

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

        var src = removed.Span.Span;
        for (int r = 0; r < inputs.Rows; r++)
        {
            var iDst = inputs[r].Span;
            var oDst = outputs[r].Span;
            var cDst = constraints[r].Span;

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
        foreach (var converterId in converters.Keys)
        {
            if (!converters.ContainsKey(converterId))
                continue;
            ref var converter = ref converters[converterId];

            var inputEdges = inputs[converterId];
            var outputEdges = outputs[converterId];
            var constraintEdges = constraints[converterId];

            foreach (var (otherId, otherConverter) in converters.GetEnumeratorAt(converterId + 1))
            {
                var otherInputs = inputs[otherId];
                var otherOutputs = outputs[otherId];
                var otherConstraints = constraints[otherId];

                if (inputEdges != otherInputs)
                    continue;
                if (outputEdges != otherOutputs)
                    continue;
                if (constraintEdges != otherConstraints)
                    continue;

                if (!converter.CanMergeWith(otherConverter))
                    continue;

                otherInputs.Clear();
                otherOutputs.Clear();
                otherConstraints.Clear();

                converter.Merge(otherConverter);
                converterIds[otherId] = converterId;
                converters.Remove(otherId);
            }
        }
    }
}
