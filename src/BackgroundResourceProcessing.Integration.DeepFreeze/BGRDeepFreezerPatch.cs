using System.Reflection;
using HarmonyLib;

namespace BackgroundResourceProcessing.Integration.DeepFreeze;

/// <summary>
/// Suppresses BackgroundResources' own DeepFreezer EC consumption when BRP is
/// handling it. Without this patch both systems would consume EC for the same
/// frozen kerbals on unloaded vessels.
/// </summary>
[HarmonyPatch]
static class BGRDeepFreezer_ProcessHandler_Patch
{
    // BackgroundResources is optional. Without this, TargetMethod returns null
    // when it isn't installed, which throws out of PatchAll and prevents every
    // other patch in this assembly from being applied.
    static bool Prepare() => AccessTools.TypeByName("BackgroundResources.DeepFreezer") != null;

    static MethodBase TargetMethod() =>
        AccessTools.Method("BackgroundResources.DeepFreezer:ProcessHandler");

    static bool Prefix()
    {
        var settings = HighLogic.CurrentGame?.Parameters.CustomParams<ModIntegrationSettings>();

        // Return false (suppress BGR's processing) when BRP is handling DeepFreeze.
        // Return true (let BGR run) when our integration is disabled.
        return !(settings?.EnableDeepFreezeIntegration ?? false);
    }
}
