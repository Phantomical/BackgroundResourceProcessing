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
[HarmonyPatch]
public static class LifeSupportController_ConsumeResources_Patch
{
    static MethodBase TargetMethod() =>
        typeof(LifeSupportController).GetMethod(
            "ConsumeResources",
            BindingFlags.Instance | BindingFlags.NonPublic,
            null,
            [typeof(double), typeof(Vessel), typeof(VesselInfo)],
            null
        );

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
