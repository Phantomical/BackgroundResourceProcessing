using System;
using System.Collections.Generic;
using BackgroundResourceProcessing.Core;

namespace BackgroundResourceProcessing.Integration.MKS;

/// <summary>
/// Identifies one MKS Planetary Logistics pool: a resource on a celestial body.
/// </summary>
internal readonly struct PoolKey(int bodyIndex, string resource) : IEquatable<PoolKey>
{
    public readonly int BodyIndex = bodyIndex;
    public readonly string Resource = resource;

    public bool Equals(PoolKey other) => BodyIndex == other.BodyIndex && Resource == other.Resource;

    public override bool Equals(object obj) => obj is PoolKey other && Equals(other);

    public override int GetHashCode() => (BodyIndex * 397) ^ (Resource?.GetHashCode() ?? 0);
}

/// <summary>
/// Coordinates background contributions to MKS's per-body Planetary Logistics
/// pools across all unloaded colonies.
/// </summary>
///
/// <remarks>
/// <para>
///   Each unloaded colony exposes a synthetic inventory per pooled resource;
///   BRP's solver computes the net rate flowing into/out of it. This
///   coordinator sums those rates, applies MKS's offload tax, and settles the
///   result incrementally into MKS's real <c>StoredQuantity</c>, which is the
///   single shared truth that loaded vessels read directly. Because only
///   incremental deltas are applied, background and active-vessel
///   contributions add without double counting.
/// </para>
/// <para>
///   MKS's own scenario only exists in the flight and space center scenes.
///   Elsewhere (the tracking station) contributions accumulate in
///   <see cref="PoolState.Pending"/> and are flushed once MKS is available
///   again. All of this is persisted, so nothing is lost across scene changes
///   or save/load.
/// </para>
/// <para>
///   When a pool runs dry the colonies drawing from it are marked dirty so
///   they re-seed against the empty pool and throttle their consumers. When a
///   dry pool is refilled the colonies that were starved of it are woken up
///   the same way.
/// </para>
/// </remarks>
[KSPScenario(
    ScenarioCreationOptions.AddToAllGames,
    GameScenes.FLIGHT,
    GameScenes.SPACECENTER,
    GameScenes.TRACKSTATION
)]
public class BRPPlanetaryPoolCoordinator : ScenarioModule
{
    // How often to push accrued background deltas into MKS StoredQuantity so
    // that loaded vessels reading the pool see a current value.
    private const double ReconcileInterval = 1.0;

    public static BRPPlanetaryPoolCoordinator Instance { get; private set; }

    internal sealed class Participant
    {
        // Signed net pool rate, before tax: positive = offloading into the pool.
        public double Rate;

        // MKS's offload tax for this colony's ModulePlanetaryLogistics.
        public double Tax;

        // Whether this colony can draw from the pool at all.
        public bool Pulls;

        // Whether this colony last saw the pool as empty, and so needs to be
        // woken up once it is refilled.
        public bool Starved;
    }

    internal sealed class PoolState
    {
        // The time up to which the participants' rates have been accounted for.
        public double LastUpdate;

        // Accrued change that has not yet been written to MKS.
        public double Pending;

        public double EmptyTime = double.PositiveInfinity;

        public readonly Dictionary<Guid, Participant> Participants = [];
    }

    private readonly Dictionary<PoolKey, PoolState> pools = [];
    private double lastPeriodicReconcile = double.NegativeInfinity;

    private bool needsRescan = true;

    public override void OnAwake()
    {
        base.OnAwake();
        Instance = this;
        GameEvents.onVesselUnloaded.Add(OnVesselUnloaded);
    }

    void OnDestroy()
    {
        GameEvents.onVesselUnloaded.Remove(OnVesselUnloaded);
        if (ReferenceEquals(Instance, this))
            Instance = null;
    }

    // BRP records and solves a vessel while it is still loaded, so the
    // vessel's first rates have to be picked up once it has actually
    // been unloaded.
    private void OnVesselUnloaded(Vessel vessel) => Report(vessel);

    private static void Report(Vessel vessel)
    {
        if (vessel == null || vessel.loaded)
            return;

        var module = vessel.FindVesselModuleImplementing<ModuleBackgroundMKSLogistics>();
        var processor = vessel.FindVesselModuleImplementing<BackgroundResourceProcessor>();
        if (module == null || processor == null)
            return;

        module.Report(processor);
    }

    /// <summary>
    /// Have every unloaded colony re-report its exchange with the pools.
    /// </summary>
    ///
    /// <remarks>
    /// When the game is saved while leaving the flight scene, scenario
    /// modules are saved before BRP records the vessels being unloaded, so
    /// those are missing from our saved state. No game time passes between
    /// saving and loading, so picking them up again here loses nothing.
    /// </remarks>
    private static void RescanVessels()
    {
        if (FlightGlobals.fetch == null)
            return;

        foreach (var vessel in FlightGlobals.Vessels)
            Report(vessel);
    }

