using HarmonyLib;
using UnityEngine;

namespace BackgroundResourceProcessing.Integration.Snacks;

[KSPAddon(KSPAddon.Startup.Instantly, true)]
public class Loader : MonoBehaviour
{
    void Awake()
    {
        Harmony harmony = new("BackgroundResourceProcessing.Integration.PersistentThrust");
        harmony.PatchAll(typeof(Loader).Assembly);
    }
}
