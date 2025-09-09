using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Reflection;
using BackgroundResourceProcessing.Collections;
using BackgroundResourceProcessing.Converter;
using BackgroundResourceProcessing.Inventory;
using BackgroundResourceProcessing.Solver;
using BackgroundResourceProcessing.Tracing;
using BackgroundResourceProcessing.Utils;
using Smooth.Collections;

namespace BackgroundResourceProcessing.Core;

/// <summary>
/// The actual resource processor.
/// </summary>
///
/// <remarks>
/// This specifically does not depend on any KSP types so that it can be
/// unit tested outside of KSP. Any method that needs access to a KSP
/// entity needs to have said entity passed in as a parameter.
/// </remarks>
internal class ResourceProcessor
{
    /// <summary>
    /// If the remaining time before an inventory fills up/empties out is
    /// less than this amount of time then we just set the inventory to be
    /// full or empty, as appropriate.
    /// </summary>
    ///
    /// <remarks>
    /// The primary use for this is to reduce the number of changepoints
    /// that need to be solved immediately after a vessel is recorded.
    /// Often there will be resources that are just slightly off being
    /// full/empty and being able to batch those together helps speed
    /// things up a little bit.
    /// </remarks>
    private const double ResourceBoundaryEpsilon = 0.01;

    /// <summary>
    /// The epsilon factor used when determine if resource boundary
    /// conditions hold.
    /// </summary>
    public const double ResourceEpsilon = 1e-6;

    private static readonly LRUCache<int, SolverCacheEntry> SolverCache = new(1024);

    public List<ResourceConverter> converters = [];
    public List<ResourceInventory> inventories = [];
    public Dictionary<InventoryId, int> inventoryIds = [];

    public double lastUpdate = 0.0;
    public double nextChangepoint = double.PositiveInfinity;

    /// <summary>
    /// Whether we have assigned <see cref="ProtoPartResourceSnapshot"/>s
    /// to each inventory.
    /// </summary>
    private bool snapshotsDirty = true;

    public ResourceProcessor() { }

    #region Serialization
    public void Load(ConfigNode node, Vessel vessel = null)
    {
        node.TryGetDouble("lastUpdate", ref lastUpdate);
        node.TryGetDouble("nextChangepoint", ref nextChangepoint);

        foreach (var iNode in node.GetNodes("INVENTORY"))
        {
            ResourceInventory inventory = new();
            inventory.Load(iNode);
            inventoryIds.Add(inventory.Id, inventories.Count);
            inventories.Add(inventory);
        }

        foreach (var cNode in node.GetNodes("CONVERTER"))
        {
            ResourceConverter converter = new(null);
            converter.Load(cNode, vessel);
            converters.Add(converter);

            converter.LoadLegacyEdges(cNode, inventoryIds);
            converter.ComputeConstraintStates(this);
        }
    }

    public void Save(ConfigNode node)
    {
        node.AddValue("lastUpdate", lastUpdate);
        node.AddValue("nextChangepoint", nextChangepoint);

        foreach (var inventory in inventories)
            inventory.Save(node.AddNode("INVENTORY"));

        foreach (var converter in converters)
            converter.Save(node.AddNode("CONVERTER"));
    }
    #endregion

    public void ComputeRates()
    {
        using var span = new TraceSpan("ResourceProcessor.ComputeRates");

        try
        {
            var soln = ComputeRateSolution();

            foreach (var inventory in inventories)
                inventory.Rate = 0.0;
            foreach (var converter in converters)
                converter.Rate = 0.0;

            for (int i = 0; i < inventories.Count; ++i)
            {
                var rate = soln.inventoryRates[i];
                if (!MathUtil.IsFinite(rate))
                    throw new Exception($"Rate for inventory {inventories[i].Id} was {rate}");
                inventories[i].Rate = rate;
            }

            for (int i = 0; i < converters.Count; ++i)
            {
                double rate = soln.converterRates[i];
                if (!MathUtil.IsFinite(rate))
                    throw new Exception($"Rate for converter {i} was {rate}");
                converters[i].Rate = rate;
            }
        }
        catch (Exception e)
        {
            foreach (var inventory in inventories)
                inventory.Rate = 0.0;
            foreach (var converter in converters)
                converter.Rate = 0.0;

            DumpCrashReport(e);
        }
    }

