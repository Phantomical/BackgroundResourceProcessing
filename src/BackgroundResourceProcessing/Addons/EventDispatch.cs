using System.Collections.Generic;
using BackgroundResourceProcessing.Collections;
using UnityEngine;

namespace BackgroundResourceProcessing.Addons;

/// <summary>
/// This addon takes care of calling processor changepoints at the right
/// time.
/// </summary>
///
/// <remarks>
/// This script disables itself if there are no pending changepoints to
/// handle.
/// </remarks>
[KSPAddon(KSPAddon.Startup.AllGameScenes, false)]
internal class EventDispatcher : MonoBehaviour
{
    // In order to keep frame times from getting too laggy
    const uint MaxChangepointUpdatesPerFrame = 4;

    private static readonly ReferenceEqualityComparer<BackgroundResourceProcessor> comparer = new();

    public static EventDispatcher Instance { get; private set; }

    private readonly PriorityQueue<BackgroundResourceProcessor, double> queue = new();
    private readonly List<BackgroundResourceProcessor> dirty = [];
    private bool mustBeEnabled = true;

    #region Global API
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

        Instance.AddCallback(module, signalAt);
    }

    /// <summary>
    /// Unregister all changepoint callbacks for <paramref name="module"/>.
    /// </summary>
    /// <param name="module"></param>
    public static void UnregisterChangepointCallbacks(BackgroundResourceProcessor module)
    {
        if (Instance == null)
            return;

        Instance.RemoveCallbacks(module);
    }

    /// <summary>
    /// Schedule a callback to OnDirtyLateUpdate to happen during LateUpdate.
    /// </summary>
    /// <param name="module"></param>
    public static void RegisterDirty(BackgroundResourceProcessor module)
    {
        if (Instance == null)
            return;

        Instance.AddDirty(module);
    }
    #endregion

    #region Internal API
    private void AddCallback(BackgroundResourceProcessor module, double signalAt)
    {
        queue.Enqueue(module, signalAt);
        enabled = true;
    }

    private void RemoveCallbacks(BackgroundResourceProcessor module)
    {
        queue.Remove(module, out var _, out var _, comparer);

        if (queue.IsEmpty)
            Disable();
    }

    private void AddDirty(BackgroundResourceProcessor module)
    {
        dirty.Add(module);
    }

    private void Disable()
    {
        if (!mustBeEnabled)
            enabled = false;
    }
    #endregion

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
        GameEvents.onSceneConfirmExit.Add(OnSceneConfirmExit);
        mustBeEnabled = false;
    }

    void OnDestroy()
    {
        if (ReferenceEquals(Instance, this))
            Instance = null;
        else
            LogUtil.Error("Instance set to another instance of EventDispatcher in OnDestroy");

        GameEvents.onSceneConfirmExit.Remove(OnSceneConfirmExit);
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

        // If we have no work to do
        if (queue.IsEmpty)
            Disable();
    }

    void LateUpdate()
    {
        while (dirty.TryPopBack(out var module))
            module.OnDirtyLateUpdate();
    }

    void OnSceneConfirmExit(GameScenes _)
    {
        enabled = true;
        mustBeEnabled = true;
    }

    class ReferenceEqualityComparer<T>() : IEqualityComparer<T>
        where T : class
    {
        public bool Equals(T x, T y)
        {
            return ReferenceEquals(x, y);
        }

        public int GetHashCode(T obj)
        {
            return obj.GetHashCode();
        }
    }
}
