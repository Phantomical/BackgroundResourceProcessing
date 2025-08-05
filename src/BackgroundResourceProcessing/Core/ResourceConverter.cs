using System;
using System.Collections.Generic;
using System.Linq;
using BackgroundResourceProcessing.Behaviour;
using BackgroundResourceProcessing.Collections;
using BackgroundResourceProcessing.Utils;

namespace BackgroundResourceProcessing.Core;

/// <summary>
/// The concrete resource converter used by the background solver.
/// </summary>
///
/// <remarks>
/// This is how the
/// </remarks>
public class ResourceConverter(ConverterBehaviour behaviour)
{
    /// <summary>
    /// Bitsets indicating which inventories this converter pushes
    /// resources to.
    /// </summary>
    public DynamicBitSet Push = [];

    /// <summary>
    /// Bitsets indicating which inventories this converter pulls
    /// resources from.
    /// </summary>
    public DynamicBitSet Pull = [];

    /// <summary>
    /// Bitsets indicating which inventories this converter uses to
    /// determine whether it is resource-constrained, for each resource.
    /// </summary>
    public DynamicBitSet Constraint = [];

    /// <summary>
    /// The behaviour that indicates how this converter actually behaves.
    /// </summary>
    public ConverterBehaviour Behaviour { get; private set; } = behaviour;

    /// <summary>
    /// The resource inputs returned from the behaviour.
    /// </summary>
    public SortedMap<int, ResourceRatio> inputs = [];

    /// <summary>
    /// The resource outputs returned from the behaviour.
    /// </summary>
    public SortedMap<int, ResourceRatio> outputs = [];

    /// <summary>
    /// The resource requirements returned from the behaviour.
    /// </summary>
    public SortedMap<int, ResourceConstraint> required = [];

    /// <summary>
    /// The time at which the behaviour has said that its behaviour might
    /// change next.
    /// </summary>
    public double nextChangepoint = double.PositiveInfinity;

    /// <summary>
    /// The current rate at which this converter is running. This will
    /// always be a number in the range <c>[0, 1]</c>.
    /// </summary>
    public double rate = 0.0;

    /// <summary>
    /// The total amount of time that this converter has been active,
    /// taking into account the activation rate.
    /// </summary>
    ///
    /// <remarks>
    /// This isn't actually used for anything by background resource
    /// processing. Rather, it is provided for use by other users of the
    /// API.
    /// </remarks>
    public double activeTime = 0.0;

    public int priority = 0;

    public bool Refresh(VesselState state)
    {
        if (Behaviour == null)
            return false;

        try
        {
            var resources = Behaviour.GetResources(state);
            nextChangepoint = resources.NextChangepoint;

            if (nextChangepoint < state.CurrentTime)
            {
                LogUtil.Error(
                    $"Behaviour {Behaviour.GetType().Name} returned changepoint in the past. ",
                    "Setting changepoint to infinity instead."
                );
                nextChangepoint = double.PositiveInfinity;
            }

            bool changed = false;

            if (OverwriteRatios(ref inputs, resources.Inputs))
                changed = true;
            if (OverwriteRatios(ref outputs, resources.Outputs))
                changed = true;
            if (OverwriteConstraints(ref required, resources.Requirements))
                changed = true;

            return changed;
        }
        catch (Exception e)
        {
            LogUtil.Error($"{Behaviour.GetType().Name}.GetResources threw an exception: {e}");

            nextChangepoint = double.PositiveInfinity;
            inputs = [];
            outputs = [];
            required = [];

            return true;
        }
    }

    public void Load(ConfigNode node, Vessel vessel = null)
    {
        node.TryGetDouble("nextChangepoint", ref nextChangepoint);
        node.TryGetValue("rate", ref rate);

        foreach (var inner in node.GetNodes("PUSH_INVENTORIES"))
            Push.AddAll(LoadBitSet(inner));

        foreach (var inner in node.GetNodes("PULL_INVENTORIES"))
            Pull.AddAll(LoadBitSet(inner));

        foreach (var inner in node.GetNodes("CONSTRAINT_INVENTORIES"))
            Constraint.AddAll(LoadBitSet(inner));

        try
        {
            var bNode = node.GetNode("BEHAVIOUR");
            if (bNode != null)
                Behaviour = ConverterBehaviour.Load(bNode, behaviour => behaviour.Vessel = vessel);
        }
        catch (Exception e)
        {
            string name = "<unknown>";
            node.TryGetValue("name", ref name);

            LogUtil.Error($"Failed to load ConverterBehaviour {name}: {e}");

            nextChangepoint = double.PositiveInfinity;
        }

        outputs.Clear();
        inputs.Clear();
        required.Clear();

        outputs.AddAll(
            ConfigUtil
                .LoadOutputResources(node)
                .Select(ratio => new KeyValuePair<int, ResourceRatio>(
                    ratio.ResourceName.GetHashCode(),
                    ratio
                ))
        );
        inputs.AddAll(
            ConfigUtil
                .LoadInputResources(node)
                .Select(ratio => new KeyValuePair<int, ResourceRatio>(
                    ratio.ResourceName.GetHashCode(),
                    ratio
                ))
        );
        required.AddAll(
            ConfigUtil
                .LoadRequiredResources(node)
                .Select(ratio => new KeyValuePair<int, ResourceConstraint>(
                    ratio.ResourceName.GetHashCode(),
                    ratio
                ))
        );
    }

