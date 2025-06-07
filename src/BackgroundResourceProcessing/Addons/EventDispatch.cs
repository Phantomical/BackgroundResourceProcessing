using BackgroundResourceProcessing.Collections;
using UnityEngine;

namespace BackgroundResourceProcessing.Addons
{
    /// <summary>
    /// This addon takes care of updating resource processor changepoints and
    /// routing certain scoped events to the right vessel.
    /// </summary>
    [KSPAddon(
        KSPAddon.Startup.Flight
            | KSPAddon.Startup.TrackingStation
            | KSPAddon.Startup.SpaceCentre
            | KSPAddon.Startup.PSystemSpawn,
        false
    )]
    internal class EventDispatcher : MonoBehaviour
    {
        // In order to keep frame times from getting too laggy
        const uint MaxChangepointUpdatesPerFrame = 32;

        public static EventDispatcher Instance { get; private set; }

        public PriorityQueue<BackgroundResourceProcessor, double> queue = new();

        /// <summary>
        /// Register a <see cref="BackgroundResourceProcessor"/> to have its
        /// <c>OnChangepoint</c> method called at time <paramref name="signalAt"/>.
        /// </summary>
        /// <param name="module">The module.</param>
        /// <param name="signalAt">The time at which to perform the callback.</param>
        public static void RegisterChangepointCallback(
            BackgroundResourceProcessor module,
            double signalAt
        )
        {
            if (double.IsNaN(signalAt))
            {
                var name = module.Vessel.GetDisplayName();
                LogUtil.Error(
                    $"Changepoint callback for vessel {name} registered with signalAt time of NaN"
                );
                return;
            }

            if (Instance == null)
                return;
            if (signalAt == double.PositiveInfinity)
                return;

            Instance.queue.Enqueue(module, signalAt);
        }

        /// <summary>
        /// Unregister all changepoint callbacks for <paramref name="module"/>.
        /// </summary>
        /// <param name="module"></param>
        public static void UnregisterChangepointCallbacks(BackgroundResourceProcessor module)
        {
            if (Instance == null)
                return;

            Instance.queue.Remove(module, out var _, out var _);
        }

        void Awake()
        {
            if (Instance != null)
                LogUtil.Error(
                    "Instance already initialized to another instance of EventDispatcher in Start"
                );

            Instance = this;
        }

        void Start()
        {
            GameEvents.onVesselSOIChanged.Add(OnVesselSOIChanged);
        }

        void OnDestroy()
        {
            if (ReferenceEquals(Instance, this))
                Instance = null;
            else
                LogUtil.Error("Instance set to another instance of EventDispatcher in OnDestroy");

            GameEvents.onVesselSOIChanged.Remove(OnVesselSOIChanged);
        }

        void FixedUpdate()
        {
            var currentTime = Planetarium.GetUniversalTime();

            for (uint i = 0; i < MaxChangepointUpdatesPerFrame; ++i)
            {
                if (!queue.TryPeek(out var module, out var changepoint))
                    break;
                if (changepoint > currentTime)
                    break;

                queue.Dequeue();
                module.OnChangepoint(changepoint);
            }
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
