using System.IO;
using BackgroundResourceProcessing.Addons;
using HarmonyLib;
using KerbalismBootstrap;
using UnityEngine;
using static BackgroundResourceProcessing.Addons.BackgroundResourceProcessingLoader;

namespace BackgroundResourceProcessing.Integration.KerbalismBootstrap;

[KSPAddon(KSPAddon.Startup.Instantly, true)]
public class KerbalismLoader : MonoBehaviour
{
    void Awake()
    {
        Harmony harmony = new("BackgroundResourceProcessing.Integration.KerbalismBootstrap");
        harmony.PatchAll(typeof(KerbalismLoader).Assembly);
    }
}

[HarmonyPatch(typeof(Bootstrap), nameof(Bootstrap.Start))]
static class Bootstrap_Start_Patch
{
    static void Postfix()
    {
        var dir = GetPluginDirectory();
        var plugin = Path.Combine(
            dir,
            "BackgroundResourceProcessing.Integration.Kerbalism.dll.plugin"
        );

        if (!File.Exists(plugin))
        {
            LogUtil.Warn("Kerbalism integration plugin was not found on disk");
            return;
        }

        LogUtil.Log(
            "Loading Background Resource Processing Kerbalism integration after kerbalism bootstrap"
        );
        LoadPlugin(plugin);
    }
}
