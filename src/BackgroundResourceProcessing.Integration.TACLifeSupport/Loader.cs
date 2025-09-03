using HarmonyLib;
using UnityEngine;

namespace BackgroundResourceProcessing.Integration.TACLifeSupport;

[KSPAddon(KSPAddon.Startup.Instantly, true)]
public class Loader : MonoBehaviour
{
    void Awake()
    {
        Harmony harmony = new("BackgroundResourceProcessing.Integration.TACLifeSupport");
        harmony.PatchAll(typeof(Loader).Assembly);
    }
}