    public void ClearRates()
    {
        foreach (var inventory in inventories)
            inventory.Rate = 0.0;
        foreach (var converter in converters)
            converter.Rate = 0.0;
    }

    private SolverSolution ComputeRateSolution()
    {
        int hash = 0;
        if (DebugSettings.Instance?.EnableSolutionCache ?? true)
        {
            hash = ComputeSolverCacheHash();
            if (
                SolverCache.TryGetValue(hash, out var entry)
                && entry.Processor.TryGetTarget(out var target)
                && ReferenceEquals(target, this)
                && entry.Solution.converterRates.Length == converters.Count
                && entry.Solution.inventoryRates.Length == inventories.Count
            )
            {
                return entry.Solution;
            }
        }

        var soln = BurstSolver.Solver.ComputeInventoryRates(this);
        // var solver = new Solver.Solver();
        // var soln = solver.ComputeInventoryRates(this);

        if (DebugSettings.Instance?.EnableSolutionCache ?? true)
            SolverCache.Add(hash, new() { Solution = soln, Processor = new(this) });

        return soln;
    }

    private int ComputeSolverCacheHash()
    {
        using var span = new TraceSpan("ResourceProcessor.ComputeSolverCacheHash");

        HashCode hasher = new();

        hasher.Add(inventories.Count, converters.Count);
        var ispan = new TraceSpan("Inventory Hashes");
        int count = inventories.Count;
        for (int i = 0; i < count; ++i)
            inventories[i].SolverHash(ref hasher);
        ispan.Dispose();

        var cspan = new TraceSpan("Converter Hashes");
        count = converters.Count;
        for (int i = 0; i < count; ++i)
            converters[i].SolverHash(ref hasher);
        cspan.Dispose();

        hasher.Add(this);

        return hasher.GetHashCode();
    }

    public double UpdateNextChangepoint(double currentTime)
    {
        double changepoint = ComputeNextChangepoint(currentTime);
        nextChangepoint = changepoint;
        return changepoint;
    }

    private struct ResourceRate
    {
        public double total;
        public double rate;

        public readonly void Deconstruct(out double total, out double rate)
        {
            total = this.total;
            rate = this.rate;
        }
    }

    public double ComputeNextChangepoint(double currentTime)
    {
        using var span = new TraceSpan("ResourceProcessor.ComputeNextChangepoint");

        double changepoint = double.PositiveInfinity;
        LinearMap<int, ResourceRate> totals = new(32);

        foreach (var inv in inventories)
        {
            var entry = totals.GetEntry(inv.ResourceId);
            ref var res = ref entry.GetOrInsert(new());
            res.total += inv.Amount;
            res.rate += inv.Rate;

            if (inv.Rate == 0.0)
                continue;

            double duration;
            if (inv.Rate < 0.0)
                duration = inv.Amount / -inv.Rate;
            else
                duration = (inv.MaxAmount - inv.Amount) / inv.Rate;

            changepoint = Math.Min(changepoint, currentTime + duration);
        }

        foreach (var converter in converters)
        {
            changepoint = Math.Min(changepoint, converter.NextChangepoint);

            foreach (var (resourceId, requirement) in converter.Required)
            {
                var (total, rate) = totals.GetValueOrDefault(resourceId);
                if (rate == 0.0)
                    continue;

                if (MathUtil.ApproxEqual(requirement.Amount, total, ResourceEpsilon))
                    continue;

                var duration = (requirement.Amount - total) / rate;
                if (duration <= 0.0)
                    continue;

                changepoint = Math.Min(changepoint, currentTime + duration);
            }
        }

        return changepoint;
    }

