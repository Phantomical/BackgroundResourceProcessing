using HarmonyLib;
using UnityEngine;

namespace BackgroundResourceProcessing.Integration.DeepFreeze;

[KSPAddon(KSPAddon.Startup.Instantly, true)]
public class Loader : MonoBehaviour
{
    private static readonly Harmony harmony = new(
        "BackgroundResourceProcessing.Integration.DeepFreeze"
    );

    static void OnHotLoad()
    {
        harmony.UnpatchAll(harmony.Id);
        harmony.PatchAll(typeof(Loader).Assembly);
    }
}
