using HarmonyLib;
using UnityEngine;

namespace BackgroundResourceProcessing.Integration.Snacks;

[KSPAddon(KSPAddon.Startup.Instantly, true)]
public class Loader : MonoBehaviour
{
    private static readonly Harmony harmony = new(
        "BackgroundResourceProcessing.Integration.Snacks"
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
