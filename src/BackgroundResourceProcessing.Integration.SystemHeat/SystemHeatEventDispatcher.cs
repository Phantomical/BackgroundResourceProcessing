using System.Linq;
using SystemHeat;
using UnityEngine;

namespace BackgroundResourceProcessing.Integration.SystemHeat;

/// <summary>
/// Listens for the <see cref="BackgroundResourceProcessor.onBeforeVesselRecord"/> event
/// and ensures that a <see cref="BRPSystemHeatMarker"/> module is present on the vessel's
/// root part before recording begins. This allows BackgroundSystemHeatFluxInventory to
/// create the flux FakePartResources that heat-producing and heat-consuming adapters
/// reference via Push/Pull.
/// </summary>
[KSPAddon(KSPAddon.Startup.Flight, false)]
public class SystemHeatEventDispatcher : MonoBehaviour
{
    private void Awake()
    {
        BackgroundResourceProcessor.onBeforeVesselRecord.Add(OnBeforeVesselRecord);
    }

    private void OnDestroy()
    {
        BackgroundResourceProcessor.onBeforeVesselRecord.Remove(OnBeforeVesselRecord);
    }

    private static void OnBeforeVesselRecord(BackgroundResourceProcessor brp)
    {
        var vessel = brp.Vessel;
        if (vessel?.rootPart == null)
            return;

        // Only add the marker if the vessel actually uses SystemHeat modules.
        if (!vessel.parts.Any(p => p.Modules.OfType<ModuleSystemHeat>().Any()))
            return;

        if (vessel.rootPart.FindModuleImplementing<BRPSystemHeatMarker>() != null)
            return;

        vessel.rootPart.AddModule(nameof(BRPSystemHeatMarker));
    }
}
