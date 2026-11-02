using System.Collections.Generic;
using BackgroundResourceProcessing.Behaviour;
using BackgroundResourceProcessing.Core;
using PlanetaryLogistics;
using UnityEngine;

namespace BackgroundResourceProcessing.Integration.MKS;

/// <summary>
/// Routes BRP background-processing lifecycle events to the per-vessel MKS
/// planetary-logistics handler.
/// </summary>
[KSPAddon(KSPAddon.Startup.AllGameScenes, false)]
public class MKSEventDispatcher : MonoBehaviour
{
    void Awake()
    {
        BackgroundResourceProcessor.onVesselRecord.Add(OnRecord);
        BackgroundResourceProcessor.onStateUpdate.Add(OnStateUpdate);
        BackgroundResourceProcessor.onVesselChangepoint.Add(OnChangepoint);
        BackgroundResourceProcessor.onVesselRestore.Add(OnRestore);
    }

    void OnDestroy()
    {
        BackgroundResourceProcessor.onVesselRecord.Remove(OnRecord);
        BackgroundResourceProcessor.onStateUpdate.Remove(OnStateUpdate);
        BackgroundResourceProcessor.onVesselChangepoint.Remove(OnChangepoint);
        BackgroundResourceProcessor.onVesselRestore.Remove(OnRestore);
    }

    private static ModuleBackgroundMKSLogistics Module(BackgroundResourceProcessor processor) =>
        processor.Vessel?.FindVesselModuleImplementing<ModuleBackgroundMKSLogistics>();

    private void OnRecord(BackgroundResourceProcessor processor) =>
        Module(processor)?.OnRecord(processor);

    private void OnStateUpdate(BackgroundResourceProcessor processor, ChangepointEvent evt) =>
        Module(processor)?.OnStateUpdate(processor);

    private void OnChangepoint(BackgroundResourceProcessor processor, ChangepointEvent evt) =>
        Module(processor)?.OnChangepoint(processor);

    private void OnRestore(BackgroundResourceProcessor processor) =>
        Module(processor)?.OnRestore(processor);
}

/// <summary>
/// Per-vessel handler that connects an unloaded MKS colony's warehoused
/// resources to the planet-wide Planetary Logistics pool while it is in the
/// background.
/// </summary>
///
/// <remarks>
/// <para>
///   For each pooled resource the vessel gets a synthetic inventory standing
///   in for the pool, plus transfer converters that move resources between it
///   and the vessel's warehouse tanks, gated on the tanks' fill level the same
///   way <see cref="ModulePlanetaryLogistics"/> is.
/// </para>
/// <para>
///   The pool inventory is tagged with <see cref="PoolModuleId"/> so that it
///   can be found again after a save/load, and so that converters added by
///   other integrations with <see cref="AddConverterOptions.LinkToAll"/> (and
///   <see cref="BackgroundResourceProcessor.AddResource"/>) leave it alone.
/// </para>
/// </remarks>
public class ModuleBackgroundMKSLogistics : VesselModule
{
    /// <summary>
    /// The module id used for the synthetic pool inventories. They have a
    /// flight id of 0, which no real part has, so this does not need to be
    /// unique among module ids.
    /// </summary>
    internal const uint PoolModuleId = 0x4D4B5350; // "MKSP"

    private const string TransferSource = "MKS Planetary Logistics";

    /// <summary>
    /// The offload tax of the vessel's <see cref="ModulePlanetaryLogistics"/>,
    /// captured when the vessel was recorded.
    /// </summary>
    [KSPField(isPersistant = true)]
    public double ResourceTax = 0.05;

    public override Activation GetActivation() => Activation.LoadedOrUnloaded;

    public override bool ShouldBeActive() =>
        HighLogic.LoadedScene switch
        {
            GameScenes.FLIGHT or GameScenes.SPACECENTER or GameScenes.TRACKSTATION => true,
            _ => false,
        };

    private static bool IsEnabled()
    {
        var settings = HighLogic.CurrentGame?.Parameters.CustomParams<ModIntegrationSettings>();
        return settings?.EnableMKSPlanetaryLogistics ?? false;
    }

    internal static bool IsPoolInventory(ResourceInventory inventory) =>
        inventory != null && inventory.FlightId == 0 && inventory.ModuleId == PoolModuleId;