    private static double Now => Planetarium.GetUniversalTime();

    /// <summary>
    /// The current balance of a pool, reconciled to the present time.
    /// </summary>
    public double Balance(int bodyIndex, string resource)
    {
        var key = new PoolKey(bodyIndex, resource);
        double pending = 0.0;
        if (pools.TryGetValue(key, out var pool))
        {
            Reconcile(key, pool, Now);
            pending = pool.Pending;
        }

        return Math.Max(MksBridge.GetStored(bodyIndex, resource) + pending, 0.0);
    }

    /// <summary>
    /// Record (or update) a background vessel's exchange with a pool.
    /// </summary>
    ///
    /// <param name="vesselId">The vessel exchanging with the pool.</param>
    /// <param name="bodyIndex">The body that the pool belongs to.</param>
    /// <param name="resource">The resource held in the pool.</param>
    /// <param name="rate">The net rate into the pool, before tax.</param>
    /// <param name="tax">The offload tax applied to positive rates.</param>
    /// <param name="pulls">Whether the vessel can draw from the pool.</param>
    /// <param name="sawEmpty">Whether the vessel last saw the pool as empty.</param>
    public void SetVesselRate(
        Guid vesselId,
        int bodyIndex,
        string resource,
        double rate,
        double tax,
        bool pulls,
        bool sawEmpty
    )
    {
        var key = new PoolKey(bodyIndex, resource);
        var pool = GetOrCreate(key);
        Reconcile(key, pool, Now);

        if (!pool.Participants.TryGetValue(vesselId, out var participant))
            pool.Participants[vesselId] = participant = new();

        participant.Rate = Math.Abs(rate) < 1e-12 ? 0.0 : rate;
        participant.Tax = tax;
        participant.Pulls = pulls;
        participant.Starved = pulls && sawEmpty;

        RecomputeEmptyTime(key, pool);
    }

    /// <summary>
    /// Remove a vessel from every pool it was contributing to (e.g. when it is
    /// loaded and MKS takes over, or when it is destroyed).
    /// </summary>
    public void RemoveVessel(Guid vesselId)
    {
        var now = Now;
        foreach (var kvp in pools)
        {
            if (!kvp.Value.Participants.ContainsKey(vesselId))
                continue;

            Reconcile(kvp.Key, kvp.Value, now);
            kvp.Value.Participants.Remove(vesselId);
            RecomputeEmptyTime(kvp.Key, kvp.Value);
        }
    }

    private PoolState GetOrCreate(PoolKey key)
    {
        if (!pools.TryGetValue(key, out var pool))
        {
            pool = new PoolState { LastUpdate = Now };
            pools[key] = pool;
        }
        return pool;
    }

    /// <summary>
    /// Accrue the background contributions since the last update and, if MKS
    /// is available, commit them to its <c>StoredQuantity</c>. The stored
    /// value is read fresh so that changes made by loaded vessels (via MKS
    /// itself) are preserved.
    /// </summary>
    private static void Reconcile(PoolKey key, PoolState pool, double now, bool flush = true)
    {
        double dt = now - pool.LastUpdate;
        if (dt > 0)
        {
            pool.LastUpdate = now;
            pool.Pending += TaxedAggregate(pool) * dt;
        }

        if (!flush)
            return;

        if (pool.Pending != 0.0 && MksBridge.AddStored(key.BodyIndex, key.Resource, pool.Pending))
            pool.Pending = 0.0;
    }

    /// <summary>
    /// The net rate the pool balance changes at, with MKS's offload tax
    /// applied to the portion being pushed in.
    /// </summary>
    private static double TaxedAggregate(PoolState pool)
    {
        double sum = 0;
        foreach (var p in pool.Participants.Values)
            sum += p.Rate > 0 ? p.Rate * (1.0 - p.Tax) : p.Rate;
        return sum;
    }

    private static void RecomputeEmptyTime(PoolKey key, PoolState pool)
    {
        double taxedRate = TaxedAggregate(pool);
        if (taxedRate >= 0)
        {
            pool.EmptyTime = double.PositiveInfinity;
            return;
        }

        double stored = Math.Max(
            MksBridge.GetStored(key.BodyIndex, key.Resource) + pool.Pending,
            0.0
        );
        pool.EmptyTime = pool.LastUpdate + stored / -taxedRate;
    }

