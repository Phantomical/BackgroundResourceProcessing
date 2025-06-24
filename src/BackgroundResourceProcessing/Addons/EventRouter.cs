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
            // There is no way to specify that an addon should be loaded in all
            // non-editor scenes so we just destroy it on the first frame after
            // loading the editor.
            if (HighLogic.LoadedSceneIsEditor)
                GameObject.Destroy(this);

            GameEvents.onVesselSOIChanged.Add(OnVesselSOIChanged);
        }

        void OnDestroy()
        {
            GameEvents.onVesselSOIChanged.Remove(OnVesselSOIChanged);
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
    }
}
