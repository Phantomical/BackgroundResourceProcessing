using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using BackgroundResourceProcessing.Utils;

namespace BackgroundResourceProcessing;

public struct VesselState
{
    /// <summary>
    /// The vessel that this module belongs to.
    /// </summary>
    ///
    /// <remarks>
    /// Expect this vessel to be unloaded. The only times that it will not
    /// be are when switching to/away from a vessel.
    /// </remarks>
    public Vessel Vessel;

    /// <summary>
    /// The time at which we are getting the rate.
    /// </summary>
    ///
    /// <remarks>
    /// Note that this may not correspond with the current game time, though
    /// it should usually be close enough that using other properties
    /// associated with the vessel (e.g. orbit parameters) should be fine.
    /// </remarks>
    public double CurrentTime;
}

public class InvalidBehaviourException(string message) : Exception(message) { }

/// <summary>
/// The type of constraint applied on this converter.
/// </summary>
public enum Constraint
{
    /// <summary>
    /// The converter must have at least <c>Amount</c> resources to activate.
    /// </summary>
    AT_LEAST,

    /// <summary>
    /// The converter must have at most <c>Amount</c> resources to activate.
    /// </summary>
    AT_MOST,
}

/// <summary>
/// A constraint applied to a resource.
/// </summary>
[DebuggerDisplay("{Constraint} {Amount} {ResourceName}")]
public struct ResourceConstraint()
{
    /// <summary>
    /// The name of the resource that this constraint applies to.
    /// </summary>
    public string ResourceName;

    /// <summary>
    /// At what resource amount does this constraint apply.
    /// </summary>
    public double Amount = 0.0;

    /// <summary>
    /// What type of constraint is being applied here.
    /// </summary>
    public Constraint Constraint = Constraint.AT_LEAST;

    /// <summary>
    /// The flow mode to use when computing inventory resource constraints.
    /// </summary>
    public ResourceFlowMode FlowMode = ResourceFlowMode.ALL_VESSEL;

    public ResourceConstraint(ResourceRatio ratio)
        : this()
    {
        ResourceName = ratio.ResourceName;
        Amount = ratio.Ratio;
        FlowMode = ratio.FlowMode;
    }

    public void Load(ConfigNode node)
    {
        node.TryGetValue("ResourceName", ref ResourceName);
        node.TryGetEnum("Constraint", ref Constraint, Constraint.AT_LEAST);
        node.TryGetEnum("FlowMode", ref FlowMode, ResourceFlowMode.ALL_VESSEL);

        if (!node.TryGetValue("Amount", ref Amount))
        {
            // This is for backwards compatibility with existing KSP
            // REQUIRED_RESOURCE blocks.
            //
            // We'll use the Amount key if present but this will make MM
            // node copies just work as expected.
            node.TryGetValue("Ratio", ref Amount);
        }
    }

    public void Save(ConfigNode node)
    {
        node.AddValue("ResourceName", ResourceName);
        node.AddValue("Amount", Amount);
        node.AddValue("Constraint", Constraint);
        node.AddValue("FlowMode", FlowMode);
    }

    internal ResourceConstraint WithDefaultedFlowMode()
    {
        if (FlowMode != ResourceFlowMode.NULL)
            return this;

        int resourceId = ResourceName.GetHashCode();
        var definition = PartResourceLibrary.Instance.GetDefinition(resourceId);

        if (definition == null)
        {
            LogUtil.Error(
                $"Resource {ResourceName} had no resource definition in PartResourceLibrary."
            );
            FlowMode = ResourceFlowMode.ALL_VESSEL_BALANCE;
        }
        else
        {
            FlowMode = definition.resourceFlowMode;
        }

        return this;
    }
}

public struct ConverterResources()
{
    /// <summary>
    /// A list of resources that are consumed by this converter, along
    /// with their rates and flow modes.
    /// </summary>
    public List<ResourceRatio> Inputs = [];

    /// <summary>
    /// A list of resources that are produced by this converter, along
    /// with their rates and flow modes.
    /// </summary>
    public List<ResourceRatio> Outputs = [];

    /// <summary>
    /// A list of constraints on what resources must be present on the
    /// vessel in order for this converter to be active.
    /// </summary>
    ///
    /// <remarks>
    /// <para>
    ///   It is possible to have constraints that
    /// </para>
    /// </remarks>
    public List<ResourceConstraint> Requirements = [];
}

