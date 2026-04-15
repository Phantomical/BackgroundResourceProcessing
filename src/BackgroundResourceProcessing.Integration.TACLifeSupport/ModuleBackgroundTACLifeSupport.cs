using System.Collections.Generic;
using BackgroundResourceProcessing.Collections;
using BackgroundResourceProcessing.Core;
using Tac;
using UnityEngine;

namespace BackgroundResourceProcessing.Integration.TACLifeSupport;

/// <summary>
/// VesselModule that registers TAC-LS life support consumption converters with BRP
/// and synchronises TAC-LS tracking state when a vessel loads.
/// </summary>
public class ModuleBackgroundTACLifeSupport : VesselModule
{
    [KSPField(isPersistant = true)]
    public int FoodConverterIndex = -1;

    [KSPField(isPersistant = true)]
    public int WaterConverterIndex = -1;

    [KSPField(isPersistant = true)]
    public int OxygenConverterIndex = -1;

    [KSPField(isPersistant = true)]
    public int ECConverterIndex = -1;

    public override Activation GetActivation() => Activation.LoadedOrUnloaded;

    public override bool ShouldBeActive() =>
        HighLogic.LoadedScene switch
        {
            GameScenes.FLIGHT or GameScenes.SPACECENTER or GameScenes.TRACKSTATION => true,
            _ => false,
        };

    /// <summary>
    /// Called when the vessel is recorded (unloaded). Registers life support
    /// resource converters with the BRP processor.
    /// </summary>
    internal void OnRecord(BackgroundResourceProcessor processor)
    {
        ClearState();

        if (!IsEnabled())
            return;

        if (vessel.isEVA)
            return;

        if (vessel.situation == Vessel.Situations.PRELAUNCH)
            return;

        var tacSettings = TacLifeSupport.Instance?.gameSettings;
        if (tacSettings == null)
            return;

        if (!tacSettings.knownVessels.Contains(vessel.id))
            return;

        var vesselInfo = tacSettings.knownVessels[vessel.id];

        if (vesselInfo.numCrew == 0 && vesselInfo.numFrozenCrew == 0)
            return;

        var global = TacStartOnce.Instance.globalSettings;
        var sec2 = HighLogic.CurrentGame.Parameters.CustomParams<TAC_SettingsParms_Sec2>();
        var options = new AddConverterOptions { LinkToAll = true };

        if (vesselInfo.numCrew > 0)
        {
            int numCrew = vesselInfo.numCrew;

            var foodBehaviour = new TACLifeSupportBehaviour(
                [
                    new()
                    {
                        ResourceName = global.Food,
                        Ratio = sec2.FoodConsumptionRate * numCrew,
                        FlowMode = ResourceFlowMode.ALL_VESSEL,
                    },
                ],
                [
                    new()
                    {
                        ResourceName = global.Waste,
                        Ratio = sec2.WasteProductionRate * numCrew,
                        FlowMode = ResourceFlowMode.ALL_VESSEL,
                        DumpExcess = true,
                    },
                ]
            );
            FoodConverterIndex = processor.AddConverter(
                new Core.ResourceConverter(foodBehaviour) { Priority = 10 },
                options
            );

            var waterBehaviour = new TACLifeSupportBehaviour(
                [
                    new()
                    {
                        ResourceName = global.Water,
                        Ratio = sec2.WaterConsumptionRate * numCrew,
                        FlowMode = ResourceFlowMode.ALL_VESSEL,
                    },
                ],
                [
                    new()
                    {
                        ResourceName = global.WasteWater,
                        Ratio = sec2.WasteWaterProductionRate * numCrew,
                        FlowMode = ResourceFlowMode.ALL_VESSEL,
                        DumpExcess = true,
                    },
                ]
            );
            WaterConverterIndex = processor.AddConverter(
                new Core.ResourceConverter(waterBehaviour) { Priority = 10 },
                options
            );

            if (NeedsOxygen(vessel))
            {
                var oxygenBehaviour = new TACLifeSupportBehaviour(
                    [
                        new()
                        {
                            ResourceName = global.Oxygen,
                            Ratio = sec2.OxygenConsumptionRate * numCrew,
                            FlowMode = ResourceFlowMode.ALL_VESSEL,
                        },
                    ],
                    [
                        new()
                        {
                            ResourceName = global.CO2,
                            Ratio = sec2.CO2ProductionRate * numCrew,
                            FlowMode = ResourceFlowMode.ALL_VESSEL,
                            DumpExcess = true,
                        },
                    ]
                );
                OxygenConverterIndex = processor.AddConverter(
                    new Core.ResourceConverter(oxygenBehaviour) { Priority = 10 },
                    options
                );
            }
        }

        // EC is consumed by all crew including frozen
        var ecRate = vesselInfo.estimatedElectricityConsumptionRate;
        if (ecRate > 0.0)
        {
            var ecBehaviour = new TACLifeSupportBehaviour(
                [
                    new()
                    {
                        ResourceName = global.Electricity,
                        Ratio = ecRate,
                        FlowMode = ResourceFlowMode.ALL_VESSEL,
                    },
                ],
                []
            );
            ECConverterIndex = processor.AddConverter(
                new Core.ResourceConverter(ecBehaviour) { Priority = 10 },
                options
            );
        }
    }