    #region Vessel Recording
    public void RecordVesselState(Vessel vessel, VesselState state)
    {
        using var span = new TraceSpan("ResourceProcessor.RecordVesselState");

        ClearVesselState();
        lastUpdate = state.CurrentTime;

        LogUtil.Debug(() => $"Recording vessel state for vessel {vessel.GetDisplayName()}");

        RecordPartResources(vessel, state);
        RecordConverters(vessel, state);
    }

    private void RecordPartResources(Vessel vessel, VesselState state)
    {
        using var span = new TraceSpan("ResourceProcessor.RecordPartResources");

        foreach (var part in vessel.Parts)
        {
            var flightId = part.flightID;

            LogUtil.Debug(() => $"Inspecting part {part.name} for inventories");

            foreach (var resource in part.Resources)
            {
                LogUtil.Debug(() =>
                    $"Found inventory with resource {resource.amount}/{resource.maxAmount} {resource.resourceName}"
                );

                var inventory = new ResourceInventory(resource);
                inventoryIds.Add(inventory.Id, inventories.Count);
                inventories.Add(inventory);
            }

            foreach (var module in part.Modules)
            {
                var adapter = BackgroundInventory.GetInventoryForModule(module);
                if (adapter == null)
                    continue;

                LogUtil.Debug(() =>
                    $"Found inventory adapter {adapter.GetType().Name} for module {module.GetType().Name}"
                );

                var moduleId = module.GetPersistentId();

                List<FakePartResource> resources;
                try
                {
                    resources = adapter.GetResources(module);
                }
                catch (Exception e)
                {
                    LogUtil.Error(
                        $"{adapter.GetType().Name}.GetResources call threw an exception: {e}"
                    );
                    continue;
                }

                if (resources == null)
                    continue;

                foreach (var res in resources)
                {
                    if (res.Amount < 0.0 || !MathUtil.IsFinite(res.Amount))
                    {
                        LogUtil.Error(
                            $"Inventory {adapter.GetType().Name} on part {part.partName} returned ",
                            $"a FakePartResource with invalid resource amount {res.Amount}"
                        );
                        continue;
                    }

                    // Note we specifically allow +Infinity here.
                    if (res.MaxAmount < 0.0 || double.IsNaN(res.MaxAmount))
                    {
                        LogUtil.Error(
                            $"Inventory {adapter.GetType().Name} on part {part.partName} returned ",
                            $"a FakePartResource with invalid resource maxAmount {res.MaxAmount}"
                        );
                        continue;
                    }

                    LogUtil.Debug(() =>
                        $"Found inventory with {res.Amount}/{res.MaxAmount} {res.ResourceName}"
                    );

                    var inventory = new ResourceInventory(res, module);
                    inventoryIds.Add(inventory.Id, inventories.Count);
                    inventories.Add(inventory);
                }
            }
        }
    }

    private void RecordConverters(Vessel vessel, VesselState state)
    {
        using var span = new TraceSpan("ResourceProcessor.RecordConverters");

        foreach (var part in vessel.Parts)
        {
            LogUtil.Debug(() => $"Inspecting part {part.name} for converters");
            foreach (var partModule in part.Modules)
                RecordPartModuleConverters(partModule, state);
        }
    }

