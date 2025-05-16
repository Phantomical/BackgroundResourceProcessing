using BackgroundResourceProcessing.Collections;
using UnityEngine;

namespace BackgroundResourceProcessing.Addons
{
    [KSPAddon(KSPAddon.Startup.FlightAndKSC | KSPAddon.Startup.TrackingStation, false)]
    public class Timer : MonoBehaviour
    {
        // In order to keep frame times from getting too laggy
        const uint MaxChangepointUpdatesPerFrame = 32;

        // A timer wheel to store the list of background processors and their
        // future changepoints.
        //
        // By using a priority queue here we can call FixedUpdate on only the
        // timers that need to have it happen.
        private PriorityQueue<BackgroundResourceProcessor, double> timers = new();

        /// <summary>
        /// Register a <see cref="BackgroundResourceProcessor"/> to have its
        /// <c>OnChangepoint</c> method called at time <paramref name="signalAt"/>.
        /// </summary>
        /// <param name="module">The module.</param>
        /// <param name="signalAt">The time at which to perform the callback.</param>
        internal void RegisterChangepointCallback(
            BackgroundResourceProcessor module,
            double signalAt
        )
        {
            timers.Enqueue(module, signalAt);
        }

        /// <summary>
        /// Unregister all changepoint callbacks for <paramref name="module"/>.
        /// </summary>
        /// <param name="module"></param>
        internal void UnregisterChangepointCallbacks(BackgroundResourceProcessor module)
        {
            timers.Remove(module, out var _, out var _);
        }

        internal void Awake()
        {
            if (Registrar.Timer != null)
            {
                LogUtil.Error(
                    "Multiple instances of BackgroundResourceProcessing are active at the same time"
                );
                return;
            }

            Registrar.Timer = this;
        }

        internal void OnDestroy()
        {
            if (!ReferenceEquals(Registrar.Timer, this))
            {
                LogUtil.Error(
                    "Multiple instances of BackgroundResourceProcessing are active at the same time"
                );
                return;
            }
            Registrar.Timer = null;
        }

        internal void FixedUpdate()
        {
            var currentTime = Planetarium.GetUniversalTime();
            uint count = 0;

            while (timers.TryPeek(out var element, out var changepoint))
            {
                if (changepoint > currentTime)
                    break;
                if (count >= MaxChangepointUpdatesPerFrame)
                    break;

                timers.Dequeue();

                element.OnChangepoint(changepoint);
                count += 1;
            }
        }
    }
}