/// <summary>
/// A behaviour that describes a converter.
/// </summary>
///
/// <remarks>
///   This is more or less the core type of UBP. It is used by the
///   background solver to determine the rates at which resources are
///   produced.
/// </remarks>
public abstract class ConverterBehaviour(int priority = 0)
    : DynamicallySerializable<ConverterBehaviour>()
{
    /// <summary>
    /// The module that constructed this behaviour.
    /// </summary>
    ///
    /// <remarks>
    /// This is mainly used for debugging and log messages. It will be
    /// automatically set when returned from a component module.
    /// </remarks>
    public string SourceModule => sourceModule;

    [KSPField]
    internal string sourceModule = null;

    /// <summary>
    /// The part that contained the source module for this behaviour.
    /// </summary>
    ///
    /// <remarks>
    /// This is purely added for debugging purposes.
    /// </remarks>
    public string SourcePart => sourcePart;

    [KSPField]
    internal string sourcePart = null;

    /// <summary>
    /// The priority with which this consumer will consume produced resources.
    /// </summary>
    ///
    /// <remarks>
    ///   This is used to determine which parts will continue to be
    ///   supplied with resources when there are not enough being produced
    ///   to satisfy all consumers/converters. Higher priorities will
    ///   consume resources first. The default is 0, and generally you can
    ///   leave the priority at that.
    /// </remarks>
    [KSPField]
    public int Priority = priority;

    /// <summary>
    /// Get the list of input, output, and required resources and their
    /// rates.
    /// </summary>
    ///
    /// <param name="state">State information about the vessel.</param>
    /// <returns>A <c>ConverterResources</c> with the relevant resources</returns>
    ///
    /// <remarks>
    /// This should be the "steady-state" rate at which this converter will
    /// consume or produce resources.
    /// </remarks>
    public abstract ConverterResources GetResources(VesselState state);

    /// <summary>
    /// Get the time at which the rates for this behaviour will change next.
    /// </summary>
    ///
    /// <param name="state">State information about the vessel.</param>
    /// <returns>The duration until the next changepoint, in seconds.</returns>
    ///
    /// <remarks>
    ///   <para>
    ///     This can be used to simulate behaviours that have non-linear
    ///     behaviour by approximating them using a piecewise linear rate
    ///     function. However, adding more changepoints does have a cost so
    ///     it is best to limit updates to at most once per day per vessel.
    ///   </para>
    ///
    ///   <para>
    ///     In cases where there are no future changepoints, you can return
    ///     <c>double.PositiveInfinity</c>. In this case, the behaviour rates
    ///     will not be loaded again due to changepoint timeout. Note that
    ///     refreshes will still happen when the vessel is switched to, or
    ///     when it switches from one SOI to another.
    ///   </para>
    ///
    ///   <para>
    ///     By default, this returns <c>double.PositiveInfinity</c>.
    ///   </para>
    /// </remarks>
    public virtual double GetNextChangepoint(VesselState state)
    {
        return double.PositiveInfinity;
    }

    public static new ConverterBehaviour Load(ConfigNode node)
    {
        return (ConverterBehaviour)DynamicallySerializable<ConverterBehaviour>.Load(node);
    }

    internal static void RegisterAll()
    {
        var types = AssemblyLoader
            .GetSubclassesOfParentClass(typeof(ConverterBehaviour))
            .Where(type => !type.IsAbstract);

        RegisterAll(types);
    }

    internal static new void RegisterAll(IEnumerable<Type> types)
    {
        DynamicallySerializable<ConverterBehaviour>.RegisterAll(types);
    }
}

/// <summary>
/// A behaviour which only produces resources.
/// </summary>
///
/// <remarks>
/// This mainly serves as a convenient way to declare behaviours which only
/// produce resources. Semantically, it has no difference from just declaring
/// a converter that only produces resources.
/// </remarks>
public abstract class ProducerBehaviour(int priority = 0) : ConverterBehaviour(priority)
{
    /// <summary>
    /// Get the list of resources that are being produced by this part and
    /// the rates at which they are being produced.
    /// </summary>
    ///
    /// <param name="state">State information about the vessel.</param>
    /// <returns>
    ///   A list of resources and the rates at which they are being produced.
    /// </returns>
    ///
    /// <remarks>
    /// <para>
    ///   This should be the "steady-state" rate at which this producer will
    ///   produce more resources. It should assume that there is room to
    ///   store the resources - resources filling up will be handled by
    ///   the background processing solver.
    /// </para>
    /// </remarks>
    public abstract List<ResourceRatio> GetOutputs(VesselState state);

