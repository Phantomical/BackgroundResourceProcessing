using System;
using System.Collections.Generic;
using System.Linq;
using BackgroundResourceProcessing.Utils;

namespace BackgroundResourceProcessing.Behaviour;

/// <summary>
/// A behaviour that describes a converter.
/// </summary>
///
/// <remarks>
///   This is more or less the core type of UBP. It is used by the
///   background solver to determine the rates at which resources are
///   produced.
/// </remarks>
public abstract class ConverterBehaviour() : DynamicallySerializable<ConverterBehaviour>()
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
    /// The vessel that this behaviour belongs to.
    /// </summary>
    ///
    /// <remarks>
    /// Expect this vessel to be unloaded. The only times that it will not be
    /// are when the initial vessel state is recorded.
    /// </remarks>
    public Vessel Vessel = null;

    /// <summary>
    /// An optional priority for this specific converter. Overrides the default
    /// priority for the adapter if specified.
    /// </summary>
    public int? Priority = null;

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
    /// Event data passed to <see cref="OnRatesComputed"/> .
    /// </summary>
    public struct RateCalculatedEvent
    {
        public double CurrentTime;
    }

    /// <summary>
    /// Called after rate calculations are done, but before changepoint callbacks
    /// are performed. This allows you to adjust the next changepoint time
    /// based on vessel state.
    /// </summary>
    /// <param name="processor">
    ///   The <see cref="BackgroundResourceProcessor"/> that owns this converter.
    /// </param>
    /// <param name="converter">
    ///   The <see cref="Core.ResourceConverter"/> that owns this behaviour.
    /// </param>
    /// <param name="evt">
    ///   A <see cref="RateCalculatedEvent"/> containing relevant event data.
    /// </param>
    public virtual void OnRatesComputed(
        BackgroundResourceProcessor processor,
        Core.ResourceConverter converter,
        RateCalculatedEvent evt
    ) { }

    public static ConverterBehaviour Load(ConfigNode node, Action<ConverterBehaviour> action = null)
    {
        Action<DynamicallySerializable<ConverterBehaviour>> castaction = null;
        if (action != null)
            castaction = behaviour => action((ConverterBehaviour)behaviour);

        return (ConverterBehaviour)
            DynamicallySerializable<ConverterBehaviour>.Load(node, castaction);
    }

    protected override void OnSave(ConfigNode node)
    {
        base.OnSave(node);
        if (sourceModule != null)
            node.AddValue("sourceModule", sourceModule);
        if (sourcePart != null)
            node.AddValue("sourcePart", sourcePart);
        if (Priority != null)
            node.AddValue("Priority", (int)Priority);
    }

    protected override void OnLoad(ConfigNode node)
    {
        base.OnLoad(node);

        int priority = 0;
        if (node.TryGetValue(nameof(Priority), ref priority))
            Priority = priority;
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
