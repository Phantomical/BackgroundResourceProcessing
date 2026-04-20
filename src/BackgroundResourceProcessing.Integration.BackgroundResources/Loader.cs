using System.Reflection;
using BackgroundResources;
using HarmonyLib;
using UnityEngine;

namespace BackgroundResourceProcessing.Integration.BackgroundResources;

[KSPAddon(KSPAddon.Startup.Instantly, true)]
public class Loader : MonoBehaviour
{
    private static readonly Harmony harmony = new(
        "BackgroundResourceProcessing.Integration.BackgroundResources"
    );

    void Awake()
    {
        harmony.PatchAll(typeof(Loader).Assembly);
    }

    void Start()
    {
        UnloadedResources.BackgroundProcessingInstalled = true;

        LogUtil.Log(
            "Disabling BackgroundResources. Please report all bugs with background resource handling to BackgroundResourceProcessing."
        );
    }

    static void OnHotUnload()
    {
        harmony.UnpatchAll(harmony.Id);
    }
}

[HarmonyPatch(typeof(UnloadedResources))]
[HarmonyPatch("OnAwake")]
public static class UnloadedResources_OnAwake_Patch
{
    private const BindingFlags Flags =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private static readonly FieldInfo LoggedBackgroundProcessingField =
        typeof(UnloadedResources).GetField("loggedBackgroundProcessing", Flags);

    static void Prefix(UnloadedResources __instance)
    {
        if (LoggedBackgroundProcessingField == null)
            return;

        LoggedBackgroundProcessingField.SetValue(__instance, true);
    }
}