    internal void OnRecord(BackgroundResourceProcessor processor)
    {
        // A previous recording may have registered this vessel. Whatever we
        // set up below replaces that.
        BRPPlanetaryPoolCoordinator.Instance?.RemoveVessel(vessel.id);

        if (!IsEnabled())
            return;

        // MKS Planetary Logistics only operates on landed/splashed colonies.
        if (vessel == null || vessel.isEVA || !vessel.LandedOrSplashed)
            return;

        var coordinator = BRPPlanetaryPoolCoordinator.Instance;
        if (coordinator == null)
            return;

        // Warehouses only exchange with the pool when the vessel also has a
        // part with ModulePlanetaryLogistics on it.
        var logistics = vessel.FindPartModuleImplementing<ModulePlanetaryLogistics>();
        if (logistics == null)
            return;

        var warehouses = MksBridge.GetPooledWarehouses(vessel);
        if (warehouses.Count == 0)
            return;

        int body = vessel.mainBody.flightGlobalsIndex;
        bool hasCrew = MksBridge.HasLogisticsCrew(vessel);
        ResourceTax = logistics.ResourceTax;

        foreach (var entry in warehouses)
        {
            var resource = entry.Key;
            var warehouseIds = entry.Value;

            // The real warehouse tank inventories for this resource, and their
            // combined capacity. The pool transfers gate on these tanks' fill.
            var tanks = new List<int>();
            double capacity = 0.0;
            var inventories = processor.Inventories;
            for (int i = 0; i < inventories.Count; ++i)
            {
                var inv = inventories[i];
                if (inv == null || inv.ModuleId != null || inv.ResourceName != resource)
                    continue;
                if (!warehouseIds.Contains(inv.FlightId))
                    continue;

                tanks.Add(i);
                capacity += inv.MaxAmount;
            }

            if (tanks.Count == 0 || capacity <= 0.0)
                continue;

            // The colony's own maximum production/consumption of this resource
            // bounds how fast we ever need to offload surplus / pull deficit.
            double produce = 0.0;
            double consume = 0.0;
            foreach (var converter in processor.Converters)
            {
                foreach (var output in converter.Outputs.Values)
                    if (output.ResourceName == resource)
                        produce += output.Ratio;
                foreach (var input in converter.Inputs.Values)
                    if (input.ResourceName == resource)
                        consume += input.Ratio;
            }

            bool doPush = produce > 0.0;
            // The pull side is gated on a logistics-skilled kerbal, matching MKS.
            // Offloading surplus (push) is always allowed.
            bool doPull = hasCrew && consume > 0.0;
            if (!doPush && !doPull)
                continue;

            // The pool as seen from this vessel: an infinite sink that offloads
            // absorb into and a finite source (seeded to the pool balance) that
            // refills draw from.
            double balance = coordinator.Balance(body, resource);
            int poolIdx = processor.AddInventory(
                new ResourceInventory(resource, balance, double.PositiveInfinity)
                {
                    ModuleId = PoolModuleId,
                }
            );

            if (doPush)
                AddTransfer(
                    processor,
                    resource,
                    produce,
                    tanks,
                    poolIdx,
                    capacity * logistics.UpperTrigger,
                    offload: true
                );
            if (doPull)
                AddTransfer(
                    processor,
                    resource,
                    consume,
                    tanks,
                    poolIdx,
                    capacity * logistics.LowerTrigger,
                    offload: false
                );
        }
    }

