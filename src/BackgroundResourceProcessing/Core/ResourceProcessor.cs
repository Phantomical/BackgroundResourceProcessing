using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BackgroundResourceProcessing.Collections;
using BackgroundResourceProcessing.Modules;
using BackgroundResourceProcessing.Tracing;
using BackgroundResourceProcessing.Utils;
using Smooth.Collections;

namespace BackgroundResourceProcessing.Core
{
    /// <summary>
    /// The actual resource processor.
    /// </summary>
    ///
    /// <remarks>
    /// This specifically does not depend on any KSP types so that it can be
    /// unit tested outside of KSP. Any method that needs access to a KSP
    /// entity needs to have said entity passed in as a parameter.
    /// </remarks>
    public class ResourceProcessor
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

        public ResourceProcessor() { }

        public void Load(ConfigNode node)
        {
            node.TryGetDouble("lastUpdate", ref lastUpdate);
            node.TryGetDouble("nextChangepoint", ref nextChangepoint);

            foreach (var cNode in node.GetNodes("CONVERTER"))
            {
                ResourceConverter converter = new(null);
                converter.Load(cNode);
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
                    inventories[i].rate = soln.inventoryRates[i];
                for (int i = 0; i < converters.Count; ++i)
                    converters[i].rate = soln.converterRates[i];
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

        public void RecordVesselState(Vessel vessel, double currentTime)
        {
            using var span = new TraceSpan("ResourceProcessor.RecordVesselState");

            ClearVesselState();
            var state = new VesselState { Vessel = vessel, CurrentTime = currentTime };
            lastUpdate = currentTime;

            LogUtil.Debug(() => $"Recording vessel state for vessel {vessel.GetDisplayName()}");

            RecordPartResources(state);
            RecordConverters(state);
        }

        private void RecordPartResources(VesselState state)
        {
            using var span = new TraceSpan("ResourceProcessor.RecordPartResources");

            var vessel = state.Vessel;

            foreach (var part in vessel.Parts)
            {
                var partId = part.persistentId;

                LogUtil.Debug(() => $"Inspecting part {part.name} for inventories");

                foreach (var resource in part.Resources)
                {
                    LogUtil.Debug(() =>
                        $"Found inventory with resource {resource.amount}/{resource.maxAmount} {resource.resourceName}"
                    );

                    inventories.Add(new ResourceInventory(resource));
                }
            }
        }

        private void RecordConverters(VesselState state)
        {
            using var span = new TraceSpan("ResourceProcessor.RecordConverters");
            var vessel = state.Vessel;

            List<Part> converterParts = [];
            Dictionary<uint, List<FakePartResource>> seen = [];

            foreach (var part in vessel.Parts)
            {
                LogUtil.Debug(() => $"Inspecting part {part.name} for converters");

                foreach (var partModule in part.Modules)
                {
                    if (partModule is not BackgroundConverterBase module)
                        continue;

                    LogUtil.Debug(() => $"Found converter module: {module.GetType().Name}");

                    ConverterBehaviour behaviour;
                    try
                    {
                        behaviour = module.GetBehaviour();
                    }
                    catch (Exception e)
                    {
                        LogUtil.Error(
                            $"{module.GetType().Name}.GetBehaviour threw an exception: {e}"
                        );
                        continue;
                    }

                    if (behaviour == null)
                    {
                        LogUtil.Debug(() => $"Converter behaviour was null");
                        continue;
                    }

                    behaviour.sourceModule = module.GetType().Name;
                    behaviour.sourcePart = part.name;

                    var index = converters.Count;
                    var converter = new ResourceConverter(behaviour) { id = index };
                    converter.Refresh(state);

                    LogUtil.Debug(() =>
                        $"Converter has {converter.inputs.Count} inputs and {converter.outputs.Count} outputs"
                    );

                    converters.Add(converter);
                    converterParts.Add(part);

                    BackgroundResourceSet linked;
                    try
                    {
                        linked = module.GetLinkedBackgroundResources();
                    }
                    catch (Exception e)
                    {
                        LogUtil.Error(
                            $"{module.GetType().Name}.GetLinkedBackgroundResources threw an exception: {e}"
                        );
                        continue;
                    }

                    if (linked == null)
                        continue;

                    if (linked.pull != null)
                    {
                        foreach (var resource in linked.pull)
                            AddBackgroundPartResource(resource, seen, converter, true);
                    }

                    if (linked.push != null)
                    {
                        foreach (var resource in linked.push)
                            AddBackgroundPartResource(resource, seen, converter, false);
                    }
                }
            }

            ComputeInventoryIds();

            for (int i = 0; i < converters.Count; ++i)
            {
                var converter = converters[i];
                var part = converterParts[i];

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
                        var set = converter.pull.GetOrAdd(
                            resource.resourceName,
                            () => new(inventories.Count)
                        );

                        var id = new InventoryId(resource);
                        var index = inventoryIds[id];
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
                        var set = converter.push.GetOrAdd(
                            resource.resourceName,
                            () => new(inventories.Count)
                        );

                        var id = new InventoryId(resource);
                        var index = inventoryIds[id];
                        set[index] = true;
                    }
                }

                foreach (var req in converter.required.Values)
                {
                    // Treat constraints as if they are pulling resources when
                    // determining which resources they are attached to.
                    var attached = GetConnectedResources(
                        vessel,
                        part,
                        req.ResourceName,
                        req.FlowMode,
                        true
                    );

                    foreach (var resource in attached.set)
                    {
                        var set = converter.constraint.GetOrAdd(
                            resource.resourceName,
                            () => new(inventories.Count)
                        );

                        var id = new InventoryId(resource);
                        var index = inventoryIds[id];
                        set[index] = true;
                    }
                }
            }
        }