    void FixedUpdate()
    {
        if (!HighLogic.LoadedSceneIsGame)
            return;

        if (needsRescan)
        {
            needsRescan = false;
            RescanVessels();
        }

        var now = Now;
        bool periodic = now - lastPeriodicReconcile >= ReconcileInterval;
        if (periodic)
        {
            lastPeriodicReconcile = now;
            PruneParticipants();
        }

        foreach (var kvp in pools)
        {
            var key = kvp.Key;
            var pool = kvp.Value;

            if (now >= pool.EmptyTime)
            {
                // Flag every drainer of a now-dry pool so it re-seeds and
                // re-solves against the empty pool. Its consumers then
                // throttle instead of over-drawing.
                Reconcile(key, pool, now);
                pool.EmptyTime = double.PositiveInfinity;
                Wake(pool, static p => p.Rate < 0);
            }
            else if (periodic)
            {
                // Keep MKS StoredQuantity fresh for any loaded vessel reading
                // it, and pick up changes those vessels made.
                Reconcile(key, pool, now);
                RecomputeEmptyTime(key, pool);

                if (Balance(key.BodyIndex, key.Resource) > ResourceProcessor.ResourceEpsilon)
                    Wake(pool, static p => p.Starved);
            }
        }
    }

    private static void Wake(PoolState pool, Func<Participant, bool> filter)
    {
        foreach (var kvp in pool.Participants)
        {
            if (!filter(kvp.Value))
                continue;

            var vessel = FlightGlobals.FindVessel(kvp.Key);
            if (vessel == null || vessel.loaded)
                continue;

            // The vessel re-reports its state at the changepoint this causes.
            kvp.Value.Starved = false;
            vessel.FindVesselModuleImplementing<BackgroundResourceProcessor>()?.MarkDirty();
        }
    }

    /// <summary>
    /// Drop participants whose vessel no longer exists (recovered, destroyed,
    /// ...) or that are now loaded and handled by MKS itself.
    /// </summary>
    private void PruneParticipants()
    {
        if (FlightGlobals.fetch == null || FlightGlobals.Vessels.Count == 0)
            return;

        List<Guid> stale = null;
        foreach (var pool in pools.Values)
        {
            foreach (var id in pool.Participants.Keys)
            {
                var vessel = FlightGlobals.FindVessel(id);
                if (vessel == null || vessel.loaded)
                    (stale ??= []).Add(id);
            }
        }

        if (stale == null)
            return;

        foreach (var id in stale)
            RemoveVessel(id);
    }

    public override void OnSave(ConfigNode node)
    {
        base.OnSave(node);

        // Bring everything up to date first, but leave it in Pending. MKS may
        // already have saved its own state for this save, in which case
        // anything we wrote to it now would be lost.
        var now = Now;
        foreach (var kvp in pools)
            Reconcile(kvp.Key, kvp.Value, now, flush: false);

        foreach (var kvp in pools)
        {
            var pool = kvp.Value;
            if (pool.Participants.Count == 0 && pool.Pending == 0.0)
                continue;

            var pnode = node.AddNode("POOL");
            pnode.AddValue("body", kvp.Key.BodyIndex);
            pnode.AddValue("resource", kvp.Key.Resource);
            pnode.AddValue("lastUpdate", pool.LastUpdate);
            pnode.AddValue("pending", pool.Pending);

            foreach (var entry in pool.Participants)
            {
                var vnode = pnode.AddNode("VESSEL");
                vnode.AddValue("id", entry.Key);
                vnode.AddValue("rate", entry.Value.Rate);
                vnode.AddValue("tax", entry.Value.Tax);
                vnode.AddValue("pulls", entry.Value.Pulls);
                vnode.AddValue("starved", entry.Value.Starved);
            }
        }
    }

    public override void OnLoad(ConfigNode node)
    {
        base.OnLoad(node);
        pools.Clear();

        foreach (var pnode in node.GetNodes("POOL"))
        {
            int body = 0;
            string resource = null;
            if (
                !pnode.TryGetValue("body", ref body) || !pnode.TryGetValue("resource", ref resource)
            )
                continue;

            var pool = new PoolState();
            pnode.TryGetValue("lastUpdate", ref pool.LastUpdate);
            pnode.TryGetValue("pending", ref pool.Pending);

            foreach (var vnode in pnode.GetNodes("VESSEL"))
            {
                Guid id = Guid.Empty;
                if (!vnode.TryGetValue("id", ref id))
                    continue;

                var participant = new Participant();
                vnode.TryGetValue("rate", ref participant.Rate);
                vnode.TryGetValue("tax", ref participant.Tax);
                vnode.TryGetValue("pulls", ref participant.Pulls);
                vnode.TryGetValue("starved", ref participant.Starved);
                pool.Participants[id] = participant;
            }

            pools[new PoolKey(body, resource)] = pool;
        }
    }
}
