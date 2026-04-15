using System.Collections.Generic;
using BackgroundResourceProcessing.Behaviour;
using BackgroundResourceProcessing.Core;
using BackgroundResourceProcessing.Utils;

namespace BackgroundResourceProcessing.Integration.TACLifeSupport;

/// <summary>
/// A converter behaviour for TAC-LS life support resources that tracks when a
/// resource first ran short during background processing.
/// </summary>
///
/// <remarks>
/// <para>
/// The <see cref="lastNotSatisfied"/> timestamp maps directly onto TAC-LS semantics:
/// it is the last time the resource was being consumed successfully, equivalent to
/// what TAC-LS stores in <c>lastFood</c>, <c>lastWater</c>, etc.
/// </para>
///
/// <para>
/// When <see cref="lastNotSatisfied"/> is null, the resource was fully satisfied
/// throughout the background period. When it is set, it records when the resource
/// first ran short — so setting <c>lastFood = lastNotSatisfied</c> on vessel restore
/// gives TAC-LS the correct starvation duration.
/// </para>
/// </remarks>
public class TACLifeSupportBehaviour : ConverterBehaviour
{
    public List<ResourceRatio> Inputs = [];
    public List<ResourceRatio> Outputs = [];

    /// <summary>
    /// The time at which this resource FIRST ran short (null = satisfied throughout).
    /// </summary>
    public double? lastNotSatisfied = null;

    public TACLifeSupportBehaviour(List<ResourceRatio> inputs, List<ResourceRatio> outputs)
    {
        Inputs = inputs;
        Outputs = outputs;
    }

    public TACLifeSupportBehaviour()
        : this([], []) { }

    public override ConverterResources GetResources(VesselState state) =>
        new() { Inputs = Inputs, Outputs = Outputs };

    public override void OnRatesComputed(
        BackgroundResourceProcessor processor,
        Core.ResourceConverter converter,
        RateCalculatedEvent evt
    )
    {
        base.OnRatesComputed(processor, converter, evt);

        if (converter.Rate < 1.0)
            lastNotSatisfied ??= evt.CurrentTime; // Record first time of shortage
        else
            lastNotSatisfied = null; // Resource restored
    }

    protected override void OnLoad(ConfigNode node)
    {
        base.OnLoad(node);
        Inputs.AddRange(ConfigUtil.LoadInputResources(node));
        Outputs.AddRange(ConfigUtil.LoadOutputResources(node));

        double t = 0;
        if (node.TryGetValue("lastNotSatisfied", ref t))
            lastNotSatisfied = t;
    }

    protected override void OnSave(ConfigNode node)
    {
        base.OnSave(node);
        ConfigUtil.SaveInputResources(node, Inputs);
        ConfigUtil.SaveOutputResources(node, Outputs);

        if (lastNotSatisfied != null)
            node.AddValue("lastNotSatisfied", (double)lastNotSatisfied);
    }
}
