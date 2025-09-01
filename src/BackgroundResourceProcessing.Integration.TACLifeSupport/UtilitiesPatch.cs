using HarmonyLib;
using Tac;

namespace BackgroundResourceProcessing.Integration.TACLifeSupport;

[HarmonyPatch(typeof(Utilities), nameof(Utilities.FormatTime))]
public static class Utilities_FormatTime_Patch
{
    static bool Prefix(ref string __result, double value)
    {
        if (double.IsPositiveInfinity(value))
        {
            __result = "indefinite";
            return false;
        }

        return true;
    }
}
