using System.Collections.Generic;
using UnityEngine;

namespace BackgroundResourceProcessing.Integration.PersistentThrust;

[KSPAddon(KSPAddon.Startup.AllGameScenes, false)]
public class EventHandler : MonoBehaviour
{
    void Awake()
    {
        BackgroundResourceProcessor.onStateUpdate.Add(OnStateUpdate);
        BackgroundResourceProcessor.onVesselRecord.Add(OnVesselRecord);
        BackgroundResourceProcessor.onVesselChangepoint.Add(OnVesselChangepoint);
    }

    void OnDestroy()
    {
        BackgroundResourceProcessor.onStateUpdate.Remove(OnStateUpdate);
        BackgroundResourceProcessor.onVesselRecord.Remove(OnVesselRecord);
        BackgroundResourceProcessor.onVesselChangepoint.Remove(OnVesselChangepoint);
    }

    void OnStateUpdate(BackgroundResourceProcessor processor, ChangepointEvent evt)
    {
        var dt = evt.CurrentChangepoint - evt.LastChangepoint;
        foreach (var converter in processor.Converters)
        {
            if (converter.Behaviour is not PersistentEngineBehaviour behaviour)
                continue;

            behaviour.ActiveTime += converter.Rate * dt;
        }

        var module = processor.Vessel.FindVesselModuleImplementing<BackgroundPersistentThrust>();
        if (module != null)
            module?.OnStateUpdate(processor, evt);
    }

    void OnVesselChangepoint(BackgroundResourceProcessor processor, ChangepointEvent evt)
    {
        var module = processor.Vessel.FindVesselModuleImplementing<BackgroundPersistentThrust>();
        if (module != null)
            module?.OnVesselChangepoint(processor);
    }

    void OnVesselRecord(BackgroundResourceProcessor processor)
    {
        var vessel = processor.Vessel;
        var module = vessel.FindVesselModuleImplementing<BackgroundPersistentThrust>();

        if (module != null)
            module?.OnVesselRecord(processor);
    }
}
