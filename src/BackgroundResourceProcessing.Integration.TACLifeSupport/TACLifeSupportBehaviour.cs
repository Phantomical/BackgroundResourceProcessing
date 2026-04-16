using System.Collections.Generic;
using BackgroundResourceProcessing.Behaviour;
using BackgroundResourceProcessing.Core;

namespace BackgroundResourceProcessing.Integration.TACLifeSupport;

/// <summary>
/// A converter behaviour for a single TAC-LS life support resource that tracks
/// when the resource first ran short during background processing.
/// </summary>
///
/// <remarks>
/// <para>
/// Rates are computed as <c>BaseRate + PerCrewRate * NumCrew</c>, so
/// <see cref="NumCrew"/> can be decremented as kerbals die without rebuilding
/// the converter list.
/// </para>
///
/// <para>
/// The <see cref="lastNotSatisfied"/> timestamp maps directly onto TAC-LS semantics:
/// it is the last time the resource was being consumed successfully, equivalent to
/// what TAC-LS stores in <c>lastFood</c>, <c>lastWater</c>, etc.
/// </para>
/// </remarks>
public class TACLifeSupportBehaviour : ConverterBehaviour
{
    /// <summary>Input resource name (e.g. "Food", "Electricity").</summary>
    public string InputResourceName = "";

    /// <summary>Fixed input rate independent of crew count.</summary>
    public double BaseInputRate = 0;

    /// <summary>Per-kerbal input rate multiplied by <see cref="NumCrew"/>.</summary>
    public double PerCrewInputRate = 0;

    /// <summary>Output (waste) resource name. Empty string means no output.</summary>
    public string OutputResourceName = "";

    /// <summary>Fixed output rate independent of crew count.</summary>
    public double BaseOutputRate = 0;

    /// <summary>Per-kerbal output rate multiplied by <see cref="NumCrew"/>.</summary>
    public double PerCrewOutputRate = 0;

    /// <summary>Current active crew count. Decrement as kerbals die.</summary>
    public int NumCrew = 0;

    /// <summary>
    /// The time at which this resource FIRST ran short (null = satisfied throughout).
    /// </summary>
    public double? lastNotSatisfied = null;

    public override ConverterResources GetResources(VesselState state)
    {
        double inputRate = BaseInputRate + PerCrewInputRate * NumCrew;
        var inputs = new List<ResourceRatio>
        {
            new()
            {
                ResourceName = InputResourceName,
                Ratio = inputRate,
                FlowMode = ResourceFlowMode.ALL_VESSEL,
            },
        };

        var outputs = new List<ResourceRatio>();
        double outputRate = BaseOutputRate + PerCrewOutputRate * NumCrew;
        if (OutputResourceName.Length > 0 && outputRate > 0)
            outputs.Add(
                new()
                {
                    ResourceName = OutputResourceName,
                    Ratio = outputRate,
                    FlowMode = ResourceFlowMode.ALL_VESSEL,
                    DumpExcess = true,
                }
            );

        return new() { Inputs = inputs, Outputs = outputs };
    }

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
        node.TryGetValue("InputResourceName", ref InputResourceName);
        node.TryGetValue("BaseInputRate", ref BaseInputRate);
        node.TryGetValue("PerCrewInputRate", ref PerCrewInputRate);
        node.TryGetValue("OutputResourceName", ref OutputResourceName);
        node.TryGetValue("BaseOutputRate", ref BaseOutputRate);
        node.TryGetValue("PerCrewOutputRate", ref PerCrewOutputRate);
        node.TryGetValue("NumCrew", ref NumCrew);

        double t = 0;
        if (node.TryGetValue("lastNotSatisfied", ref t))
            lastNotSatisfied = t;
    }

    protected override void OnSave(ConfigNode node)
    {
        base.OnSave(node);
        node.AddValue("InputResourceName", InputResourceName);
        if (BaseInputRate != 0) node.AddValue("BaseInputRate", BaseInputRate);
        if (PerCrewInputRate != 0) node.AddValue("PerCrewInputRate", PerCrewInputRate);
        if (OutputResourceName.Length > 0)
        {
            node.AddValue("OutputResourceName", OutputResourceName);
            if (BaseOutputRate != 0) node.AddValue("BaseOutputRate", BaseOutputRate);
            if (PerCrewOutputRate != 0) node.AddValue("PerCrewOutputRate", PerCrewOutputRate);
        }
        node.AddValue("NumCrew", NumCrew);

        if (lastNotSatisfied != null)
            node.AddValue("lastNotSatisfied", (double)lastNotSatisfied);
    }
}
