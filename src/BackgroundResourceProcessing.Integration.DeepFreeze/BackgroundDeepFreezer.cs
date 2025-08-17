using System.Collections.Generic;
using System.Reflection;
using BackgroundResourceProcessing.Behaviour;
using BackgroundResourceProcessing.Collections;
using BackgroundResourceProcessing.Converter;
using BackgroundResourceProcessing.Core;
using BackgroundResourceProcessing.Inventory;
using DF;
using KSP.Localization;

namespace BackgroundResourceProcessing.Integration.DeepFreeze;

public class BackgroundDeepFreezerConverter : BackgroundConverter<DeepFreezer>
{
    [KSPField]
    public string ThawPotentialResource = "BRPDeepFreezerThawPotential";

    [KSPField]
    public string UnpoweredTimeResource = "BRPDeepFreezerUnpoweredTime";

    public override ModuleBehaviour GetBehaviour(DeepFreezer module)
    {
        if (module.TotalFrozen <= 0)
            return null;
        if (!module.DFIECReqd)
            return null;

        var behaviour = new ModuleBehaviour(
            [
                new ConstantProducer(
                    [
                        new()
                        {
                            ResourceName = ThawPotentialResource,
                            Ratio = 1.0,
                            DumpExcess = false,
                            FlowMode = ResourceFlowMode.NO_FLOW,
                        },
                    ]
                ),
                new ConstantConsumer(
                    [
                        new()
                        {
                            ResourceName = "ElectricCharge",
                            Ratio = module.DFIFrznChargeRequired / 60.0 * module.TotalFrozen,
                            FlowMode = ResourceFlowMode.ALL_VESSEL_BALANCE,
                        },
                        new()
                        {
                            ResourceName = ThawPotentialResource,
                            Ratio = 1.0,
                            FlowMode = ResourceFlowMode.NO_FLOW,
                        },
                    ]
                )
                {
                    Priority = 10,
                },
                new BackgroundDeepFreezerBehaviour()
                {
                    ThawPotentialResource = ThawPotentialResource,
                    UnpoweredTimeResource = UnpoweredTimeResource,
                    FlightId = module.part.flightID,
                    ModuleId = module.GetPersistentId(),
                },
            ]
        );

        behaviour.AddPullModule(module);
        behaviour.AddPushModule(module);

        return behaviour;
    }
}

public class BackgroundDeepFreezerBehaviour : ConverterBehaviour
{
    [KSPField(isPersistant = true)]
    public string ThawPotentialResource = "BRPDeepFreezerThawPotential";

    [KSPField(isPersistant = true)]
    public string UnpoweredTimeResource = "BRPDeepFreezerUnpoweredTime";

    [KSPField(isPersistant = true)]
    public uint FlightId;

    [KSPField(isPersistant = true)]
    public uint ModuleId;

    [KSPField(isPersistant = true)]
    public bool KerbalsAreDead = false;

    private static bool DeathFatal => DF.DeepFreeze.Instance?.DFIDeathFatal ?? false;

    public override ConverterResources GetResources(VesselState state)
    {
        return new()
        {
            Inputs =
            [
                new()
                {
                    ResourceName = ThawPotentialResource,
                    Ratio = 1.0,
                    FlowMode = ResourceFlowMode.NO_FLOW,
                },
            ],
            Outputs =
            [
                new()
                {
                    ResourceName = UnpoweredTimeResource,
                    Ratio = 1.0,
                    FlowMode = ResourceFlowMode.NO_FLOW,
                    DumpExcess = !DeathFatal,
                },
            ],
        };
    }

    public override void OnRatesComputed(
        BackgroundResourceProcessor processor,
        Core.ResourceConverter converter,
        RateCalculatedEvent evt
    )
    {
        var inventory = processor.GetInventoryById(new(FlightId, UnpoweredTimeResource, ModuleId));
        if (inventory == null)
            return;

        var df = DF.DeepFreeze.Instance;
        var state = processor.GetResourceState(PartResourceLibrary.ElectricityHashcode);

        UpdateInventoryRate(inventory);
        UpdateVesselInfo(df, state, evt.CurrentTime);
        UpdatePartInfo(inventory, df, evt.CurrentTime);

        if (inventory.Rate > 0.0)
            KillFrozenCrew(df);
    }