    /// <summary>
    /// Called when the vessel is restored (loaded). Synchronises TAC-LS tracking
    /// timestamps from BRP's behaviour state so that death/hibernation triggers
    /// correctly on the next FixedUpdate.
    /// </summary>
    internal void OnRestore(BackgroundResourceProcessor processor)
    {
        if (!IsEnabled())
            return;

        var tacSettings = TacLifeSupport.Instance?.gameSettings;
        if (tacSettings == null)
            return;

        if (!tacSettings.knownVessels.Contains(vessel.id))
            return;

        var vesselInfo = tacSettings.knownVessels[vessel.id];
        var converters = processor.Converters;
        var now = Planetarium.GetUniversalTime();

        var foodBehaviour = GetBehaviour(converters, FoodConverterIndex);
        var waterBehaviour = GetBehaviour(converters, WaterConverterIndex);
        var oxygenBehaviour = GetBehaviour(converters, OxygenConverterIndex);
        var ecBehaviour = GetBehaviour(converters, ECConverterIndex);

        // Update vessel-level timestamps.
        // lastNotSatisfied = T means the resource ran out at T.
        // Setting lastFood = T gives TAC-LS timeWithoutFood = (now - T) which is correct.
        // lastNotSatisfied = null means the resource was always available, so lastFood = now
        // and TAC-LS will compute deltaTime ≈ 0 on the next tick (no double deduction).
        if (foodBehaviour != null)
            vesselInfo.lastFood = foodBehaviour.lastNotSatisfied ?? now;
        if (waterBehaviour != null)
            vesselInfo.lastWater = waterBehaviour.lastNotSatisfied ?? now;
        if (oxygenBehaviour != null)
            vesselInfo.lastOxygen = oxygenBehaviour.lastNotSatisfied ?? now;
        if (ecBehaviour != null)
            vesselInfo.lastElectricity = ecBehaviour.lastNotSatisfied ?? now;

        // Update per-kerbal timestamps from vesselInfo.CrewInVessel.
        foreach (var crewInfo in vesselInfo.CrewInVessel)
        {
            if (crewInfo.DFfrozen)
            {
                // Frozen crew do not consume food/water/oxygen but do consume EC.
                if (ecBehaviour != null)
                    crewInfo.lastEC = ecBehaviour.lastNotSatisfied ?? now;
            }
            else
            {
                if (foodBehaviour != null)
                    crewInfo.lastFood = foodBehaviour.lastNotSatisfied ?? now;
                if (waterBehaviour != null)
                    crewInfo.lastWater = waterBehaviour.lastNotSatisfied ?? now;
                if (oxygenBehaviour != null)
                    crewInfo.lastO2 = oxygenBehaviour.lastNotSatisfied ?? now;
                if (ecBehaviour != null)
                    crewInfo.lastEC = ecBehaviour.lastNotSatisfied ?? now;
            }
        }

        ClearState();
    }

