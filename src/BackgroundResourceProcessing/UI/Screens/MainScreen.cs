using System.Collections.Generic;
using KSP.UI.Screens.DebugToolbar.Screens;
using TMPro;
using UnityEngine;

namespace BackgroundResourceProcessing.UI.Screens;

internal class MainScreenContent : MonoBehaviour
{
    [SerializeField]
    TextMeshProUGUI _trackedVesselsValue;

    [SerializeField]
    TextMeshProUGUI _totalInventoriesValue;

    [SerializeField]
    TextMeshProUGUI _totalConvertersValue;

    internal static RectTransform CreatePrefab()
    {
        var rt = DebugUIManager.CreateScreenPrefab<MainScreenContent>("BRP_MainScreen");
        var content = rt.GetComponent<MainScreenContent>();
        content.BuildUI(rt);
        return rt;
    }

    void BuildUI(Transform parent)
    {
        // ── Statistics ───────────────────────────────────────────────────
        DebugUIManager.CreateHeader(parent, "Statistics");
        DebugUIManager.CreateSeparator(parent);

        _trackedVesselsValue = DebugUIManager.CreateTableRow(parent, "Tracked Vessels", "N/A");
        _totalInventoriesValue = DebugUIManager.CreateTableRow(parent, "Total Inventories", "N/A");
        _totalConvertersValue = DebugUIManager.CreateTableRow(parent, "Total Converters", "N/A");

        DebugUIManager.CreateSpacer(parent);

        // ── Debug Settings ───────────────────────────────────────────────
        DebugUIManager.CreateHeader(parent, "Debug Settings");
        DebugUIManager.CreateSeparator(parent);

        DebugUIManager.CreateToggle<DebugUIToggle>(parent, "Debug UI");
        DebugUIManager.CreateToggle<DebugLoggingToggle>(parent, "Debug Logging");
        DebugUIManager.CreateToggle<SolverTraceToggle>(parent, "Solver Trace Logging");
        DebugUIManager.CreateToggle<EnableBurstToggle>(parent, "Burst-Accelerated Methods");
        DebugUIManager.CreateToggle<EnableSolutionCacheToggle>(parent, "Solution Cache");
    }

    void OnEnable()
    {
        UpdateStatistics();
    }

    void Update()
    {
        UpdateStatistics();
    }

    void UpdateStatistics()
    {
        if (FlightGlobals.Vessels == null)
        {
            _trackedVesselsValue.text = "N/A";
            _totalInventoriesValue.text = "N/A";
            _totalConvertersValue.text = "N/A";
            return;
        }

        int trackedVessels = 0;
        int totalInventories = 0;
        int totalConverters = 0;

        foreach (var vessel in FlightGlobals.Vessels)
        {
            var processor = vessel.FindVesselModuleImplementing<BackgroundResourceProcessor>();
            if (processor == null)
                continue;

            var invCount = processor.Inventories.Count;
            var convCount = processor.Converters.Count;
            if (invCount == 0 && convCount == 0)
                continue;

            trackedVessels++;
            totalInventories += invCount;
            totalConverters += convCount;
        }

        _trackedVesselsValue.text = trackedVessels.ToString();
        _totalInventoriesValue.text = totalInventories.ToString();
        _totalConvertersValue.text = totalConverters.ToString();
    }
}

internal class DebugUIToggle : DebugScreenToggle
{
    protected override void SetupValues() => SetToggle(DebugSettings.Instance.DebugUI);

    protected override void OnToggleChanged(bool state) => DebugSettings.Instance.DebugUI = state;
}

internal class DebugLoggingToggle : DebugScreenToggle
{
    protected override void SetupValues() => SetToggle(DebugSettings.Instance.DebugLogging);

    protected override void OnToggleChanged(bool state) =>
        DebugSettings.Instance.DebugLogging = state;
}

internal class SolverTraceToggle : DebugScreenToggle
{
    protected override void SetupValues() => SetToggle(DebugSettings.Instance.SolverTrace);

    protected override void OnToggleChanged(bool state) =>
        DebugSettings.Instance.SolverTrace = state;
}

internal class EnableBurstToggle : DebugScreenToggle
{
    protected override void SetupValues() => SetToggle(DebugSettings.Instance.EnableBurst);

    protected override void OnToggleChanged(bool state) =>
        DebugSettings.Instance.EnableBurst = state;
}

internal class EnableSolutionCacheToggle : DebugScreenToggle
{
    protected override void SetupValues() => SetToggle(DebugSettings.Instance.EnableSolutionCache);

    protected override void OnToggleChanged(bool state) =>
        DebugSettings.Instance.EnableSolutionCache = state;
}