    public sealed override ConverterResources GetResources(VesselState state)
    {
        return new() { Outputs = GetOutputs(state) };
    }
}

/// <summary>
/// A behaviour which only consumes resources.
/// </summary>
///
/// <remarks>
/// This mainly serves as a convenient way to declare behaviours which only
/// consume resources. Semantically, it has no difference from just declaring
/// a converter that only consumes resources.
/// </remarks>
public abstract class ConsumerBehaviour(int priority = 0) : ConverterBehaviour(priority)
{
    /// <summary>
    /// Get the current list of resources that are being consumed by this
    /// part and the rates at which they are being consumed.
    /// </summary>
    ///
    /// <param name="state">State information about the vessel.</param>
    /// <returns>
    ///   A list of resources and the rates at which they are being consumed.
    /// </returns>
    ///
    /// <remarks>
    /// This should be the "steady-state" rate at which this consumer will
    /// consume resources. It should assume that the resources are available.
    /// Insufficient stored resources will be handled by the background
    /// processing solver.
    /// </remarks>
    public abstract List<ResourceRatio> GetInputs(VesselState state);

    public sealed override ConverterResources GetResources(VesselState state)
    {
        return new() { Inputs = GetInputs(state) };
    }
}

/// <summary>
/// A converter that converts a set of resources into another set of
/// resources at a constant rate.
/// </summary>
public class ConstantConverter : ConverterBehaviour
{
    public List<ResourceRatio> inputs = [];
    public List<ResourceRatio> outputs = [];
    public List<ResourceConstraint> required = [];

    public ConstantConverter() { }

    public ConstantConverter(List<ResourceRatio> inputs, List<ResourceRatio> outputs)
    {
        this.inputs = inputs;
        this.outputs = outputs;
    }

    public ConstantConverter(
        List<ResourceRatio> inputs,
        List<ResourceRatio> outputs,
        List<ResourceConstraint> required
    )
    {
        this.inputs = inputs;
        this.outputs = outputs;
        this.required = required;
    }

    public override ConverterResources GetResources(VesselState state)
    {
        ConverterResources resources = default;
        resources.Inputs = inputs;
        resources.Outputs = outputs;
        resources.Requirements = required;
        return resources;
    }

    protected override void OnLoad(ConfigNode node)
    {
        base.OnLoad(node);
        inputs = [.. ConfigUtil.LoadInputResources(node)];
        outputs = [.. ConfigUtil.LoadOutputResources(node)];
        required = [.. ConfigUtil.LoadRequiredResources(node)];
    }

    protected override void OnSave(ConfigNode node)
    {
        base.OnSave(node);
        ConfigUtil.SaveInputResources(node, inputs);
        ConfigUtil.SaveOutputResources(node, outputs);
        ConfigUtil.SaveRequiredResources(node, required);
    }
}

/// <summary>
/// A producer that produces resources at a fixed rate.
/// </summary>
public class ConstantProducer : ProducerBehaviour
{
    private List<ResourceRatio> outputs = [];

    public ConstantProducer() { }

    public ConstantProducer(List<ResourceRatio> outputs)
    {
        this.outputs = outputs;
    }

    public override List<ResourceRatio> GetOutputs(VesselState state)
    {
        return outputs;
    }

    protected override void OnLoad(ConfigNode node)
    {
        base.OnLoad(node);
        outputs = [.. ConfigUtil.LoadOutputResources(node)];
    }

    protected override void OnSave(ConfigNode node)
    {
        base.OnSave(node);
        ConfigUtil.SaveOutputResources(node, outputs);
    }
}

/// <summary>
/// A consumer that consumes resources at a fixed rate.
/// </summary>
public class ConstantConsumer : ConsumerBehaviour
{
    private List<ResourceRatio> inputs = [];

    public ConstantConsumer() { }

    public ConstantConsumer(List<ResourceRatio> inputs)
    {
        this.inputs = inputs;
    }

    public override List<ResourceRatio> GetInputs(VesselState state)
    {
        return inputs;
    }

    protected override void OnLoad(ConfigNode node)
    {
        base.OnLoad(node);
        inputs = [.. ConfigUtil.LoadInputResources(node)];
    }

    protected override void OnSave(ConfigNode node)
    {
        base.OnSave(node);
        ConfigUtil.SaveInputResources(node, inputs);
    }
}