    /// <summary>
    /// Add a synthetic converter that transfers a resource between the vessel's
    /// real warehouse tanks and the planetary pool, gated on the tanks' fill so
    /// it reproduces MKS's trigger behaviour.
    /// </summary>
    ///
    /// <param name="offload">
    /// When true, drain surplus from the tanks into the pool while the tanks
    /// hold at least <paramref name="trigger"/>. When false, refill the tanks
    /// from the pool while they hold at most <paramref name="trigger"/>.
    /// </param>
    private static void AddTransfer(
        BackgroundResourceProcessor processor,
        string resource,
        double rate,
        List<int> tanks,
        int poolIdx,
        double trigger,
        bool offload
    )
    {
        // Input and output are the same resource; which side is the pool vs the
        // tanks is decided by the Push/Pull bitsets below, not the recipe.
        var input = new ResourceRatio
        {
            ResourceName = resource,
            Ratio = rate,
            FlowMode = ResourceFlowMode.NULL,
        };
        var output = new ResourceRatio
        {
            ResourceName = resource,
            Ratio = rate,
            FlowMode = ResourceFlowMode.NULL,
        };
        var constraint = new ResourceConstraint
        {
            ResourceName = resource,
            Amount = trigger,
            Constraint = offload ? Constraint.AT_LEAST : Constraint.AT_MOST,
        };

        var behaviour = new ConstantConverter([input], [output], [constraint])
        {
            SourceModule = TransferSource,
        };
        var converter = new Core.ResourceConverter(behaviour);

        if (offload)
        {
            // Pull surplus out of the real tanks, push it into the pool.
            foreach (var tank in tanks)
                converter.Pull.Add(tank);
            converter.Push.Add(poolIdx);
        }
        else
        {
            // Pull the deficit out of the pool, push it into the real tanks.
            converter.Pull.Add(poolIdx);
            foreach (var tank in tanks)
                converter.Push.Add(tank);
        }

        // The fill constraint is evaluated against the real tanks.
        foreach (var tank in tanks)
            converter.Constraint.Add(tank);

        // Connect the inventories before adding the converter so that its
        // initial constraint state is computed against the tanks.
        processor.AddConverter(converter);
    }

    internal void OnStateUpdate(BackgroundResourceProcessor processor)
    {
        // Runs before the solve: re-seed each pool inventory with the pool's
        // authoritative balance so the solver knows how much can be pulled.
        var coordinator = BRPPlanetaryPoolCoordinator.Instance;
        if (coordinator == null || vessel.loaded)
            return;

        int body = vessel.mainBody.flightGlobalsIndex;
        foreach (var inventory in processor.Inventories)
        {
            if (!IsPoolInventory(inventory))
                continue;

            bool wasEmpty = inventory.Empty;
            inventory.Amount = coordinator.Balance(body, inventory.ResourceName);

            // Another colony (or a loaded vessel) emptied or refilled the
            // pool. That changes which transfers can run, which nothing else
            // in this changepoint would notice.
            if (inventory.Empty != wasEmpty)
                processor.MarkDirty();
        }
    }

    internal void OnChangepoint(BackgroundResourceProcessor processor)
    {
        // Runs after the solve: report the solved net pool rate. Loaded
        // vessels are handled by MKS itself.
        if (vessel.loaded)
            return;

        Report(processor);
    }

    /// <summary>
    /// Tell the coordinator everything it needs to know about this vessel's
    /// exchange with each pool, as of the last changepoint.
    /// </summary>
    ///
    /// <remarks>
    /// This is self-contained so that the coordinator can rebuild its state
    /// from the vessels themselves, which it needs to do in a couple of
    /// places. See <see cref="BRPPlanetaryPoolCoordinator"/>.
    /// </remarks>
    internal void Report(BackgroundResourceProcessor processor)
    {
        var coordinator = BRPPlanetaryPoolCoordinator.Instance;
        if (coordinator == null || vessel == null)
            return;

        int body = vessel.mainBody.flightGlobalsIndex;
        var inventories = processor.Inventories;
        for (int i = 0; i < inventories.Count; ++i)
        {
            var inventory = inventories[i];
            if (!IsPoolInventory(inventory))
                continue;

            coordinator.SetVesselRate(
                vessel.id,
                body,
                inventory.ResourceName,
                inventory.Rate,
                ResourceTax,
                pulls: PullsFrom(processor, i),
                sawEmpty: inventory.Empty
            );
        }
    }

    private static bool PullsFrom(BackgroundResourceProcessor processor, int poolIdx)
    {
        foreach (var converter in processor.Converters)
        {
            if (converter?.Behaviour?.SourceModule != TransferSource)
                continue;
            if (converter.Pull[poolIdx])
                return true;
        }

        return false;
    }

    internal void OnRestore(BackgroundResourceProcessor processor)
    {
        // The vessel is becoming active; MKS itself takes over the pool.
        BRPPlanetaryPoolCoordinator.Instance?.RemoveVessel(vessel.id);
    }
}
