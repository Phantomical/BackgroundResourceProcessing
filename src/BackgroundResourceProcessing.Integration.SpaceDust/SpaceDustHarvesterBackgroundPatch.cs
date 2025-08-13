using System;
using BackgroundResourceProcessing;
using HarmonyLib;
using SpaceDust;
using UnityEngine;

namespace BackgroundResourceProcessing.Integration.SpaceDust;

[KSPAddon(KSPAddon.Startup.Instantly, true)]
public class Loader : MonoBehaviour
{
    void Awake()
    {
        Harmony harmony = new("BackgroundResourceProcessing.Integration.SpaceDust");
        harmony.PatchAll(typeof(Loader).Assembly);
    }
}

[HarmonyPatch(typeof(SpaceDustHarvesterBackground))]
[HarmonyPatch("AddBackgroundResources")]
public class SpaceDustHarvesterBackground_AddBackgroundResources_Patch
{
    static bool Prefix(ProtoVessel protoVessel, string resourceName, double amount)
    {
        try
        {
            var settings = HighLogic.CurrentGame?.Parameters.CustomParams<Settings>();
            var enabled = settings?.EnableSpaceDustIntegration ?? false;
            if (!enabled)
                return true;

            var vessel = protoVessel.vesselRef;
            var processor = vessel?.FindVesselModuleImplementing<BackgroundResourceProcessor>();
            if (processor == null)
                return true;

            processor.AddResource(resourceName, amount);
            return false;
        }
        catch (Exception e)
        {
            LogUtil.Error($"Patched AddBackgroundResource method threw an exception: {e}");
            return true;
        }
    }
}
