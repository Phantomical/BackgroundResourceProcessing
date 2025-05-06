using System.Diagnostics;
using System.Reflection;
using UnifiedBackgroundProcessing.Behaviour;
using UnifiedBackgroundProcessing.Collections;
using UnifiedBackgroundProcessing.Solver;
using UnityEngine;

namespace UnifiedBackgroundProcessing
{
    [KSPAddon(KSPAddon.Startup.FlightAndKSC | KSPAddon.Startup.TrackingStation, false)]
    public class UnifiedBackgroundProcessing : MonoBehaviour
    {
        public static UnifiedBackgroundProcessing Instance { get; private set; } = null;

        // In order to keep frame times from getting too laggy
        const uint MaxChangepointUpdatesPerFrame = 32;

        // A timer wheel to store the list of background processors and their
        // future changepoints.
        //
        // By using a priority queue here we can call FixedUpdate on only the
        // timers that need to have it happen.
        private PriorityQueue<BackgroundProcessorModule, double> timers = new();

        /// <summary>
        /// Register a <see cref="BackgroundProcessorModule"/> to have its
        /// <c>OnChangepoint</c> method called at time <paramref name="signalAt"/>.
        /// </summary>
        /// <param name="module">The module.</param>
        /// <param name="signalAt">The time at which to perform the callback.</param>
        internal void RegisterChangepointCallback(BackgroundProcessorModule module, double signalAt)
        {
            timers.Enqueue(module, signalAt);
        }

        /// <summary>
        /// Unregister all changepoint callbacks for <paramref name="module"/>.
        /// </summary>
        /// <param name="module"></param>
        internal void UnregisterChangepointCallbacks(BackgroundProcessorModule module)
        {
            timers.Remove(module, out var _, out var _);
        }

        internal void Awake()
        {
            if (Instance != null)
            {
                LogUtil.Error(
                    "Multiple instances of UnifiedBackgroundProcessing are active at the same time"
                );
            }

            Instance = this;
        }

        internal void OnDestroy()
        {
            if (!ReferenceEquals(Instance, this))
            {
                LogUtil.Error(
                    "Multiple instances of UnifiedBackgroundProcessing are active at the same time"
                );
                return;
            }
            Instance = null;
        }

        internal void FixedUpdate()
        {
            var currentTime = Planetarium.GetUniversalTime();
            uint count = 0;

            while (!timers.TryPeek(out var element, out var changepoint))
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

        #region Behaviour Registration
        /// <summary>
        /// Register all behaviour classes annotated with <c>[<see cref="Behaviour"/>]</c>
        /// within the current assembly.
        /// </summary>
        public static void RegisterAllBehaviours()
        {
            Assembly assembly = new StackTrace().GetFrame(1).GetMethod().ReflectedType.Assembly;
            RegisterAllBehaviours(assembly);
        }

        /// <summary>
        /// Register all behaviour classes annotated with <c>[<see cref="Behaviour"/>]</c>
        /// within the provided assembly.
        /// </summary>
        public static void RegisterAllBehaviours(Assembly assembly)
        {
            BaseBehaviour.RegisterAll(assembly);
        }
        #endregion
    }

    /// <summary>
    /// This addon is responsible for
    /// </summary>
    [KSPAddon(KSPAddon.Startup.Instantly, true)]
    public class UnifiedBackgroundProcessingLoader : MonoBehaviour
    {
        public void OnAwake()
        {
            UnifiedBackgroundProcessing.RegisterAllBehaviours();
        }
    }
}
