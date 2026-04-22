using System.Linq;
using SystemHeat;
using UnityEngine;

namespace BackgroundResourceProcessing.Integration.SystemHeat;

/// <summary>
/// Subscribes to <see cref="BackgroundResourceProcessor.onBeforeVesselRecord"/>
/// and ensures that a <see cref="BRPSystemHeatMarker"/> module is present on
/// the vessel's root part before recording begins. This lets
/// <see cref="BackgroundSystemHeatFluxInventory"/> create the flux
/// FakePartResources that heat-producing and heat-consuming adapters
/// reference via Push/Pull.
/// </summary>
[KSPAddon(KSPAddon.Startup.Instantly, true)]
public class Loader : MonoBehaviour
{
    void Awake()
    {
        BackgroundResourceProcessor.onBeforeVesselRecord.Add(OnBeforeVesselRecord);
        DontDestroyOnLoad(this);
    }

    // The subscription on BackgroundResourceProcessor.onBeforeVesselRecord is
    // a static delegate that holds a reference to the old-assembly method
    // across a hot-reload. Drop it on OnHotUnload so OnHotLoad can install
    // the new-assembly delegate in its place.
    void OnHotLoad()
    {
        BackgroundResourceProcessor.onBeforeVesselRecord.Add(OnBeforeVesselRecord);
    }

    void OnHotUnload()
    {
        BackgroundResourceProcessor.onBeforeVesselRecord.Remove(OnBeforeVesselRecord);
    }

    void OnBeforeVesselRecord(BackgroundResourceProcessor brp)
    {
        var vessel = brp.Vessel;
        if (vessel?.rootPart == null)
            return;

        if (!vessel.parts.Any(p => p.Modules.OfType<ModuleSystemHeat>().Any()))
            return;

        if (vessel.rootPart.FindModuleImplementing<BRPSystemHeatMarker>() != null)
            return;

        vessel.rootPart.AddModule(nameof(BRPSystemHeatMarker));
    }
}
