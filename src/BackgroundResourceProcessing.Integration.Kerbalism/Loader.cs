using HarmonyLib;
using UnityEngine;

namespace BackgroundResourceProcessing.Integration.Kerbalism;

[KSPAddon(KSPAddon.Startup.Instantly, true)]
public class Loader : MonoBehaviour
{
    void Awake()
    {
        Harmony harmony = new("BackgroundResourceProcessing.Integration.Kerbalism");
        harmony.PatchAll(typeof(Loader).Assembly);
    }
}
