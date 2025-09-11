using System;
using System.Collections.Generic;
using System.Linq;
using BackgroundResourceProcessing.Collections.Burst;
using Unity.Burst.CompilerServices;
using Unity.Collections;
using static BackgroundResourceProcessing.Collections.KeyValuePairExt;

namespace BackgroundResourceProcessing.BurstSolver;

internal struct GraphConverter
{
    public int baseId;
    public double weight = 0.0;

    /// <summary>
    /// The input resources for this converter and their rates.
    /// </summary>
    public LinearMap<int, GraphRatio> inputs;

    /// <summary>
    /// The output resources for this converter and their rates.
    /// </summary>
    public LinearMap<int, GraphRatio> outputs;

    /// <summary>
    /// Resources for which this converter is at capacity.
    /// </summary>
    public LinearMap<int, Constraint> constraints;

    public GraphConverter(int id, Core.ResourceConverter converter, AllocatorHandle allocator)
    {
        baseId = id;
        weight = GetPriorityWeight(converter.Priority);

        inputs = CreateRatioMap(converter.Inputs, allocator);
        outputs = CreateRatioMap(converter.Outputs, allocator);
        constraints = new(converter.Required.Count, allocator);
    }

    private static LinearMap<int, GraphRatio> CreateRatioMap(
        Collections.SortedMap<int, ResourceRatio> map,
        AllocatorHandle allocator
    )
    {
        LinearMap<int, GraphRatio> res = new(map.Count, allocator);
        foreach (var (resourceId, ratio) in map)
            res.AddUnchecked(resourceId, new(ratio));
        return res;
    }

    [IgnoreWarning(1370)]
    public void Merge(in GraphConverter other)
    {
        if (inputs.Count != other.inputs.Count)
            throw new InvalidOperationException(
                "Attempted to merge converters with different input resources"
            );
        if (outputs.Count != other.outputs.Count)
            throw new InvalidOperationException(
                "Attempted to merge converters with different output resources"
            );

        for (int i = 0; i < inputs.Count; ++i)
        {
            ref var entry1 = ref inputs.GetEntryAtIndex(i);
            ref var entry2 = ref other.inputs.GetEntryAtIndex(i);

            if (entry1.Key != entry2.Key)
                throw new InvalidOperationException(
                    "Attempted to merge converters with different input resources"
                );

            Merge(ref entry1.Value, entry2.Value);
        }

        for (int i = 0; i < outputs.Count; ++i)
        {
            ref var entry1 = ref outputs.GetEntryAtIndex(i);
            ref var entry2 = ref other.outputs.GetEntryAtIndex(i);

            if (entry1.Key != entry2.Key)
                throw new InvalidOperationException(
                    "Attempted to merge converters with different input resources"
                );

            Merge(ref entry1.Value, entry2.Value);
        }

        weight += other.weight;
        baseId = Math.Min(baseId, other.baseId);
    }

    internal static void Merge(ref GraphRatio a, GraphRatio b)
    {
        a.Ratio += b.Ratio;
    }

    public readonly bool CanMergeWith(GraphConverter other)
    {
        if (!inputs.KeysEqual(other.inputs))
            return false;
        if (!outputs.KeysEqual(other.outputs))
            return false;

        return ConstraintEquals(in constraints, in other.constraints);
    }

    private static double GetPriorityWeight(int priority)
    {
        // This is chosen so that B^10 ~= 1e6, which should be sufficiently
        // small that the simplex solver remains well-conditioned.
        const double B = 3.98107;

        priority = Math.Max(Math.Min(priority, 10), -10);

        return Math.Pow(B, priority);
    }

    internal static bool ConstraintEquals(
        in LinearMap<int, Constraint> a,
        in LinearMap<int, Constraint> b
    )
    {
        if (a.Count != b.Count)
            return false;

        for (int i = 0; i < a.Count; ++i)
        {
            ref var ae = ref a.GetEntryAtIndex(i);
            if (!b.TryGetIndex(ae.Key, out var j))
                return false;

            ref var be = ref b.GetEntryAtIndex(j);

            if (ae.Value != be.Value)
                return false;
        }

        return true;
    }
}
