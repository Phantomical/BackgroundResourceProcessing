using System;
using System.Runtime.CompilerServices;
using BackgroundResourceProcessing.Collections.Burst;
using BackgroundResourceProcessing.Core;
using BackgroundResourceProcessing.Tracing;
using Unity.Burst.CompilerServices;
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

    public ResourceGraph(ResourceProcessor processor, AllocatorHandle allocator)
    {
        using var _span = new TraceSpan("new ResourceGraph");

        var nInventories = processor.inventories.Count;
        var nConverters = processor.converters.Count;

        inventoryIds = new(nInventories, allocator);
        converterIds = new(nConverters, allocator);

        for (int i = 0; i < nInventories; ++i)
            inventoryIds[i] = i;
        for (int i = 0; i < nConverters; ++i)
            converterIds[i] = i;

        inventories = new(nInventories, allocator);
        converters = new(nConverters, allocator);

        inputs = new AdjacencyMatrix(nConverters, nInventories, allocator);
        outputs = new AdjacencyMatrix(nConverters, nInventories, allocator);
        constraints = new AdjacencyMatrix(nConverters, nInventories, allocator);

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
            var conv = new GraphConverter(i, converter, allocator);

            if (!SatisfiesConstraints(converter, ref conv))
                continue;

            converters.Add(i, conv);
            inputs[i].Assign(converter.Pull.Bits);
            outputs[i].Assign(converter.Push.Bits);

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

    private static bool SatisfiesConstraints(Core.ResourceConverter rconv, ref GraphConverter gconv)
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
            ref var inventory = ref inventories.GetUnchecked(inventoryId);

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
                var otherInv = inventories.GetUnchecked(otherId);
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
    public unsafe void MergeEquivalentConverters()
    {
        AdjacencyMatrix equal = new(inputs.Rows, inputs.Rows, inventoryIds.Allocator);
        equal.FillUpperDiagonal();

        inputs.RemoveUnequalRows(equal);
        outputs.RemoveUnequalRows(equal);
        constraints.RemoveUnequalRows(equal);

        for (int converterId = 0; converterId < equal.Rows; converterId++)
        {
            if (!converters.ContainsKey(converterId))
                continue;
            ref var converter = ref converters.GetUnchecked(converterId);
            var row = equal[converterId];

            foreach (var otherId in row)
            {
                if (!converters.ContainsKey(otherId))
                    continue;
                ref var other = ref converters.GetUnchecked(otherId);

                if (!converter.CanMergeWith(in other))
                    continue;

                inputs[otherId].Clear();
                outputs[otherId].Clear();
                constraints[otherId].Clear();

                converter.Merge(other);
                converterIds[otherId] = converterId;
                converters.Remove(otherId);
            }
        }

        /*
        foreach (var converterId in converters.Keys)
        {
            if (!converters.ContainsKey(converterId))
                continue;
            ref var converter = ref converters.GetUnchecked(converterId);

            BurstUtil.Assume(converterId < inputs.Rows);
            BurstUtil.Assume(converterId < outputs.Rows);
            BurstUtil.Assume(converterId < constraints.Rows);

            var inputEdges = inputs[converterId];
            var outputEdges = outputs[converterId];
            var constraintEdges = constraints[converterId];

            ulong* aie = inputEdges.Span.Data;
            ulong* aoe = outputEdges.Span.Data;
            ulong* ace = constraintEdges.Span.Data;

            foreach (var (otherId, otherConverter) in converters.GetEnumeratorAt(converterId + 1))
            {
                BurstUtil.Assume(otherId < inputs.Rows);
                BurstUtil.Assume(otherId < outputs.Rows);
                BurstUtil.Assume(otherId < constraints.Rows);

                var otherInputs = inputs[otherId];
                var otherOutputs = outputs[otherId];
                var otherConstraints = constraints[otherId];

                ulong* bie = otherInputs.Span.Data;
                ulong* boe = otherOutputs.Span.Data;
                ulong* bce = otherConstraints.Span.Data;

                for (int i = 0; i < inputs.ColumnWords; ++i)
                {
                    if (aie[i] != bie[i] || aoe[i] != boe[i] || ace[i] != bce[i])
                        goto LOOP_END;
                }

                if (!converter.CanMergeWith(otherConverter))
                    continue;

                otherInputs.Clear();
                otherOutputs.Clear();
                otherConstraints.Clear();

                converter.Merge(otherConverter);
                converterIds[otherId] = converterId;
                converters.Remove(otherId);

            LOOP_END:
                ;
            }
        }
        */
    }
}