        private void ComputeInventoryIds()
        {
            inventoryIds.Clear();
            for (int i = 0; i < inventories.Count; ++i)
                inventoryIds.Add(inventories[i].Id, i);
        }

        private void AddBackgroundPartResource(
            IBackgroundPartResource resource,
            Dictionary<uint, List<FakePartResource>> seen,
            ResourceConverter converter,
            bool pulling
        )
        {
            if (resource is not PartModule module)
            {
                LogUtil.Error(
                    $"IBackgroundPartResource implementer {resource.GetType().Name} is not a PartModule"
                );
                return;
            }

            if (module.part == null)
            {
                LogUtil.Error(
                    $"IBackgroundPartResource implementer {resource.GetType().Name} is not attached to a part"
                );
                return;
            }

            LogUtil.Debug(() =>
                $"Adding fake inventories from module {module.GetType().Name} on part {module.part.name}"
            );

            var moduleId = module.GetPersistentId();

            if (!seen.TryGetValue(moduleId, out List<FakePartResource> resources))
            {
                try
                {
                    resources = [.. resource.GetResources()];
                }
                catch (Exception e)
                {
                    LogUtil.Error(
                        $"{resource.GetType().Name}.GetResources call threw an exception: {e}"
                    );
                    resources = [];
                }

                seen.Add(moduleId, resources);
            }

            var part = module.part;
            foreach (var res in resources)
            {
                if (res.amount < 0.0 || !MathUtil.IsFinite(res.amount))
                {
                    LogUtil.Error(
                        $"Module {module.GetType().Name} on part {part.partName} returned ",
                        $"a FakePartResource with invalid resource amount {res.amount}"
                    );
                    continue;
                }

                // Note we specifically allow +Infinity here.
                if (res.maxAmount < 0.0 || double.IsNaN(res.maxAmount))
                {
                    LogUtil.Error(
                        $"Module {module.GetType().Name} on part {part.partName} returned ",
                        $"a FakePartResource with invalid resource maxAmount {res.maxAmount}"
                    );
                    continue;
                }

                var inventory = new ResourceInventory(res, module);
                if (!inventoryIds.TryGetValue(inventory.Id, out var index))
                {
                    index = inventories.Count;
                    inventories.Add(inventory);
                    inventoryIds.Add(inventory.Id, index);
                }

                var linked = pulling ? converter.pull : converter.push;
                var set = linked.GetOrAdd(res.resourceName, () => new(inventories.Count));

                set.Add(index);
            }
        }

        public void ClearVesselState()
        {
            converters.Clear();
            inventories.Clear();
            inventoryIds.Clear();
            nextChangepoint = double.PositiveInfinity;
        }

        public void UpdateBehaviours(VesselState state)
        {
            var currentTime = state.CurrentTime;

            foreach (var converter in converters)
            {
                if (converter.nextChangepoint > currentTime)
                    continue;

                converter.Refresh(state);
            }
        }

        public void ForceUpdateBehaviours(VesselState state)
        {
            foreach (var converter in converters)
                converter.Refresh(state);
        }

        /// <summary>
        /// Update the amount of resource stored in each resource.
        /// </summary>
        /// <param name="currentTime"></param>
        public void UpdateInventories(double currentTime)
        {
            using var span = new TraceSpan("ResourceProcessor.UpdateInventories");

            var deltaT = currentTime - lastUpdate;
            foreach (var inventory in inventories)
            {
                inventory.amount += inventory.rate * deltaT;

                if (inventory.Full)
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
            }
        }

        /// <summary>
        /// Update all inventories on the vessel to reflect those stored within
        /// this module.
        /// </summary>
        public void ApplyInventories(Vessel vessel)
        {
            Dictionary<uint, Part> parts = [];
            parts.AddAll(
                vessel.Parts.Select(part => DictUtil.CreateKeyValuePair(part.persistentId, part))
            );

            foreach (var inventory in inventories)
            {
                if (!parts.TryGetValue(inventory.partId, out var part))
                    continue;

                if (inventory.moduleId == null)
                {
                    var resource = part.Resources.Get(inventory.resourceName);
                    if (resource == null)
                        continue;

                    var amount = inventory.amount;
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

                    if (module is not IBackgroundPartResource resource)
                        continue;

                    LogUtil.Debug(() =>
                        $"Updating fake inventory on module {module.GetType().Name} of {part.name} to {inventory.amount:g4}/{inventory.maxAmount:g4} {inventory.resourceName}"
                    );

                    try
                    {
                        resource.UpdateStoredAmount(inventory.resourceName, inventory.amount);
                    }
                    catch (Exception e)
                    {
                        LogUtil.Error(
                            $"{module.GetType().Name}.UpdateStoredAmount threw an exception: {e}"
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
            foreach (var converter in converters)
            {
                foreach (var pull in converter.pull.Values.SelectMany(vals => vals))
                {
                    if (pull >= inventories.Count || pull < 0)
                        throw new Exception(
                            $"Converter with id {converter.id} has pull reference to nonexistant inventory {pull}"
                        );
                }

                foreach (var push in converter.pull.Values.SelectMany(vals => vals))
                {
                    if (push >= inventories.Count || push < 0)
                        throw new Exception(
                            $"Converter with id {converter.id} has push reference to nonexistant inventory {push}"
                        );
                }
            }
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
                    if (
                        (resource.flowMode & PartResource.FlowMode.Out)
                        != PartResource.FlowMode.None
                    )
                        output.Add(resource);
                }
                else
                {
                    if (
                        (resource.flowMode & PartResource.FlowMode.In) != PartResource.FlowMode.None
                    )
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
                $"crash-{now.Year}-{now.Month:D2}-{now.Day:D2}-{now.Hour:D2}-{now.Minute:D2}-{now.Second:D2}.cfg";
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
}
