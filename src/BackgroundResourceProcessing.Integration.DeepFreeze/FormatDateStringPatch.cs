using HarmonyLib;
using RSTUtils;

namespace BackgroundResourceProcessing.Integration.DeepFreeze;

/// <summary>
/// Render +Infinity as "∞" instead of the integer-overflow garbage produced by
/// DF's native formatter, which casts the input directly to <c>int</c>. The
/// simulator-backed TimeRem column can now legitimately return +Infinity when
/// a vessel is permanently powered (e.g. solar-positive in steady state).
/// </summary>
[HarmonyPatch(typeof(Utilities), "FormatDateString")]
static class FormatDateString_Patch
{
    static bool Prefix(double time, ref string __result)
    {
        if (double.IsPositiveInfinity(time))
        {
            __result = "∞";
            return false;
        }

        return true;
    }
}
