using System;
using System.Collections.Generic;
using System.Linq;
using BackgroundResourceProcessing.Behaviour;
using BackgroundResourceProcessing.Core;

namespace BackgroundResourceProcessing.Integration.CryoTanks;

public class CryoTankBoiloffBehaviour : ConstantProducer
{
    [KSPField(isPersistant = true)]
    public string BoiloffResourceName;

    public InventoryId FuelInventoryId;

    [KSPField(isPersistant = true)]
    public double BoiloffRate;

    [KSPField(isPersistant = true)]
    public double MaxError;

    // TODO: LongWave/ShortWave heating. I haven't actually been able to find a
    //       part that actually enables that so I've skipped it for now.

    public CryoTankBoiloffBehaviour() { }

    public CryoTankBoiloffBehaviour(List<ResourceRatio> outputs)
        : base(outputs) { }

    public override ConverterResources GetResources(VesselState state)
    {
        var processor = Vessel.FindVesselModuleImplementing<BackgroundResourceProcessor>();
        var inventory = processor.GetInventoryById(FuelInventoryId);
        if (inventory == null)
            return new();

        var rate = inventory.amount * BoiloffRate;

        ConverterResources resources = new()
        {
            Inputs =
            [
                new ResourceRatio()
                {
                    ResourceName = BoiloffResourceName,
                    Ratio = 1.0,
                    FlowMode = ResourceFlowMode.NO_FLOW,
                },
                new ResourceRatio()
                {
                    ResourceName = inventory.resourceName,
                    Ratio = rate,
                    FlowMode = ResourceFlowMode.NO_FLOW,
                },
            ],
        };

        resources.Outputs.AddRange(
            Outputs.Select(output => new ResourceRatio()
            {
                ResourceName = output.ResourceName,
                Ratio = output.Ratio * rate,
                FlowMode = output.FlowMode,
                DumpExcess = true,
            })
        );

        return resources;
    }

    public override void OnRatesComputed(
        BackgroundResourceProcessor processor,
        Core.ResourceConverter converter,
        RateCalculatedEvent evt
    )
    {
        var inventory = processor.GetInventoryById(FuelInventoryId);
        if (inventory == null)
            return;

        if (inventory.rate == 0.0)
            converter.nextChangepoint = double.PositiveInfinity;
        else
            converter.nextChangepoint =
                evt.CurrentTime
                + Math.Max(inventory.amount / Math.Abs(inventory.rate) * MaxError, 600.0);
    }

    protected override void OnLoad(ConfigNode node)
    {
        base.OnLoad(node);

        ConfigNode idNode = null;
        if (node.TryGetNode("FUEL_INVENTORY", ref idNode))
            FuelInventoryId.Load(idNode);
    }

    protected override void OnSave(ConfigNode node)
    {
        base.OnSave(node);

        FuelInventoryId.Save(node.AddNode("FUEL_INVENTORY"));
    }
}
