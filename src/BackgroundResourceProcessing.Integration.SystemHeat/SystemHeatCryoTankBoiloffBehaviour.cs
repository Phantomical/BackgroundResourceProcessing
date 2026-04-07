using System;
using System.Collections.Generic;
using BackgroundResourceProcessing.Behaviour;
using BackgroundResourceProcessing.Core;

namespace BackgroundResourceProcessing.Integration.SystemHeat;

public class SystemHeatCryoTankBoiloffBehaviour : ConstantProducer
{
    public InventoryId FuelInventoryId;

    [KSPField(isPersistant = true)]
    public double BoiloffRate;

    [KSPField(isPersistant = true)]
    public double MaxError;

    public SystemHeatCryoTankBoiloffBehaviour() { }

    public SystemHeatCryoTankBoiloffBehaviour(List<ResourceRatio> outputs)
        : base(outputs ?? []) { }

    public override ConverterResources GetResources(VesselState state)
    {
        var processor = Vessel.FindVesselModuleImplementing<BackgroundResourceProcessor>();
        var inventory = processor.GetInventoryById(FuelInventoryId);
        if (inventory == null)
            return new();

        var rate = inventory.Amount * BoiloffRate;

        ConverterResources resources = new()
        {
            Inputs =
            [
                new ResourceRatio()
                {
                    ResourceName = inventory.ResourceName,
                    Ratio = rate,
                    FlowMode = ResourceFlowMode.NO_FLOW,
                },
            ],
        };

        foreach (var output in Outputs)
        {
            resources.Outputs.Add(
                new ResourceRatio()
                {
                    ResourceName = output.ResourceName,
                    Ratio = output.Ratio * rate,
                    FlowMode = output.FlowMode,
                    DumpExcess = true,
                }
            );
        }

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

        if (inventory.Rate == 0.0)
            converter.NextChangepoint = double.PositiveInfinity;
        else
            converter.NextChangepoint =
                evt.CurrentTime
                + Math.Max(inventory.Amount / Math.Abs(inventory.Rate) * MaxError, 600.0);
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