    private void RecordPartModuleConverters(PartModule partModule, VesselState state)
    {
        var part = partModule.part;
        var vessel = part.vessel;

        var adapter = BackgroundConverter.GetConverterForModule(partModule);
        if (adapter == null)
            return;

        LogUtil.Debug(() =>
            $"Found converter adapter {adapter.GetType().Name} for module {partModule.GetType().Name}"
        );

        ModuleBehaviour behaviours;
        int priority = 0;
        try
        {
            behaviours = adapter.GetBehaviour(partModule);
            priority = adapter.GetModulePriority(partModule);
        }
        catch (Exception e)
        {
            LogUtil.Error($"{adapter.GetType().Name}.GetBehaviour threw an exception: {e}");
            return;
        }

        if (behaviours == null || behaviours.Converters == null)
            return;

        foreach (var behaviour in behaviours.Converters)
        {
            behaviour.sourceModule = partModule.GetType().Name;
            behaviour.sourcePart = part.name;
            behaviour.Vessel = vessel;

            var converter = new ResourceConverter(behaviour)
            {
                Priority = behaviour.Priority ?? priority,
                FlightId = part.flightID,
                ModuleId = partModule.GetPersistentId(),
            };
            converter.Refresh(state);

            LogUtil.Debug(() =>
                $"Converter has {converter.Inputs.Count} inputs and {converter.Outputs.Count} outputs"
            );

            converters.Add(converter);

            foreach (var ratio in converter.Inputs.Values)
            {
                var pull = GetConnectedResources(
                    vessel,
                    part,
                    ratio.ResourceName,
                    ratio.FlowMode,
                    true
                );

                foreach (var resource in pull.set)
                {
                    var id = new InventoryId(resource);
                    if (inventoryIds.TryGetValue(id, out var index))
                        converter.Pull[index] = true;
                }

                if (behaviours.Pull == null)
                    continue;

                foreach (var module in behaviours.Pull)
                {
                    var id = new InventoryId(module, ratio.ResourceName);
                    if (!inventoryIds.TryGetValue(id, out var index))
                        continue;
                    converter.Pull[index] = true;
                }
            }

            foreach (var ratio in converter.Outputs.Values)
            {
                var push = GetConnectedResources(
                    vessel,
                    part,
                    ratio.ResourceName,
                    ratio.FlowMode,
                    true
                );

                foreach (var resource in push.set)
                {
                    var id = new InventoryId(resource);
                    if (inventoryIds.TryGetValue(id, out var index))
                        converter.Push[index] = true;
                }

                if (behaviours.Push == null)
                    continue;

                foreach (var module in behaviours.Push)
                {
                    var id = new InventoryId(module, ratio.ResourceName);
                    if (!inventoryIds.TryGetValue(id, out var index))
                        continue;
                    converter.Push[index] = true;
                }
            }

            foreach (var req in converter.Required.Values)
            {
                // Treat constraints as if they are pulling resources when
                // determining which resources they are attached to.
                var attached = GetConnectedResources(
                    part.vessel,
                    part,
                    req.ResourceName,
                    req.FlowMode,
                    true
                );

                foreach (var resource in attached.set)
                {
                    var id = new InventoryId(resource);
                    if (inventoryIds.TryGetValue(id, out var index))
                        converter.Constraint[index] = true;
                }

                if (behaviours.Constraint == null)
                    continue;

                foreach (var module in behaviours.Constraint)
                {
                    var id = new InventoryId(module, req.ResourceName);
                    if (!inventoryIds.TryGetValue(id, out var index))
                        continue;
                    converter.Constraint[index] = true;
                }
            }
        }
    }

    public void ClearVesselState()
    {
        converters.Clear();
        inventories.Clear();
        inventoryIds.Clear();
        nextChangepoint = double.PositiveInfinity;
    }
    #endregion

    /// <summary>
    /// Update behaviours that have indicated their next changepoint has
    /// passed.
    /// </summary>
    /// <returns>Whether there has been any actual update to converters</returns>
    public bool UpdateBehaviours(VesselState state)
    {
        var currentTime = state.CurrentTime;
        var changed = false;

        foreach (var converter in converters)
        {
            if (converter.NextChangepoint > currentTime)
                continue;

            if (converter.Refresh(state))
                changed = true;
        }

        return changed;
    }

    public void ForceUpdateBehaviours(VesselState state)
    {
        using var span = new TraceSpan("ResourceProcessor.ForceUpdateBehaviours");

        foreach (var converter in converters)
            converter.Refresh(state);
    }

