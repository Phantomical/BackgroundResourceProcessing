using UnityEngine;

namespace BackgroundResourceProcessing.UI;

internal partial class DebugUI
{
    private void DrawProfilingTab()
    {
        using var group = new PushVerticalGroup();
        if (GUILayout.Button("Recompute Shadow State"))
            RecomputeShadowState();
        if (GUILayout.Button("Stress Test Shadow State"))
            StressTestShadowState();
    }

    void RecomputeShadowState()
    {
        var vessel = FlightGlobals.ActiveVessel;
        if (vessel is null)
            return;

        var processor = vessel.FindVesselModuleImplementing<BackgroundResourceProcessor>();
        if (processor is null)
            return;

        processor.RecomputeShadowState();
    }

    void StressTestShadowState()
    {
        var vessel = FlightGlobals.ActiveVessel;
        if (vessel is null)
            return;

        var processor = vessel.FindVesselModuleImplementing<BackgroundResourceProcessor>();
        if (processor is null)
            return;

        for (int i = 0; i < 10000; ++i)
            processor.RecomputeShadowState();
    }
}
