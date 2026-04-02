using KSP.UI.Screens.DebugToolbar;
using UnityEngine;

namespace BackgroundResourceProcessing.UI.Screens;

/// <summary>
/// Registers BackgroundResourceProcessing into the Alt+F12 debug menu.
/// Runs once at MainMenu, after DebugScreenSpawner has created the debug screen.
/// </summary>
[KSPAddon(KSPAddon.Startup.MainMenu, once: true)]
internal class DebugMenuScreen : MonoBehaviour
{
    void Start()
    {
        if (!DebugUIManager.Initialize())
        {
            Debug.LogError(
                "[BackgroundResourceProcessing] Failed to initialize DebugUIManager, skipping debug menu registration"
            );
            return;
        }

        DebugUIManager.BuildWindowPrefab();

        var mainScreen = MainScreenContent.CreatePrefab();
        AddDebugScreen(null, "BRP", "BRP", mainScreen);

        var currentVesselScreen = CurrentVesselScreenContent.CreatePrefab();
        AddDebugScreen("BRP", "BRP_CurrentVessel", "Current Vessel", currentVesselScreen);

        var allVesselsScreen = AllVesselsScreenContent.CreatePrefab();
        AddDebugScreen("BRP", "BRP_AllVessels", "All Vessels", allVesselsScreen);
    }

    static void AddDebugScreen(string parentName, string name, string text, RectTransform prefab)
    {
        DebugScreenSpawner.Instance.debugScreens.screens.Add(
            new LabelledScreenWrapper()
            {
                parentName = parentName,
                name = name,
                text = text,
                screen = prefab,
            }
        );
    }

    class LabelledScreenWrapper : AddDebugScreens.ScreenWrapper
    {
        public override string ToString() => name;
    }
}