    /// <summary>
    /// Update the amount of resource stored in each resource inventory.
    /// </summary>
    ///
    /// <returns><c>true</c> if this state is currently at a changepoint</returns>
    public bool UpdateState(double currentTime, bool updateSnapshots)
    {
        using var span = new TraceSpan("ResourceProcessor.UpdateState");

        var changepoint = false;
        var deltaT = currentTime - lastUpdate;
        Type moduleType = null;

        // No need to update if no time has passed.
        if (deltaT == 0.0)
            return false;

        foreach (var inventory in inventories)
        {
            var oldState = inventory.GetInventoryState();

            if (!updateSnapshots)
            {
                inventory.Amount += inventory.Rate * deltaT;
            }
            else if (inventory.Snapshot != null)
            {
                var snapshot = inventory.Snapshot;

                inventory.Amount = snapshot.amount;
                inventory.MaxAmount = snapshot.maxAmount;
                inventory.Amount += inventory.Rate * deltaT;

                snapshot.UpdateConfigNodeAmounts();
            }
            else if (inventory.ModuleSnapshot != null)
            {
                var module = inventory.ModuleSnapshot;

                if (moduleType?.Name != module.moduleName)
                    moduleType = AssemblyLoader.GetClassByName(
                        typeof(PartModule),
                        module.moduleName
                    );

                SnapshotUpdate snapshot = new()
                {
                    LastUpdate = lastUpdate,
                    CurrentTime = currentTime,
                    Delta = inventory.Rate * deltaT,
                };

                var adapter = BackgroundInventory.GetInventoryForType(moduleType);
                if (adapter == null)
                    inventory.Amount += snapshot.Delta;
                else
                    adapter.UpdateSnapshot(module, inventory, snapshot);
            }
            else
            {
                inventory.Amount += inventory.Rate * deltaT;
            }

            if (!MathUtil.IsFinite(inventory.Amount))
            {
                LogUtil.Error(
                    $"Refusing to update inventory to {inventory.Amount:g4}/{inventory.MaxAmount:g4} {inventory.ResourceName}"
                );
                continue;
            }
            else if (inventory.Full)
                inventory.Amount = inventory.MaxAmount;
            else if (inventory.Empty)
                inventory.Amount = 0.0;
            else if (inventory.RemainingTime < ResourceBoundaryEpsilon)
            {
                // Fudge inventory rates slightly to reduce the number of
                // changepoints that need to be computed.
                //
                // If the inventory is going to fill up/empty in < 0.01s
                // then we can just set it to full/empty right here.
                if (inventory.Rate < 0.0)
                    inventory.Amount = 0.0;
                else
                    inventory.Amount = inventory.MaxAmount;
            }

            if (updateSnapshots && inventory.Snapshot != null)
            {
                var snapshot = inventory.Snapshot;

                snapshot.amount = inventory.Amount;
                inventory.OriginalAmount = inventory.Amount;
            }

            if (oldState != inventory.GetInventoryState())
                changepoint = true;
        }

        foreach (var converter in converters)
            converter.ActiveTime += deltaT * converter.Rate;

        lastUpdate = currentTime;
        return changepoint;
    }

    /// <summary>
    /// Update the recorded constraint states for each converter.
    /// </summary>
    /// <returns>
    ///   <c>true</c> if there has been a change in constraint state for a
    ///   converter.
    /// </returns>
    public bool UpdateConstraintState()
    {
        LinearMap<int, double> totals = new(16);
        bool changed = false;

        foreach (var converter in converters)
        {
            if (converter.Required.Count == 0)
                continue;

            changed |= UpdateConstraintState(converter, totals);
        }

        return changed;
    }

