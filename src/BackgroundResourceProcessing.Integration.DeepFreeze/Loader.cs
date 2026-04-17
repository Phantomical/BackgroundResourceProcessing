using HarmonyLib;
using UnityEngine;

namespace BackgroundResourceProcessing.Integration.DeepFreeze;

[KSPAddon(KSPAddon.Startup.Instantly, true)]
public class Loader : MonoBehaviour
{
    void Awake()
    {
        var harmony = new Harmony("BackgroundResourceProcessing.Integration.DeepFreeze");
        harmony.PatchAll(typeof(Loader).Assembly);
    }
}
