using BackgroundResourceProcessing.Utils;
using UnityEngine;

namespace BackgroundResourceProcessing.Addons
{
    /// <summary>
    /// This class takes care of routing certain scoped events to methods on
    /// the right vessel.
    /// </summary>
    [KSPAddon(KSPAddon.Startup.AllGameScenes, false)]
    internal class EventRouter : MonoBehaviour
    {
        void Start()
        {
            GameEvents.onVesselSOIChanged.Add(OnVesselSOIChanged);
            GameEvents.OnGameSettingsApplied.Add(OnSettingsUpdate);
            GameEvents.OnGameDatabaseLoaded.Add(OnGameDatabaseLoaded);

            OnSettingsUpdate();
        }

        void OnDestroy()
        {
            GameEvents.onVesselSOIChanged.Remove(OnVesselSOIChanged);
            GameEvents.OnGameSettingsApplied.Remove(OnSettingsUpdate);
            GameEvents.OnGameDatabaseLoaded.Remove(OnGameDatabaseLoaded);
        }

        private void OnVesselSOIChanged(GameEvents.HostedFromToAction<Vessel, CelestialBody> evt)
        {
            var vessel = evt.host;

            // We don't need to do anything for loaded vessels.
            if (vessel.loaded)
                return;

            var module = vessel.FindVesselModuleImplementing<BackgroundResourceProcessor>();
            if (module == null)
                return;

            module.OnVesselSOIChanged(evt);
        }

        private void OnSettingsUpdate()
        {
            var settings = HighLogic.CurrentGame?.Parameters.CustomParams<DebugSettings>();

            // Safety: We would get a lot of NREs if the settings instance was
            //         ever set to null so just to be careful we do a check here.
            if (settings == null)
                return;

            DebugSettings.Instance = settings;
        }

        private void OnGameDatabaseLoaded()
        {
            TypeRegistry.Reload();
        }
    }
}