    /// <summary>
    /// Update the recorded constraint state for the provided converter.
    /// </summary>
    /// <param name="converter"></param>
    /// <returns></returns>
    public bool UpdateConstraintState(ResourceConverter converter)
    {
        if (converter.Required.Count == 0)
            return false;

        LinearMap<int, double> totals = new(converter.Required.Count);
        return UpdateConstraintState(converter, totals);
    }

    private bool UpdateConstraintState(ResourceConverter converter, LinearMap<int, double> totals)
    {
        if (converter.Required.Count == 0)
            return false;

        totals.Clear();
        totals.Reserve(converter.Required.Count);

        ConstraintState prev = ConstraintState.ENABLED;
        ConstraintState next = ConstraintState.ENABLED;

        foreach (var inventoryId in converter.Constraint)
        {
            var inventory = inventories[inventoryId];
            if (!converter.Required.ContainsKey(inventory.ResourceId))
                continue;

            var entry = totals.GetEntry(inventory.ResourceId);
            ref var total = ref entry.GetOrInsert(0.0);
            total += inventory.Amount;
        }

        foreach (ref var entry in converter.Required.Entries)
        {
            var resource = entry.Key;
            ref var required = ref entry.Value;
            var total = totals.GetValueOr(resource, 0.0);

            prev = prev.Merge(required.State);

            if (MathUtil.ApproxEqual(required.Amount, total, ResourceEpsilon))
            {
                required.State = ConstraintState.BOUNDARY;
            }
            else
            {
                required.State = ConstraintState.ENABLED;
                switch (required.Constraint)
                {
                    case Constraint.AT_LEAST:
                        if (total < required.Amount)
                            required.State = ConstraintState.DISABLED;
                        break;
                    case Constraint.AT_MOST:
                        if (total > required.Amount)
                            required.State = ConstraintState.DISABLED;
                        break;
                }
            }

            next = next.Merge(required.State);
        }

        return prev != next;
    }

    public void RecordProtoInventories(Vessel vessel)
    {
        if (!snapshotsDirty)
            return;

        Dictionary<uint, List<ResourceInventory>> inventoryByModuleId = [];
        foreach (var inventory in inventories)
        {
            if (inventory.ModuleId == null)
                continue;

            if (!inventoryByModuleId.TryGetValue((uint)inventory.ModuleId, out var list))
                inventoryByModuleId.Add((uint)inventory.ModuleId, list = []);

            list.Add(inventory);
        }

        foreach (var part in vessel.protoVessel.protoPartSnapshots)
        {
            var flightId = part.flightID;

            foreach (var resource in part.resources)
            {
                var id = new InventoryId(flightId, resource.resourceName);
                if (!inventoryIds.TryGetValue(id, out var index))
                    continue;
                var inventory = inventories[index];

                inventory.Snapshot = resource;
            }

            if (inventoryByModuleId.Count == 0)
                continue;

            foreach (var module in part.modules)
            {
                uint moduleId = 0;
                if (!module.moduleValues.TryGetValue("persistentId", ref moduleId))
                    continue;

                if (!inventoryByModuleId.TryGetValue(moduleId, out var inventories))
                    continue;

                foreach (var inventory in inventories)
                    inventory.ModuleSnapshot = module;
            }
        }

        ProtoPartSnapshot current = null;
        uint? currentFlightID = null;

        foreach (var inventory in inventories)
        {
            if (inventory.ModuleId == null)
                continue;

            if (inventory.ModuleId != currentFlightID)
            {
                current = GetProtoPartByFlightId(vessel.protoVessel, inventory.FlightId);
                if (current == null)
                    continue;
                currentFlightID = inventory.FlightId;
            }

            ProtoPartSnapshot part = current;
        }

        snapshotsDirty = false;
    }

    private static ProtoPartSnapshot GetProtoPartByFlightId(ProtoVessel vessel, uint flightID)
    {
        foreach (var part in vessel.protoPartSnapshots)
        {
            if (part.flightID == flightID)
                return part;
        }

        return null;
    }