    private void UpdateInventoryRate(ResourceInventory inventory)
    {
        if (inventory.Full)
            return;

        if (inventory.Rate > 0.0)
            inventory.Rate = 1.0;
        else if (inventory.Rate == 0.0)
            inventory.Amount = 0.0;
    }

    private void UpdateVesselInfo(DF.DeepFreeze df, InventoryState ec, double currentTime)
    {
        if (!df.KnownVessels.TryGetValue(Vessel.id, out var vesselInfo))
            return;

        vesselInfo.lastUpdate = currentTime;
        vesselInfo.storedEC = ec.amount;

        if (ec.rate >= 0.0)
            vesselInfo.predictedECOut = double.PositiveInfinity;
        else
            vesselInfo.predictedECOut = ec.amount / -ec.rate;
    }

    private void UpdatePartInfo(ResourceInventory inventory, DF.DeepFreeze df, double currentTime)
    {
        if (!df.KnownFreezerParts.TryGetValue(FlightId, out var partInfo))
            return;

        partInfo.lastUpdate = currentTime;

        if (inventory.Rate > 0.0)
        {
            partInfo.outofEC = true;

            if (!partInfo.ECWarning)
            {
                ScreenMessages.PostScreenMessage(
                    Localizer.Format("#autoLOC_DF_00072"),
                    10.0f,
                    ScreenMessageStyle.UPPER_CENTER
                ); //#autoLOC_DF_00072 = Insufficient electric charge to monitor frozen kerbals.

                partInfo.ECWarning = true;
            }
        }
        else
        {
            partInfo.timeLastElectricity = currentTime;
            partInfo.deathCounter = currentTime;
            partInfo.ECWarning = false;
        }
    }

    private void KillFrozenCrew(DF.DeepFreeze df)
    {
        if (!DeathFatal)
            return;

        // We already killed them, no need to do it again
        if (KerbalsAreDead)
            return;

        var deadKerbals = new List<string>();
        foreach (var (name, info) in df.FrozenKerbals)
        {
            if (info.partID != FlightId)
                continue;
            if (info.vesselID != Vessel.id)
                continue;
            if (info.type == ProtoCrewMember.KerbalType.Tourist)
                continue;

            deadKerbals.Add(name);
        }

        foreach (var kerbal in deadKerbals)
        {
            df.KillFrozenCrew(kerbal);
            ScreenMessages.PostScreenMessage(
                Localizer.Format("#autoLOC_DF_00074", kerbal),
                10.0f,
                ScreenMessageStyle.UPPER_CENTER
            ); //#autoLOC_DF_00074 = <<1>> died due to lack of Electrical Charge to run cryogenics
        }

        KerbalsAreDead = true;
    }
}

public class BackgroundDeepFreezerInventory : BackgroundInventory<DeepFreezer>
{
    private static readonly FieldInfo DeathRollField = typeof(DeepFreezer).GetField(
        "deathRoll",
        BindingFlags.Static | BindingFlags.NonPublic
    );

    private static readonly FieldInfo DeathCounterField = typeof(DeepFreezer).GetField(
        "deathCounter",
        BindingFlags.Instance | BindingFlags.NonPublic
    );

    [KSPField]
    public string ThawPotentialResource = "BRPDeepFreezerThawPotential";

    [KSPField]
    public string UnpoweredTimeResource = "BRPDeepFreezerUnpoweredTime";

    public override List<FakePartResource> GetResources(DeepFreezer module)
    {
        if (module.TotalFrozen <= 0)
            return null;
        if (!module.DFIECReqd)
            return null;

        return
        [
            new()
            {
                ResourceName = ThawPotentialResource,
                Amount = 0.0,
                MaxAmount = 0.0,
            },
            new()
            {
                ResourceName = UnpoweredTimeResource,
                Amount = 0.0,
                MaxAmount = (float)DeathRollField.GetValue(null),
            },
        ];
    }

    public override void UpdateResource(DeepFreezer module, ResourceInventory inventory)
    {
        if (inventory.ResourceName != UnpoweredTimeResource)
            return;

        var now = Planetarium.GetUniversalTime();
        module.timeSinceLastECtaken = (float)now;
        DeathCounterField.SetValue(module, now);

        throw new System.NotImplementedException();
    }
}
