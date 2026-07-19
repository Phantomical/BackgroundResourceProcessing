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

            FoodConverterIndex = processor.AddConverter(
                new Core.ResourceConverter(
                    new TACLifeSupportBehaviour
                    {
                        SourceModule = "TAC-LS",
                        InputResourceName = global.Food,
                        PerCrewInputRate = sec2.FoodConsumptionRate,
                        OutputResourceName = global.Waste,
                        PerCrewOutputRate = sec2.WasteProductionRate,
                        NumCrew = numCrew,
                    }
                )
                {
                    Priority = 10,
                },
                options
            );

            WaterConverterIndex = processor.AddConverter(
                new Core.ResourceConverter(
                    new TACLifeSupportBehaviour
                    {
                        SourceModule = "TAC-LS",
                        InputResourceName = global.Water,
                        PerCrewInputRate = sec2.WaterConsumptionRate,
                        OutputResourceName = global.WasteWater,
                        PerCrewOutputRate = sec2.WasteWaterProductionRate,
                        NumCrew = numCrew,
                    }
                )
                {
                    Priority = 10,
                },
                options
            );

            if (NeedsOxygen(vessel))
            {
                OxygenConverterIndex = processor.AddConverter(
                    new Core.ResourceConverter(
                        new TACLifeSupportBehaviour
                        {
                            SourceModule = "TAC-LS",
                            InputResourceName = global.Oxygen,
                            PerCrewInputRate = sec2.OxygenConsumptionRate,
                            OutputResourceName = global.CO2,
                            PerCrewOutputRate = sec2.CO2ProductionRate,
                            NumCrew = numCrew,
                        }
                    )
                    {
                        Priority = 10,
                    },
                    options
                );
            }
        }

        // EC: base rate scales with occupied parts, per-crew rate scales with numCrew.
        // numOccupiedParts is fixed at record time (we can't track part occupancy for
        // unloaded vessels), but numCrew is updated as kerbals die.
        var ecBase = sec2.BaseElectricityConsumptionRate * vesselInfo.numOccupiedParts;
        var ecPerCrew = sec2.ElectricityConsumptionRate;
        if (ecBase + ecPerCrew * vesselInfo.numCrew > 0.0)
        {
            ECConverterIndex = processor.AddConverter(
                new Core.ResourceConverter(
                    new TACLifeSupportBehaviour
                    {
                        SourceModule = "TAC-LS",
                        InputResourceName = global.Electricity,
                        BaseInputRate = ecBase,
                        PerCrewInputRate = ecPerCrew,
                        NumCrew = vesselInfo.numCrew,
                    }
                )
                {
                    Priority = 10,
                },
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
    /// has been depleted long enough to exceed the threshold, honouring the user's
    /// Hibernate/Die setting the same way <c>LifeSupportController</c> does.
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
        var converters = processor.Converters;
        double now = evt.CurrentChangepoint;

        // TAC-LS reads its limits from the difficulty params (not globalSettings,
        // which goes stale once the user edits them) and treats any hibernate
        // value other than "Die" as hibernation.
        var sec1 = HighLogic.CurrentGame.Parameters.CustomParams<TAC_SettingsParms>();
        var sec3 = HighLogic.CurrentGame.Parameters.CustomParams<TAC_SettingsParms_Sec3>();
        bool die = sec1.hibernate == "Die";

        var foodBehaviour = GetBehaviour(converters, FoodConverterIndex);
        var waterBehaviour = GetBehaviour(converters, WaterConverterIndex);
        var oxygenBehaviour = GetBehaviour(converters, OxygenConverterIndex);
        var ecBehaviour = GetBehaviour(converters, ECConverterIndex);

        int initialCrew = vesselInfo.numCrew;
        int killed = 0;

        // O2 and EC are tracked vessel-wide (no per-kerbal respite): hibernation
        // covers the whole crew, death takes one random kerbal per trigger.
        if (oxygenBehaviour != null)
        {
            if (DeadlineExpired(oxygenBehaviour, now, sec3.MaxTimeWithoutOxygen))
            {
                if (die)
                    killed += KillRandomCrew(
                        controller,
                        oxygenBehaviour,
                        sec3.MaxTimeWithoutOxygen,
                        now,
                        60,
                        600,
                        "oxygen deprivation"
                    );
                else
                    controller.HibernateCrewMembers(vessel, vesselInfo, "O2");
            }
            else if (oxygenBehaviour.lastNotSatisfied == null && !die)
            {
                controller.RemoveHibernationStates(vessel, vesselInfo, "O2");
            }
        }

        if (ecBehaviour != null)
        {
            if (DeadlineExpired(ecBehaviour, now, sec3.MaxTimeWithoutElectricity))
            {
                if (die)
                {
                    killed += KillRandomCrew(
                        controller,
                        ecBehaviour,
                        sec3.MaxTimeWithoutElectricity,
                        now,
                        500,
                        2000,
                        "air toxicity"
                    );

                    // TAC-LS clamps lastElectricity to the current time; O2 has
                    // no such clamp.
                    if (ecBehaviour.lastNotSatisfied is double lastEC)
                        ecBehaviour.lastNotSatisfied = System.Math.Min(lastEC, now);
                }
                else
                {
                    controller.HibernateCrewMembers(vessel, vesselInfo, "EC");
                }
            }
            else if (ecBehaviour.lastNotSatisfied == null && !die)
            {
                controller.RemoveHibernationStates(vessel, vesselInfo, "EC");
            }
        }

        // Food and water are per-kerbal with a respite grace period.
        foreach (var crewInfo in vesselInfo.CrewInVessel)
        {
            if (crewInfo.DFfrozen)
                continue;

            bool starved = DeadlineExpired(
                foodBehaviour,
                now,
                sec3.MaxTimeWithoutFood + crewInfo.respite
            );
            bool dehydrated = DeadlineExpired(
                waterBehaviour,
                now,
                sec3.MaxTimeWithoutWater + crewInfo.respite
            );

            if (die)
            {
                if (!starved && !dehydrated)
                    continue;

                var protoMember = vessel
                    .protoVessel?.GetVesselCrew()
                    .Find(c => c.name == crewInfo.name);
                if (protoMember == null)
                    continue;

                controller.KillCrewMember(
                    protoMember,
                    starved ? "starvation" : "dehydration",
                    vessel
                );
                killed++;
            }
            else
            {
                if (starved)
                    controller.HibernateCrewMember(crewInfo, vessel, vesselInfo, "Food");
                else if (
                    foodBehaviour != null
                    && foodBehaviour.lastNotSatisfied == null
                    && crewInfo.hibernating
                    && crewInfo.lackofFood
                )
                    controller.RemoveHibernationState(crewInfo, vessel, "Food");

                if (dehydrated)
                    controller.HibernateCrewMember(crewInfo, vessel, vesselInfo, "Water");
                else if (
                    waterBehaviour != null
                    && waterBehaviour.lastNotSatisfied == null
                    && crewInfo.hibernating
                    && crewInfo.lackofWater
                )
                    controller.RemoveHibernationState(crewInfo, vessel, "Water");
            }
        }

        if (killed > 0)
        {
            int survivingCrew = System.Math.Max(0, initialCrew - killed);
            UpdateCrewRates(
                survivingCrew,
                foodBehaviour,
                waterBehaviour,
                oxygenBehaviour,
                ecBehaviour
            );
            processor.MarkDirty();
        }
    }

    private static bool DeadlineExpired(
        TACLifeSupportBehaviour behaviour,
        double now,
        double maxTimeWithout
    )
    {
        return behaviour?.lastNotSatisfied != null
            && (now - behaviour.lastNotSatisfied.Value) > maxTimeWithout;
    }

    /// <summary>
    /// Replays TAC-LS's fatal O2/EC starvation over the elapsed background time:
    /// the mod kills one random kerbal each time the threshold is crossed and then
    /// pushes the depletion timestamp forward by a random stagger interval.
    /// (TAC-LS's <c>Random.Range(0, crew.Count - 1)</c> off-by-one, which never
    /// picks the last-listed kerbal, is deliberately not reproduced.)
    /// </summary>
    private int KillRandomCrew(
        LifeSupportController controller,
        TACLifeSupportBehaviour behaviour,
        double maxTimeWithout,
        double now,
        int minStagger,
        int maxStagger,
        string causeOfDeath
    )
    {
        int killed = 0;
        while (
            behaviour.lastNotSatisfied != null
            && (now - behaviour.lastNotSatisfied.Value) > maxTimeWithout
        )
        {
            var crew = vessel.protoVessel?.GetVesselCrew();
            if (crew == null || crew.Count == 0)
                break;

            var member = crew[Random.Range(0, crew.Count)];
            controller.KillCrewMember(member, causeOfDeath, vessel);
            killed++;

            behaviour.lastNotSatisfied += Random.Range(minStagger, maxStagger);
        }
        return killed;
    }

    private static void UpdateCrewRates(
        int count,
        TACLifeSupportBehaviour foodBehaviour,
        TACLifeSupportBehaviour waterBehaviour,
        TACLifeSupportBehaviour oxygenBehaviour,
        TACLifeSupportBehaviour ecBehaviour
    )
    {
        if (foodBehaviour != null)
            foodBehaviour.NumCrew = count;
        if (waterBehaviour != null)
            waterBehaviour.NumCrew = count;
        if (oxygenBehaviour != null)
            oxygenBehaviour.NumCrew = count;
        if (ecBehaviour != null)
            ecBehaviour.NumCrew = count;
    }

    private static bool NeedsOxygen(Vessel vessel)
    {
        if (
            vessel.mainBody == FlightGlobals.GetHomeBody()
            || vessel.mainBody.atmosphereContainsOxygen
        )
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
