using System;
using System.Collections.Generic;
using System.Linq;
using BackgroundResourceProcessing.Behaviour;
using BackgroundResourceProcessing.Collections;
using BackgroundResourceProcessing.Utils;
using FinePrint.Contracts;

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
    public SortedMap<int, ResourceRatio> Inputs = [];

    /// <summary>
    /// The resource outputs returned from the behaviour.
    /// </summary>
    public SortedMap<int, ResourceRatio> Outputs = [];

    /// <summary>
    /// The resource requirements returned from the behaviour.
    /// </summary>
    public SortedMap<int, ResourceConverterConstraint> Required = [];

    /// <summary>
    /// The time at which the behaviour has said that its behaviour might
    /// change next.
    /// </summary>
    public double NextChangepoint = double.PositiveInfinity;

    /// <summary>
    /// The current rate at which this converter is running. This will
    /// always be a number in the range <c>[0, 1]</c>.
    /// </summary>
    public double Rate = 0.0;

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
    public double ActiveTime = 0.0;

    /// <summary>
    /// The priority of this converter. Converters with a higher priority will
    /// be preferred over those with a lower priority.
    /// </summary>
    public int Priority = 0;

    /// <summary>
    /// The flight id of the part this converter was created from, or null if it
    /// was not created from a part.
    /// </summary>
    public uint? FlightId = null;

    /// <summary>
    /// The persistent id of the part module this converter was created from, or
    /// null if it was not created from a part module.
    /// </summary>
    public uint? ModuleId = null;

    /// <summary>
    /// Get the overall constraint state for this converter.
    /// </summary>
    public ConstraintState ConstraintState
    {
        get
        {
            var state = ConstraintState.ENABLED;

            foreach (ref var required in Required.Values)
                state = state.Merge(required.State);

            return state;
        }
    }

    public bool Refresh(VesselState state)
    {
        if (Behaviour == null)
        {
            NextChangepoint = double.PositiveInfinity;
            return false;
        }

        try
        {
            var resources = Behaviour.GetResources(state);
            NextChangepoint = resources.NextChangepoint;

            if (NextChangepoint < state.CurrentTime)
            {
                LogUtil.Error(
                    $"Behaviour {Behaviour.GetType().Name} returned changepoint in the past. ",
                    "Setting changepoint to infinity instead."
                );
                NextChangepoint = double.PositiveInfinity;
            }

            bool changed = false;

            if (OverwriteRatios(ref Inputs, resources.Inputs))
                changed = true;
            if (OverwriteRatios(ref Outputs, resources.Outputs))
                changed = true;
            if (OverwriteConstraints(ref Required, resources.Requirements))
                changed = true;

            return changed;
        }
        catch (Exception e)
        {
            LogUtil.Error($"{Behaviour.GetType().Name}.GetResources threw an exception: {e}");

            NextChangepoint = double.PositiveInfinity;
            Inputs = [];
            Outputs = [];
            Required = [];

            return true;
        }
    }

    public void Load(ConfigNode node, Vessel vessel = null)
    {
        node.TryGetDouble("nextChangepoint", ref NextChangepoint);
        node.TryGetValue("rate", ref Rate);
        node.TryGetValue("priority", ref Priority);

        uint flightId = 0;
        if (node.TryGetValue("flightId", ref flightId))
            FlightId = flightId;

        uint moduleId = 0;
        if (node.TryGetValue("moduleId", ref moduleId))
            ModuleId = moduleId;

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

            NextChangepoint = double.PositiveInfinity;
        }

        Outputs.Clear();
        Inputs.Clear();
        Required.Clear();

        Outputs.AddAll(
            ConfigUtil
                .LoadOutputResources(node)
                .Select(ratio => new KeyValuePair<int, ResourceRatio>(
                    ratio.ResourceName.GetHashCode(),
                    ratio
                ))
        );
        Inputs.AddAll(
            ConfigUtil
                .LoadInputResources(node)
                .Select(ratio => new KeyValuePair<int, ResourceRatio>(
                    ratio.ResourceName.GetHashCode(),
                    ratio
                ))
        );
        Required = LoadRequiredResources(node);
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

    internal void ComputeConstraintStates(ResourceProcessor processor)
    {
        if (ConstraintState != ConstraintState.UNSET)
            return;

        processor.UpdateConstraintState(this);
    }

    public void Save(ConfigNode node)
    {
        node.AddValue("nextChangepoint", NextChangepoint);
        node.AddValue("rate", Rate);
        node.AddValue("priority", Priority);

        if (FlightId is not null)
            node.AddValue("flightId", FlightId.Value);

        if (ModuleId is not null)
            node.AddValue("moduleId", ModuleId.Value);

        if (!Push.IsEmpty)
            SaveBitSet(node.AddNode("PUSH_INVENTORIES"), Push);
        if (!Pull.IsEmpty)
            SaveBitSet(node.AddNode("PULL_INVENTORIES"), Pull);
        if (!Constraint.IsEmpty)
            SaveBitSet(node.AddNode("CONSTRAINT_INVENTORIES"), Constraint);

        Behaviour?.Save(node.AddNode("BEHAVIOUR"));
        ConfigUtil.SaveOutputResources(node, Outputs.Select(output => output.Value));
        ConfigUtil.SaveInputResources(node, Inputs.Select(input => input.Value));
        SaveRequiredResources(node, Required);
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
        var inputs = this.Inputs.Select(entry => entry.Value.ResourceName);
        var outputs = this.Outputs.Select(entry => entry.Value.ResourceName);

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
        ref SortedMap<int, ResourceConverterConstraint> ratios,
        List<ResourceConstraint> inputs
    )
    {
        var old = ratios;
        ratios = new(inputs.Count);
        using (var builder = ratios.CreateBuilder())
        {
            foreach (var constraint in inputs)
                builder.Add(constraint.ResourceName.GetHashCode(), new(constraint));
        }

        return old == ratios;
    }

    internal ResourceConverter CloneForSimulator()
    {
        var clone = (ResourceConverter)MemberwiseClone();
        clone.Behaviour = null;
        clone.NextChangepoint = double.PositiveInfinity;
        clone.Required = Required.Clone();
        return clone;
    }

    internal void SolverHash(ref HashCode hasher)
    {
        hasher.AddAll(Push.Bits);
        hasher.AddAll(Pull.Bits);
        hasher.Add(ConstraintState);
    }

    private static void SaveRequiredResources(
        ConfigNode node,
        SortedMap<int, ResourceConverterConstraint> required
    )
    {
        foreach (var constraint in required.Values)
            constraint.Save(node.AddNode("REQUIRED_RESOURCE"));
    }

    private static SortedMap<int, ResourceConverterConstraint> LoadRequiredResources(
        ConfigNode node
    )
    {
        var nodes = node.GetNodes("REQUIRED_RESOURCE");
        if (nodes == null || nodes.Length == 0)
            return [];

        var required = new SortedMap<int, ResourceConverterConstraint>(nodes.Length);
        var builder = required.CreateBuilder();
        for (int i = 0; i < nodes.Length; ++i)
        {
            ResourceConverterConstraint constraint = default;
            constraint.Load(nodes[i]);
            builder.Set(constraint.ResourceName.GetHashCode(), constraint);
        }

        builder.Commit();
        return required;
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
