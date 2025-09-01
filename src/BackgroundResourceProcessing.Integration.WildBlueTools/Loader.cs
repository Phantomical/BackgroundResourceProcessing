using HarmonyLib;
using UnityEngine;

namespace BackgroundResourceProcessing.Integration.WildBlueTools;

[KSPAddon(KSPAddon.Startup.Instantly, true)]
public class Loader : MonoBehaviour
{
    void Awake()
    {
        Harmony harmony = new("BackgroundResourceProcessing.Integration.WildBlueTools");
        harmony.PatchAll(typeof(Loader).Assembly);
    }
}