    /// <summary>
    /// Called at each BRP changepoint. Fires TAC-LS death/hibernation if a resource
    /// has been depleted long enough to exceed the threshold.
    /// </summary>
    internal void OnChangepoint(BackgroundResourceProcessor processor, ChangepointEvent evt)
    {
        if (!IsEnabled())
            return;

        var tacSettings = TacLifeSupport.Instance?.gameSettings;
        if (tacSettings == null || !tacSettings.knownVessels.Contains(vessel.id))
            return;

        var vesselInfo = tacSettings.knownVessels[vessel.id];
        var controller = LifeSupportController.Instance;
        var global = TacStartOnce.Instance.globalSettings;
        var converters = processor.Converters;
        double now = evt.CurrentChangepoint;

        var foodBehaviour = GetBehaviour(converters, FoodConverterIndex);
        var waterBehaviour = GetBehaviour(converters, WaterConverterIndex);
        var oxygenBehaviour = GetBehaviour(converters, OxygenConverterIndex);
        var ecBehaviour = GetBehaviour(converters, ECConverterIndex);

        // O2 and EC: vessel-level hibernation (no per-kerbal respite)
        if (
            oxygenBehaviour?.lastNotSatisfied != null
            && (now - oxygenBehaviour.lastNotSatisfied.Value) > global.MaxTimeWithoutOxygen
        )
            controller.HibernateCrewMembers(vessel, vesselInfo, "O2");

        if (
            ecBehaviour?.lastNotSatisfied != null
            && (now - ecBehaviour.lastNotSatisfied.Value) > global.MaxTimeWithoutElectricity
        )
            controller.HibernateCrewMembers(vessel, vesselInfo, "EC");

        // Food and water: per-kerbal kill with respite grace period
        foreach (var crewInfo in vesselInfo.CrewInVessel)
        {
            if (crewInfo.DFfrozen || crewInfo.hibernating)
                continue;

            var protoMember = vessel.protoVessel
                ?.GetVesselCrew()
                .Find(c => c.name == crewInfo.name);
            if (protoMember == null)
                continue;

            if (
                foodBehaviour?.lastNotSatisfied != null
                && (now - foodBehaviour.lastNotSatisfied.Value)
                    > (global.MaxTimeWithoutFood + crewInfo.respite)
            )
                controller.KillCrewMember(protoMember, "starvation", vessel);

            if (
                waterBehaviour?.lastNotSatisfied != null
                && (now - waterBehaviour.lastNotSatisfied.Value)
                    > (global.MaxTimeWithoutWater + crewInfo.respite)
            )
                controller.KillCrewMember(protoMember, "dehydration", vessel);
        }
    }

    private static bool NeedsOxygen(Vessel vessel)
    {
        if (vessel.mainBody == FlightGlobals.GetHomeBody() || vessel.mainBody.atmosphereContainsOxygen)
        {
            double seaLevelPressure = vessel.mainBody.GetPressure(0);
            if (seaLevelPressure <= 0)
                return true;

            double atmDensity = vessel.staticPressurekPa / seaLevelPressure;
            if (atmDensity > 0.2)
                return false;
        }
        return true;
    }

    private bool IsEnabled()
    {
        var settings = HighLogic.CurrentGame?.Parameters.CustomParams<ModIntegrationSettings>();
        if (!(settings?.EnableTACLSIntegration ?? false))
            return false;

        if (TacLifeSupport.Instance == null)
            return false;

        if (TacStartOnce.Instance == null)
            return false;

        return true;
    }

    private void ClearState()
    {
        FoodConverterIndex = -1;
        WaterConverterIndex = -1;
        OxygenConverterIndex = -1;
        ECConverterIndex = -1;
    }

    private static TACLifeSupportBehaviour GetBehaviour(
        StableListView<Core.ResourceConverter> converters,
        int index
    )
    {
        if (index < 0 || index >= converters.Count)
            return null;

        return converters[index].Behaviour as TACLifeSupportBehaviour;
    }
}
