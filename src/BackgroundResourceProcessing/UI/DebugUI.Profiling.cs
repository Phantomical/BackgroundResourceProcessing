using UnityEngine;

namespace BackgroundResourceProcessing.UI;

internal partial class DebugUI
{
    private void DrawProfilingTab()
    {
        using var group = new PushVerticalGroup();
        if (GUILayout.Button("Recompute Shadow State"))
            RecomputeShadowState();
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
}
