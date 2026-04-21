using HarmonyLib;
using UnityEngine;

namespace BackgroundResourceProcessing.Integration.WildBlueTools;

[KSPAddon(KSPAddon.Startup.Instantly, true)]
public class Loader : MonoBehaviour
{
    private static readonly Harmony harmony = new(
        "BackgroundResourceProcessing.Integration.WildBlueTools"
    );

    void Awake()
    {
        harmony.PatchAll(typeof(Loader).Assembly);
    }

    static void OnHotLoad()
    {
        harmony.UnpatchAll(harmony.Id);
        harmony.PatchAll(typeof(Loader).Assembly);
    }
}
