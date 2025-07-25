using System;

namespace BackgroundResourceProcessing.Behaviour;

/// <summary>
/// The behaviour used for a <see cref="ModuleScienceConverter"/>.
/// </summary>
///
/// <remarks>
/// This will dynamically adjust its rate based on amount of data stored in the
/// inventory as specified by <see cref="DataResourceName"/>,
/// <see cref="LabFlightId"/>, and <see cref="LabModuleId"/>.
/// </remarks>
public class ScienceConverterBehaviour : ConverterBehaviour
{
    [KSPField(isPersistant = true)]
    public string DataResourceName;

    [KSPField(isPersistant = true)]
    public string ScienceResourceName;

    [KSPField(isPersistant = true)]
    public uint LabFlightId;

    [KSPField(isPersistant = true)]
    public uint LabModuleId;

    [KSPField(isPersistant = true)]
    public double Productivity;

    [KSPField(isPersistant = true)]
    public double ScienceMultiplier;

    [KSPField(isPersistant = true)]
    public double PowerRequirement;

    [KSPField(isPersistant = true)]
    public double MaxError;

    public override ConverterResources GetResources(VesselState state)
    {
        var processor = Vessel.FindVesselModuleImplementing<BackgroundResourceProcessor>();
        if (processor == null)
            return default;

        var dataInv = processor.GetInventoryById(new(LabFlightId, DataResourceName, LabModuleId));
        if (dataInv == null)
            return default;

        var data = dataInv.amount;

        return new ConverterResources()
        {
            Inputs =
            [
                new()
                {
                    ResourceName = "ElectricCharge",
                    Ratio = PowerRequirement,
                    FlowMode = ResourceFlowMode.ALL_VESSEL,
                },
                new()
                {
                    ResourceName = DataResourceName,
                    Ratio = data * Productivity,
                    FlowMode = ResourceFlowMode.NO_FLOW,
                },
            ],
            Outputs =
            [
                new()
                {
                    ResourceName = ScienceResourceName,
                    Ratio = data * Productivity * ScienceMultiplier,
                    FlowMode = ResourceFlowMode.NO_FLOW,
                },
            ],
            NextChangepoint = double.PositiveInfinity,
        };
    }

    public override void OnRatesComputed(
        BackgroundResourceProcessor processor,
        Core.ResourceConverter converter,
        RateCalculatedEvent evt
    )
    {
        var data = processor.GetInventoryById(new(LabFlightId, DataResourceName, LabModuleId));
        if (data == null)
            return;

        if (data.rate == 0.0)
            converter.nextChangepoint = double.PositiveInfinity;
        else
            converter.nextChangepoint =
                evt.CurrentTime + Math.Max(data.amount / Math.Abs(data.rate) * MaxError, 600.0);
    }
}
