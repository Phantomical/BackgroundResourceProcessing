using HarmonyLib;
using UnityEngine;

namespace BackgroundResourceProcessing.Integration.TACLifeSupport;

[KSPAddon(KSPAddon.Startup.Instantly, true)]
public class Loader : MonoBehaviour
{
    void Awake()
    {
        try
        {
            Harmony.DEBUG = true;
            Harmony harmony = new("BackgroundResourceProcessing.Integration.TACLifeSupport");
            harmony.PatchAll(typeof(Loader).Assembly);
        }
        finally
        {
            Harmony.DEBUG = false;
        }
    }
}