    /// <summary>
    /// Update all inventories on the vessel to reflect those stored within
    /// this module.
    /// </summary>
    public void ApplyInventories(Vessel vessel)
    {
        if (inventories.Count == 0)
            return;

        Dictionary<uint, Part> parts = [];
        foreach (var part in vessel.Parts)
            parts.TryAddExt(part.flightID, part);

        foreach (var inventory in inventories)
        {
            if (!parts.TryGetValue(inventory.FlightId, out var part))
                continue;

            if (inventory.ModuleId == null)
            {
                var resource = part.Resources.Get(inventory.ResourceName);
                if (resource == null)
                    continue;

                var amount = inventory.Amount;
                if (!MathUtil.IsFinite(amount))
                {
                    LogUtil.Error(
                        $"Refusing to update inventory on {part.name} to {amount:g4}/{resource.maxAmount:g4} {resource.resourceName}"
                    );
                    continue;
                }

                double difference = resource.amount - inventory.OriginalAmount;
                if (!MathUtil.ApproxEqual(difference, 0.0))
                    amount += difference;

                LogUtil.Log(
                    $"Updating inventory on {part.name} to {amount:g4}/{resource.maxAmount:g4} {resource.resourceName}"
                );
                if (!MathUtil.ApproxEqual(difference, 0.0))
                    LogUtil.Log(
                        $"  Stored amount differs by {difference:g4} from saved original amount {inventory.OriginalAmount:g4}"
                    );

                resource.amount = MathUtil.Clamp(amount, 0.0, resource.maxAmount);
            }
            else
            {
                var module = part.Modules[(uint)inventory.ModuleId];
                if (module == null)
                    continue;

                var adapter = BackgroundInventory.GetInventoryForModule(module);
                if (adapter == null)
                    continue;

                LogUtil.Debug(() =>
                    $"Updating fake inventory on module {module.GetType().Name} of {part.name} to {inventory.Amount:g4}/{inventory.MaxAmount:g4} {inventory.ResourceName}"
                );

                try
                {
                    adapter.UpdateResource(module, inventory);
                }
                catch (Exception e)
                {
                    LogUtil.Error(
                        $"{adapter.GetType().Name}.UpdateResource threw an exception: {e}"
                    );
                }
            }
        }
    }

    /// <summary>
    /// Get <see cref="InventoryState"/>s representing total resources
    /// managed by this resource processor.
    /// </summary>
    /// <returns></returns>
    public Dictionary<string, InventoryState> GetResourceStates()
    {
        Dictionary<string, InventoryState> totals = [];

        foreach (var inventory in inventories)
        {
            var state = totals.GetValueOr(inventory.ResourceName, default);
            totals[inventory.ResourceName] = state.Merge(inventory.State);
        }

        return totals;
    }

    /// <summary>
    /// Validate that all push and pull inventories used by converters
    /// actually exist.
    /// </summary>
    ///
    /// <remarks>
    /// This is primarily meant for uses in tests or as a debug check.
    /// </remarks>
    internal void ValidateReferencedInventories()
    {
        for (int i = 0; i < converters.Count; ++i)
        {
            var converter = converters[i];

            foreach (var pull in converter.Pull)
            {
                if (pull >= inventories.Count || pull < 0)
                    throw new Exception(
                        $"Converter with id {i} has pull reference to nonexistant inventory {pull}"
                    );
            }

            foreach (var push in converter.Push)
            {
                if (push >= inventories.Count || push < 0)
                    throw new Exception(
                        $"Converter with id {i} has push reference to nonexistant inventory {push}"
                    );
            }

            foreach (var push in converter.Constraint)
            {
                if (push >= inventories.Count || push < 0)
                    throw new Exception(
                        $"Converter with id {i} has constraint reference to nonexistant inventory {push}"
                    );
            }
        }
    }