    internal void LoadLegacyEdges(ConfigNode node, Dictionary<InventoryId, int> inventoryIds)
    {
        foreach (var inner in node.GetNodes("PUSH_INVENTORY"))
        {
            InventoryId id = default;
            id.Load(inner);

            if (!inventoryIds.TryGetValue(id, out var index))
                continue;
            Push.Add(index);
        }

        foreach (var inner in node.GetNodes("PULL_INVENTORY"))
        {
            InventoryId id = default;
            id.Load(inner);

            if (!inventoryIds.TryGetValue(id, out var index))
                continue;

            Pull.Add(index);
        }
    }

    public void Save(ConfigNode node)
    {
        node.AddValue("nextChangepoint", nextChangepoint);
        node.AddValue("rate", rate);

        SaveBitSet(node.AddNode("PUSH_INVENTORIES"), Push);
        SaveBitSet(node.AddNode("PULL_INVENTORIES"), Pull);
        SaveBitSet(node.AddNode("CONSTRAINT_INVENTORIES"), Constraint);

        Behaviour?.Save(node.AddNode("BEHAVIOUR"));
        ConfigUtil.SaveOutputResources(node, outputs.Select(output => output.Value));
        ConfigUtil.SaveInputResources(node, inputs.Select(input => input.Value));
        ConfigUtil.SaveRequiredResources(node, required.Select(required => required.Value));
    }

    private static DynamicBitSet LoadBitSet(ConfigNode node)
    {
        var indices = node.GetValues("index");
        int max = -1;

        for (int i = 0; i < indices.Length; ++i)
        {
            if (!uint.TryParse(indices[i], out var result))
                continue;
            max = (int)result;
        }

        var set = new DynamicBitSet(max + 1);
        for (int i = 0; i < indices.Length; ++i)
        {
            if (!uint.TryParse(indices[i], out var result))
                continue;
            set[(int)result] = true;
        }

        return set;
    }

    private static void SaveBitSet(ConfigNode node, DynamicBitSet set)
    {
        foreach (var index in set)
            node.AddValue("index", index);
    }

    public override string ToString()
    {
        var inputs = this.inputs.Select(entry => entry.Value.ResourceName);
        var outputs = this.outputs.Select(entry => entry.Value.ResourceName);

        return $"{string.Join(",", inputs)} => {string.Join(",", outputs)}";
    }

    private static bool OverwriteRatios(
        ref SortedMap<int, ResourceRatio> ratios,
        List<ResourceRatio> inputs
    )
    {
        var old = ratios;
        ratios = new(inputs.Count);
        using (var builder = ratios.CreateBuilder())
        {
            foreach (var ratio in inputs)
                builder.Add(ratio.ResourceName.GetHashCode(), ratio.WithDefaultedFlowMode());
        }

        return old == ratios;
    }

    private static bool OverwriteConstraints(
        ref SortedMap<int, ResourceConstraint> ratios,
        List<ResourceConstraint> inputs
    )
    {
        var old = ratios;
        ratios = new(inputs.Count);
        using (var builder = ratios.CreateBuilder())
        {
            foreach (var ratio in inputs)
                builder.Add(ratio.ResourceName.GetHashCode(), ratio.WithDefaultedFlowMode());
        }

        return old == ratios;
    }

    internal ResourceConverter CloneForSimulator()
    {
        var clone = (ResourceConverter)MemberwiseClone();
        clone.Behaviour = null;
        clone.nextChangepoint = double.PositiveInfinity;
        return clone;
    }

    internal void SolverHash(ref HashCode hasher)
    {
        hasher.AddAll(Push.Bits);
        hasher.AddAll(Pull.Bits);
    }
}

internal static class ResourceRatioExtensions
{
    public static ResourceRatio WithDefaultedFlowMode(this ResourceRatio res)
    {
        if (res.FlowMode != ResourceFlowMode.NULL)
            return res;

        int resourceId = res.ResourceName.GetHashCode();
        var definition = PartResourceLibrary.Instance.GetDefinition(resourceId);
        if (definition == null)
        {
            LogUtil.Error(
                $"Resource {res.ResourceName} had no resource definition in PartResourceLibrary."
            );
            res.FlowMode = ResourceFlowMode.ALL_VESSEL_BALANCE;
        }
        else
        {
            res.FlowMode = definition.resourceFlowMode;
        }

        return res;
    }
}
