using System.Collections.Generic;
using BackgroundResourceProcessing.Collections;
using BackgroundResourceProcessing.Shadow;
using Unity.Burst;
using UnityEngine;

namespace BackgroundResourceProcessing.Addons;

/// <summary>
/// This addon takes care of calling processor changepoints at the right
/// time.
/// </summary>
///
/// <remarks>
/// <para>
/// This script disables itself if there are no pending changepoints to
/// handle.
/// </para>
///
/// <para>
/// Changepoints that need a new shadow state are not run straight away.
/// Instead, the shadow states for all of them are computed together in a
/// single <see cref="ShadowBatch"/> job, and the changepoints are run on a
/// later <c>FixedUpdate</c> once that job has finished. This means that the
/// main thread never blocks waiting for shadow calculations, at the cost of
/// delaying those changepoints by an update.
/// </para>
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
    private readonly List<PendingBatch> batches = [];
    private bool mustBeEnabled = true;

    /// <summary>
    /// A changepoint that is waiting on its shadow state to be computed.
    /// </summary>
    struct PendingChangepoint
    {
        /// <summary>
        /// The module to call. Set to <c>null</c> if its callbacks were
        /// unregistered while the batch was in flight.
        /// </summary>
        public BackgroundResourceProcessor Module;
        public double Changepoint;
        public int Index;
        public uint ShadowVersion;
    }

    class PendingBatch(ShadowBatch batch)
    {
        public readonly ShadowBatch Batch = batch;
        public readonly List<PendingChangepoint> Changepoints = [];
        public bool Done;
    }

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

        foreach (var pending in batches)
        {
            var changepoints = pending.Changepoints;
            for (int i = 0; i < changepoints.Count; ++i)
            {
                if (!ReferenceEquals(changepoints[i].Module, module))
                    continue;

                var entry = changepoints[i];
                entry.Module = null;
                changepoints[i] = entry;
            }
        }

        if (IsIdle)
            Disable();
    }

    private bool IsIdle => queue.IsEmpty && batches.Count == 0;

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
        _ = BurstCompiler.IsEnabled;
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

        foreach (var pending in batches)
            pending.Batch.Dispose();
        batches.Clear();
    }

    void FixedUpdate()
    {
        var currentTime = Planetarium.GetUniversalTime();

        RunCompletedBatches();

        PendingBatch pending = null;
        for (uint i = 0; i < MaxChangepointUpdatesPerFrame; ++i)
        {
            if (!queue.TryPeek(out var module, out var changepoint))
                break;
            if (changepoint > currentTime)
                break;

            queue.Dequeue();

            if (!module.NeedsShadowStateUpdate(changepoint))
            {
                module.OnChangepoint(changepoint);
                continue;
            }

            pending ??= new(new ShadowBatch(currentTime));

            int index;
            try
            {
                index = pending.Batch.Add(module.Vessel);
            }
            catch (System.Exception e)
            {
                // Don't drop the changepoint. OnChangepoint falls back to
                // computing the shadow state synchronously.
                LogUtil.Error($"Failed to add vessel to shadow batch: {e}");
                module.OnChangepoint(changepoint);
                continue;
            }

            pending.Changepoints.Add(
                new PendingChangepoint
                {
                    Module = module,
                    Changepoint = changepoint,
                    Index = index,
                    ShadowVersion = module.ShadowStateVersion,
                }
            );
        }

        if (pending != null)
        {
            pending.Batch.Schedule();
            batches.Add(pending);
        }

        // If we have no work to do
        if (IsIdle)
            Disable();
    }

    /// <summary>
    /// Run the changepoints for every shadow batch whose job has finished.
    /// Batches that are still running are left for a later update.
    /// </summary>
    private void RunCompletedBatches()
    {
        // OnChangepoint never schedules a new batch, so the indices here stay
        // stable. Batches are only removed once all their changepoints have
        // run so that RemoveCallbacks can still cancel entries in them.
        for (int i = 0; i < batches.Count; ++i)
        {
            if (batches[i].Batch.IsCompleted)
                RunBatch(batches[i]);
        }

        batches.RemoveAll(pending => pending.Done);
    }

    private static void RunBatch(PendingBatch pending)
    {
        var batch = pending.Batch;
        var changepoints = pending.Changepoints;

        try
        {
            batch.Complete();

            // Entries may be cancelled by OnChangepoint calls for earlier
            // entries, so this needs to re-read each entry by index.
            for (int i = 0; i < changepoints.Count; ++i)
            {
                var entry = changepoints[i];
                if (entry.Module == null)
                    continue;

                try
                {
                    entry.Module.ApplyBatchedShadowState(
                        batch.GetResult(entry.Index),
                        entry.ShadowVersion
                    );
                    entry.Module.OnChangepoint(entry.Changepoint);
                }
                catch (System.Exception e)
                {
                    LogUtil.Error($"Changepoint callback threw an exception: {e}");
                }
            }
        }
        finally
        {
            pending.Done = true;
            batch.Dispose();
        }
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