    internal ResourceProcessor CloneForSimulator()
    {
        var clone = (ResourceProcessor)MemberwiseClone();
        clone.inventories = [.. inventories.Select(inv => inv.CloneForSimulator())];
        clone.converters = [.. converters.Select(conv => conv.CloneForSimulator())];
        return clone;
    }

    private PartSet.ResourcePrioritySet GetConnectedResources(
        Vessel vessel,
        Part part,
        string resourceName,
        ResourceFlowMode flowMode,
        bool pulling
    )
    {
        int resourceId = resourceName.GetHashCode();

        // This switch roughly mirrors the switch in in Part.GetConnectedResourceTotals
        switch (flowMode)
        {
            case ResourceFlowMode.ALL_VESSEL:
            case ResourceFlowMode.STAGE_PRIORITY_FLOW:
            case ResourceFlowMode.ALL_VESSEL_BALANCE:
            case ResourceFlowMode.STAGE_PRIORITY_FLOW_BALANCE:
                if (part.vessel == null)
                    return new();

                return vessel.resourcePartSet.GetResourceList(resourceId, pulling, false);
            case ResourceFlowMode.STACK_PRIORITY_SEARCH:
            case ResourceFlowMode.STAGE_STACK_FLOW:
            case ResourceFlowMode.STAGE_STACK_FLOW_BALANCE:
                return part.crossfeedPartSet.GetResourceList(resourceId, pulling, false);

            default:
                var resources = GetFlowingResources(part.Resources, pulling);
                resources.RemoveAll(resource => resource.resourceName != resourceName);

                return BuildResourcePrioritySet(resources);
        }
    }

    private List<PartResource> GetFlowingResources(PartResourceList resources, bool pulling)
    {
        List<PartResource> output = [];

        foreach (var resource in resources.dict.Values)
        {
            if (!resource.flowState)
                continue;

            if (pulling)
            {
                if ((resource.flowMode & PartResource.FlowMode.Out) != PartResource.FlowMode.None)
                    output.Add(resource);
            }
            else
            {
                if ((resource.flowMode & PartResource.FlowMode.In) != PartResource.FlowMode.None)
                    output.Add(resource);
            }
        }

        return output;
    }

    private static PartSet.ResourcePrioritySet BuildResourcePrioritySet(
        List<PartResource> resources
    )
    {
        PartSet.ResourcePrioritySet set = new() { lists = [], set = [] };
        set.lists.Add(resources);
        set.set.AddAll(resources);
        return set;
    }

    static bool HasDisplayedSolverCrashError = false;

    private void DumpCrashReport(Exception e)
    {
        // Make sure we at least print the error
        LogUtil.Error($"Solver threw an exception: {e}");

        DumpVessel();
    }

    internal void DumpVessel()
    {
        var pluginDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        var exportDir = Path.Combine(pluginDir, @"..\Crashes");
        var now = DateTime.Now;
        var outputName =
            $"crash-{now.Year}-{now.Month:D2}-{now.Day:D2}-{now.Hour:D2}-{now.Minute:D2}-{now.Second:D2}.cfg.crash";
        var outputPath = Path.Combine(exportDir, outputName);

        Directory.CreateDirectory(exportDir);

        ConfigNode root = new();
        ConfigNode node = root.AddNode("BRP_SHIP");
        Save(node);
        root.Save(outputPath);

        LogUtil.Error(
            $"An error occurred within BackgroundResourceProcessing. Please file ",
            $"a bug with this KSP.log file and the ship state file saved at {outputPath}."
        );

        if (!HasDisplayedSolverCrashError)
        {
            ScreenMessages.PostScreenMessage(
                "An internal error has occurred within BackgroundResourceProcessing. This will not corrupt your save, but please submit a bug report with your KSP.log file."
            );

            // Avoid spamming messages if we end up with a bunch of crashes.
            HasDisplayedSolverCrashError = true;
        }
    }

    struct SolverCacheEntry
    {
        public SolverSolution Solution;
        public WeakReference<ResourceProcessor> Processor;
    }
}
