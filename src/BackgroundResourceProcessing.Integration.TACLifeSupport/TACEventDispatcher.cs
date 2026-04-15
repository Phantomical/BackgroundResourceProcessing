using UnityEngine;

namespace BackgroundResourceProcessing.Integration.TACLifeSupport;

/// <summary>
/// Bridges BRP vessel record/restore events to <see cref="ModuleBackgroundTACLifeSupport"/>.
/// </summary>
[KSPAddon(KSPAddon.Startup.AllGameScenes, false)]
public class TACEventDispatcher : MonoBehaviour
{
    void Awake()
    {
        BackgroundResourceProcessor.onVesselRecord.Add(OnRecord);
        BackgroundResourceProcessor.onVesselRestore.Add(OnRestore);
        BackgroundResourceProcessor.onVesselChangepoint.Add(OnChangepoint);
    }

    void OnDestroy()
    {
        BackgroundResourceProcessor.onVesselRecord.Remove(OnRecord);
        BackgroundResourceProcessor.onVesselRestore.Remove(OnRestore);
        BackgroundResourceProcessor.onVesselChangepoint.Remove(OnChangepoint);
    }

    void OnRecord(BackgroundResourceProcessor processor)
    {
        processor
            .Vessel.FindVesselModuleImplementing<ModuleBackgroundTACLifeSupport>()
            ?.OnRecord(processor);
    }

    void OnRestore(BackgroundResourceProcessor processor)
    {
        processor
            .Vessel.FindVesselModuleImplementing<ModuleBackgroundTACLifeSupport>()
            ?.OnRestore(processor);
    }

    void OnChangepoint(BackgroundResourceProcessor processor, ChangepointEvent evt)
    {
        processor
            .Vessel.FindVesselModuleImplementing<ModuleBackgroundTACLifeSupport>()
            ?.OnChangepoint(processor, evt);
    }
}
