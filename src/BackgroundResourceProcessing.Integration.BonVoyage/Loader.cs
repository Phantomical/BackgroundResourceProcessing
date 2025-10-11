using HarmonyLib;
using UnityEngine;

namespace BackgroundResourceProcessing.Integration.BonVoyage;

[KSPAddon(KSPAddon.Startup.Instantly, true)]
public class Loader : MonoBehaviour
{
    void Awake()
    {
        var harmony = new Harmony("BackgroundResourceProcessing.Integration.BonVoyage");
        harmony.PatchAll(typeof(Loader).Assembly);
    }
}
