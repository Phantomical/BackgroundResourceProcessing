using HarmonyLib;
using UnityEngine;

namespace BackgroundResourceProcessing.Integration.WBIResources;

[KSPAddon(KSPAddon.Startup.Instantly, true)]
public class Loader : MonoBehaviour
{
    void Awake()
    {
        Harmony harmony = new("BackgroundResourceProcessing.Integration.WBIResources");
        harmony.PatchAll(typeof(Loader).Assembly);
    }
}
