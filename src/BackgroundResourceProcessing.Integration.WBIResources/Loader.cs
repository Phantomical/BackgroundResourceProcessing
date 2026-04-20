using HarmonyLib;
using UnityEngine;

namespace BackgroundResourceProcessing.Integration.WBIResources;

[KSPAddon(KSPAddon.Startup.Instantly, true)]
public class Loader : MonoBehaviour
{
    private static readonly Harmony harmony = new(
        "BackgroundResourceProcessing.Integration.WBIResources"
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
