using HarmonyLib;
using UnityEngine;

namespace BackgroundResourceProcessing.Integration.TACLifeSupport;

[KSPAddon(KSPAddon.Startup.Instantly, true)]
public class Loader : MonoBehaviour
{
    private static readonly Harmony harmony = new(
        "BackgroundResourceProcessing.Integration.TACLifeSupport"
    );

    void Awake()
    {
        harmony.PatchAll(typeof(Loader).Assembly);
    }

    static void OnHotUnload()
    {
        harmony.UnpatchAll(harmony.Id);
    }
}
