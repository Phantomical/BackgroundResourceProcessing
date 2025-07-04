using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BackgroundResourceProcessing.Collections;
using BackgroundResourceProcessing.Converter;
using BackgroundResourceProcessing.Inventory;
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

        foreach (var cNode in node.GetNodes("CONVERTER"))
        {
            ResourceConverter converter = new(null);
            converter.Load(cNode, vessel);
            converters.Add(converter);
        }

        foreach (var iNode in node.GetNodes("INVENTORY"))
        {
            ResourceInventory inventory = new();
            inventory.Load(iNode);
            inventoryIds.Add(inventory.Id, inventories.Count);
            inventories.Add(inventory);
        }

        int index = 0;
        foreach (var cNode in node.GetNodes("CONVERTER"))
        {
            var converter = converters[index++];
            converter.LoadLegacyEdges(cNode, inventoryIds);
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
            var solver = new Solver.Solver();
            var soln = solver.ComputeInventoryRates(this);

            foreach (var inventory in inventories)
                inventory.rate = 0.0;
            foreach (var converter in converters)
                converter.rate = 0.0;

            for (int i = 0; i < inventories.Count; ++i)
            {
                var rate = soln.inventoryRates[i];
                if (!MathUtil.IsFinite(rate))
                    throw new Exception($"Rate for inventory {inventories[i].Id} was {rate}");
                inventories[i].rate = rate;
            }

            for (int i = 0; i < converters.Count; ++i)
            {
                double rate = soln.converterRates[i];
                if (!MathUtil.IsFinite(rate))
                    throw new Exception($"Rate for converter {i} was {rate}");
                converters[i].rate = rate;
            }
        }
        catch (Exception e)
        {
            foreach (var inventory in inventories)
                inventory.rate = 0.0;
            foreach (var converter in converters)
                converter.rate = 0.0;

            DumpCrashReport(e);
        }
    }

    public double UpdateNextChangepoint(double currentTime)
    {
        double changepoint = ComputeNextChangepoint(currentTime);
        nextChangepoint = changepoint;
        return changepoint;
    }

    public double ComputeNextChangepoint(double currentTime)
    {
        using var span = new TraceSpan("ResourceProcessor.ComputeNextChangepoint");

        double changepoint = double.PositiveInfinity;
        Dictionary<string, KeyValuePair<double, double>> totals = [];

        foreach (var inv in inventories)
        {
            var (total, tRate) = totals.GetValueOr(inv.resourceName, default);
            total += inv.amount;
            tRate += inv.rate;
            totals[inv.resourceName] = new(total, tRate);

            if (inv.rate == 0.0)
                continue;

            double duration;
            if (inv.rate < 0.0)
                duration = inv.amount / -inv.rate;
            else
                duration = (inv.maxAmount - inv.amount) / inv.rate;

            changepoint = Math.Min(changepoint, currentTime + duration);
        }

        foreach (var converter in converters)
        {
            changepoint = Math.Min(changepoint, converter.nextChangepoint);

            foreach (var requirement in converter.required.Values)
            {
                var (total, rate) = totals.GetValueOr(requirement.ResourceName, default);
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
    public void RecordVesselState(Vessel vessel, double currentTime)
    {
        using var span = new TraceSpan("ResourceProcessor.RecordVesselState");

        ClearVesselState();
        var state = new VesselState(currentTime);
        lastUpdate = currentTime;

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
                    if (res.amount < 0.0 || !MathUtil.IsFinite(res.amount))
                    {
                        LogUtil.Error(
                            $"Inventory {adapter.GetType().Name} on part {part.partName} returned ",
                            $"a FakePartResource with invalid resource amount {res.amount}"
                        );
                        continue;
                    }

                    // Note we specifically allow +Infinity here.
                    if (res.maxAmount < 0.0 || double.IsNaN(res.maxAmount))
                    {
                        LogUtil.Error(
                            $"Inventory {adapter.GetType().Name} on part {part.partName} returned ",
                            $"a FakePartResource with invalid resource maxAmount {res.maxAmount}"
                        );
                        continue;
                    }

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
        try
        {
            behaviours = adapter.GetBehaviour(partModule);
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

            var converter = new ResourceConverter(behaviour);
            converter.Refresh(state);

            LogUtil.Debug(() =>
                $"Converter has {converter.inputs.Count} inputs and {converter.outputs.Count} outputs"
            );

            converters.Add(converter);

            foreach (var ratio in converter.inputs.Values)
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
                    var set = converter.Pull.GetOrAdd(
                        resource.resourceName,
                        () => new(inventories.Count)
                    );

                    var id = new InventoryId(resource);
                    var index = inventoryIds[id];
                    set[index] = true;
                }

                if (behaviours.Pull == null)
                    continue;

                foreach (var module in behaviours.Pull)
                {
                    var id = new InventoryId(module, ratio.ResourceName);
                    if (!inventoryIds.TryGetValue(id, out var index))
                        continue;
                    var set = converter.Pull.GetOrAdd(
                        ratio.ResourceName,
                        () => new(inventories.Count)
                    );
                    set[index] = true;
                }
            }

            foreach (var ratio in converter.outputs.Values)
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
                    var set = converter.Push.GetOrAdd(
                        resource.resourceName,
                        () => new(inventories.Count)
                    );

                    var id = new InventoryId(resource);
                    var index = inventoryIds[id];
                    set[index] = true;
                }

                if (behaviours.Push == null)
                    continue;

                foreach (var module in behaviours.Push)
                {
                    var id = new InventoryId(module, ratio.ResourceName);
                    if (!inventoryIds.TryGetValue(id, out var index))
                        continue;
                    var set = converter.Push.GetOrAdd(
                        ratio.ResourceName,
                        () => new(inventories.Count)
                    );
                    set[index] = true;
                }
            }

            foreach (var req in converter.required.Values)
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
                    var set = converter.Constraint.GetOrAdd(
                        resource.resourceName,
                        () => new(inventories.Count)
                    );

                    var id = new InventoryId(resource);
                    var index = inventoryIds[id];
                    set[index] = true;
                }

                if (behaviours.Constraint == null)
                    continue;

                foreach (var module in behaviours.Constraint)
                {
                    var id = new InventoryId(module, req.ResourceName);
                    if (!inventoryIds.TryGetValue(id, out var index))
                        continue;
                    var set = converter.Constraint.GetOrAdd(
                        req.ResourceName,
                        () => new(inventories.Count)
                    );
                    set[index] = true;
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
    /// <param name="state"></param>
    /// <returns>Whether there has been any actual update to converters</returns>
    public bool UpdateBehaviours(VesselState state)
    {
        var currentTime = state.CurrentTime;
        var changed = false;

        foreach (var converter in converters)
        {
            if (converter.nextChangepoint > currentTime)
                continue;

            if (converter.Refresh(state))
                changed = true;
        }

        return changed;
    }

    public void ForceUpdateBehaviours(VesselState state)
    {
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
        using var span = new TraceSpan("ResourceProcessor.UpdateInventories");

        var changepoint = false;
        var deltaT = currentTime - lastUpdate;
        foreach (var inventory in inventories)
        {
            var oldState = inventory.GetInventoryState();
            var snapshot = updateSnapshots ? inventory.Snapshot : null;

            if (snapshot != null)
            {
                inventory.amount = snapshot.amount;
                inventory.maxAmount = snapshot.maxAmount;
            }

            inventory.amount += inventory.rate * deltaT;

            if (!MathUtil.IsFinite(inventory.amount))
            {
                LogUtil.Error(
                    $"Refusing to update inventory to {inventory.amount:g4}/{inventory.maxAmount:g4} {inventory.resourceName}"
                );
                continue;
            }
            else if (inventory.Full)
                inventory.amount = inventory.maxAmount;
            else if (inventory.Empty)
                inventory.amount = 0.0;
            else if (inventory.RemainingTime < ResourceBoundaryEpsilon)
            {
                // Fudge inventory rates slightly to reduce the number of
                // changepoints that need to be computed.
                //
                // If the inventory is going to fill up/empty in < 0.01s
                // then we can just set it to full/empty right here.
                if (inventory.rate < 0.0)
                    inventory.amount = 0.0;
                else
                    inventory.amount = inventory.maxAmount;
            }

            if (snapshot != null)
            {
                snapshot.amount = inventory.amount;
                inventory.originalAmount = inventory.amount;
            }

            if (oldState != inventory.GetInventoryState())
                changepoint = true;
        }

        foreach (var converter in converters)
            converter.activeTime += deltaT * converter.rate;

        lastUpdate = currentTime;
        return changepoint;
    }

    public void RecordProtoInventories(Vessel vessel)
    {
        if (!snapshotsDirty)
            return;

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
        }

        snapshotsDirty = false;
    }

    /// <summary>
    /// Update all inventories on the vessel to reflect those stored within
    /// this module.
    /// </summary>
    public void ApplyInventories(Vessel vessel)
    {
        Dictionary<uint, Part> parts = [];
        parts.AddAll(vessel.Parts.Select(part => DictUtil.CreateKeyValuePair(part.flightID, part)));

        foreach (var inventory in inventories)
        {
            if (!parts.TryGetValue(inventory.flightId, out var part))
                continue;

            if (inventory.moduleId == null)
            {
                var resource = part.Resources.Get(inventory.resourceName);
                if (resource == null)
                    continue;

                var amount = inventory.amount;
                if (!MathUtil.IsFinite(amount))
                {
                    LogUtil.Error(
                        $"Refusing to update inventory on {part.name} to {amount:g4}/{resource.maxAmount:g4} {resource.resourceName}"
                    );
                    continue;
                }

                if (!MathUtil.ApproxEqual(inventory.originalAmount, resource.amount))
                {
                    var difference = resource.amount - inventory.originalAmount;
                    amount += difference;
                }

                LogUtil.Debug(() =>
                    $"Updating inventory on {part.name} to {amount:g4}/{resource.maxAmount:g4} {resource.resourceName}"
                );

                resource.amount = MathUtil.Clamp(amount, 0.0, resource.maxAmount);
            }
            else
            {
                var module = part.Modules[(uint)inventory.moduleId];
                if (module == null)
                    continue;

                var adapter = BackgroundInventory.GetInventoryForModule(module);
                if (adapter == null)
                    continue;

                LogUtil.Debug(() =>
                    $"Updating fake inventory on module {module.GetType().Name} of {part.name} to {inventory.amount:g4}/{inventory.maxAmount:g4} {inventory.resourceName}"
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
    public Dictionary<string, InventoryState> GetResourceTotals()
    {
        Dictionary<string, InventoryState> totals = [];

        foreach (var inventory in inventories)
        {
            var state = totals.GetValueOr(inventory.resourceName, default);
            totals[inventory.resourceName] = state.Merge(inventory.State);
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

            foreach (var pull in converter.Pull.Values.SelectMany(vals => vals))
            {
                if (pull >= inventories.Count || pull < 0)
                    throw new Exception(
                        $"Converter with id {i} has pull reference to nonexistant inventory {pull}"
                    );
            }

            foreach (var push in converter.Pull.Values.SelectMany(vals => vals))
            {
                if (push >= inventories.Count || push < 0)
                    throw new Exception(
                        $"Converter with id {i} has push reference to nonexistant inventory {push}"
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
                return BuildResourcePrioritySet(GetFlowingResources(part.Resources, pulling));
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
}
