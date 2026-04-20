using System.Reflection;
using HarmonyLib;
using Tac;

namespace BackgroundResourceProcessing.Integration.TACLifeSupport;

/// <summary>
/// Prevents double-consumption of life support resources on unloaded vessels
/// when both the old BackgroundResources mod and BRP are installed simultaneously.
/// </summary>
///
/// <remarks>
/// Without the old BackgroundResources mod, TAC-LS already skips
/// <c>ConsumeResources</c> for unloaded vessels (<c>backgroundResourcesAvailable = false</c>),
/// so this patch is a no-op in the common case. It is included defensively to handle
/// the edge case where both mods coexist.
/// </remarks>
[HarmonyPatch(typeof(LifeSupportController), nameof(LifeSupportController.ConsumeResources))]
public static class LifeSupportController_ConsumeResources_Patch
{
    static bool Prefix(Vessel vessel)
    {
        // Never interfere with loaded vessel processing.
        if (vessel.loaded)
            return true;

        var settings = HighLogic.CurrentGame?.Parameters.CustomParams<ModIntegrationSettings>();
        if (!(settings?.EnableTACLSIntegration ?? false))
            return true;

        var module = vessel.FindVesselModuleImplementing<ModuleBackgroundTACLifeSupport>();
        if (module == null)
            return true;

        // If BRP has registered converters for this vessel, skip TAC-LS's own
        // consumption to prevent double-deduction of resources.
        return module.ECConverterIndex < 0;
    }
}

/// <summary>
/// Replaces TAC-LS's <c>doWarningProcessing</c> for BRP-managed unloaded
/// vessels, writing BRP's simulation results directly into
/// <see cref="VesselInfo"/> so the monitoring window, roster window, and
/// app-icon color are authoritative.
/// </summary>
///
/// <remarks>
/// TAC-LS's native implementation computes <c>estimatedTime*Depleted</c> as
/// <c>last* + remaining* / rate</c>. For unloaded vessels whose simulation
/// BRP owns this value is wrong in two ways: <c>last*</c> drifts because we
/// skip <c>ConsumeResources</c>, and the rate doesn't account for
/// production (e.g. solar panels). We substitute BRP's simulated depletion
/// times and then call TAC-LS's own <c>ShowWarnings</c> to keep the screen
/// message / time-warp-stop behaviour on status transitions. When BRP's
/// simulation projects infinite life we pass an above-threshold remainder
/// to <c>ShowWarnings</c> so it sets GOOD and suppresses the spurious
/// "running out" alert even if the current battery level is momentarily
/// below the warning percent.
/// </remarks>
[HarmonyPatch(typeof(LifeSupportController), nameof(LifeSupportController.doWarningProcessing))]
public static class LifeSupportController_doWarningProcessing_Patch
{
    static bool Prefix(LifeSupportController __instance, VesselInfo vesselInfo, double currentTime)
    {
        if (vesselInfo == null)
            return true;

        var settings = HighLogic.CurrentGame?.Parameters.CustomParams<ModIntegrationSettings>();
        if (!(settings?.EnableTACLSIntegration ?? false))
            return true;

        var vessel = FindVessel(vesselInfo);
        if (vessel == null || vessel.loaded)
            return true;

        var module = vessel.FindVesselModuleImplementing<ModuleBackgroundTACLifeSupport>();
        if (module == null)
            return true;

        if (
            module.FoodConverterIndex < 0
            && module.WaterConverterIndex < 0
            && module.OxygenConverterIndex < 0
            && module.ECConverterIndex < 0
        )
            return true;

        var processor = vessel.FindVesselModuleImplementing<BackgroundResourceProcessor>();
        if (processor == null)
            return true;

        var stats = VesselSimulationCache.GetInstance().GetCachedVesselStats(vessel, vesselInfo);
        if (stats == null)
            return true;

        var global = TacStartOnce.Instance.globalSettings;
        var s = stats.Value;

        ApplyResource(
            __instance,
            processor,
            vesselInfo,
            global.Food,
            global.displayFood,
            s.FoodExhaustedUT,
            module.FoodConverterIndex,
            ref vesselInfo.estimatedTimeFoodDepleted,
            ref vesselInfo.foodStatus
        );
        ApplyResource(
            __instance,
            processor,
            vesselInfo,
            global.Water,
            global.displayWater,
            s.WaterExhaustedUT,
            module.WaterConverterIndex,
            ref vesselInfo.estimatedTimeWaterDepleted,
            ref vesselInfo.waterStatus
        );
        ApplyResource(
            __instance,
            processor,
            vesselInfo,
            global.Oxygen,
            global.displayOxygen,
            s.OxygenExhaustedUT,
            module.OxygenConverterIndex,
            ref vesselInfo.estimatedTimeOxygenDepleted,
            ref vesselInfo.oxygenStatus
        );
        ApplyResource(
            __instance,
            processor,
            vesselInfo,
            global.Electricity,
            global.displayElectricity,
            s.ElectricityExhaustedUT,
            module.ECConverterIndex,
            ref vesselInfo.estimatedTimeElectricityDepleted,
            ref vesselInfo.electricityStatus
        );

        vesselInfo.overallStatus =
            vesselInfo.foodStatus
            | vesselInfo.oxygenStatus
            | vesselInfo.waterStatus
            | vesselInfo.electricityStatus;
        __instance.overallLifeSupportStatus |= vesselInfo.overallStatus;

        // TAC-LS's monitoring window shows `currentTime - lastUpdate` as
        // "last update". Because BRP skips ConsumeResources for these
        // vessels, lastUpdate would otherwise drift — pin it to now.
        vesselInfo.lastUpdate = currentTime;

        return false;
    }

    static void ApplyResource(
        LifeSupportController controller,
        BackgroundResourceProcessor processor,
        VesselInfo vesselInfo,
        string resourceName,
        string displayName,
        double exhaustedUT,
        int converterIndex,
        ref double estimatedDepleted,
        ref VesselInfo.Status status
    )
    {
        if (converterIndex < 0)
            return;

        estimatedDepleted = exhaustedUT;

        var state = processor.GetCurrentResourceState(resourceName);
        controller.ShowWarnings(
            vesselInfo.vesselName,
            state.amount,
            state.maxAmount,
            0.0,
            displayName,
            ref status
        );
    }

    static Vessel FindVessel(VesselInfo vesselInfo)
    {
        var knownVessels = TacLifeSupport.Instance?.gameSettings?.knownVessels;
        if (knownVessels == null)
            return null;

        foreach (var vessel in FlightGlobals.Vessels)
        {
            if (vessel == null)
                continue;
            if (!knownVessels.TryGetValue(vessel.id, out var knownVessel))
                continue;
            if (ReferenceEquals(knownVessel, vesselInfo))
                return vessel;
        }

        return null;
    }
}
