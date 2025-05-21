using System;
using System.Collections.Generic;
using System.Linq;
using BackgroundResourceProcessing.Collections;
using BackgroundResourceProcessing.Modules;
using BackgroundResourceProcessing.Solver.V2;
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
        public List<Converter> converters = [];
        public Dictionary<InventoryId, ResourceInventory> inventories = [];

        public double lastUpdate = 0.0;
        public double nextChangepoint = double.PositiveInfinity;

        public ResourceProcessor() { }

        public void Load(ConfigNode node)
        {
            node.TryGetDouble("lastUpdate", ref lastUpdate);
            node.TryGetDouble("nextChangepoint", ref nextChangepoint);

            foreach (var cNode in node.GetNodes("CONVERTER"))
            {
                Converter converter = new(null);
                converter.Load(cNode);
                converters.Add(converter);
            }

            foreach (var iNode in node.GetNodes("INVENTORY"))
            {
                ResourceInventory inventory = new();
                inventory.Load(iNode);
                inventories.Add(inventory.Id, inventory);
            }
        }

        public void Save(ConfigNode node)
        {
            node.AddValue("lastUpdate", lastUpdate);
            node.AddValue("nextChangepoint", nextChangepoint);

            foreach (var converter in converters)
                converter.Save(node.AddNode("CONVERTER"));

            foreach (var inventory in inventories.Values)
                inventory.Save(node.AddNode("INVENTORY"));
        }

        public void ComputeRates()
        {
            try
            {
                var solver = new V2Solver();
                var rates = solver.ComputeInventoryRates(this);

                foreach (var (id, rate) in rates.KSPEnumerate())
                    if (inventories.TryGetValue(id, out var inventory))
                        inventory.rate = rate;
            }
            catch (Exception e)
            {
                LogUtil.Error("Solver threw an exception: ", e);
            }
        }

        public void UpdateNextChangepoint(double currentTime)
        {
            nextChangepoint = ComputeNextChangepoint(currentTime);
        }

        public double ComputeNextChangepoint(double currentTime)
        {
            double changepoint = double.PositiveInfinity;

            foreach (var entry in inventories)
            {
                var inv = entry.Value;

                if (inv.rate == 0.0)
                    continue;

                double duration;
                if (inv.rate < 0.0)
                    duration = inv.amount / -inv.rate;
                else
                    duration = (inv.maxAmount - inv.amount) / inv.amount;

                changepoint = Math.Min(changepoint, currentTime + duration);
            }

            foreach (var converter in converters)
                changepoint = Math.Min(changepoint, converter.nextChangepoint);

            return changepoint;
        }

        public void RecordVesselState(Vessel vessel, double currentTime)
        {
            ClearVesselState();
            var state = new VesselState { Vessel = vessel, CurrentTime = currentTime };
            lastUpdate = currentTime;

            LogUtil.Debug(() => $"Recording vessel state for vessel {vessel.GetDisplayName()}");

            foreach (var part in vessel.Parts)
            {
                var partId = part.persistentId;

                LogUtil.Debug(() => $"Inspecting part {part.name} for inventories");

                foreach (var resource in part.Resources)
                {
                    LogUtil.Debug(() =>
                        $"Found inventory with resource {resource.amount}/{resource.maxAmount} {resource.resourceName}"
                    );
                    inventories.Add(
                        new InventoryId(partId, resource.resourceName),
                        new ResourceInventory(resource)
                    );
                }
            }

            int nextConverterId = 0;
            foreach (var part in vessel.Parts)
            {
                LogUtil.Debug(() => $"Inspecting part {part.name} for converters");

                var converters = part.FindModulesImplementing<IBackgroundConverter>();

                // No point calculating linked inventories if there are no
                // background modules on the current part.
                if (converters.Count == 0)
                    continue;

                foreach (var module in converters)
                {
                    LogUtil.Debug(() => $"Found converter module: {module.GetType().Name}");

                    var behaviour = module.GetBehaviour();
                    if (behaviour == null)
                    {
                        LogUtil.Debug(() => $"Converter behaviour was null");
                        continue;
                    }

                    behaviour.sourceModule = module.GetType().Name;
                    behaviour.sourcePart = part.name;

                    var converter = new Converter(behaviour) { id = nextConverterId++ };
                    converter.Refresh(state);

                    LogUtil.Debug(() =>
                        $"Converter has {converter.inputs.Count} inputs and {converter.outputs.Count} outputs"
                    );

                    foreach (var (_, ratio) in converter.inputs.KSPEnumerate())
                    {
                        var pull = GetConnectedResources(
                            vessel,
                            part,
                            ratio.ResourceName,
                            ratio.FlowMode,
                            true
                        );

                        LogUtil.Debug(() =>
                            $"Found {pull.set.Count} inventories attached to input resource {ratio.ResourceName}"
                        );

                        foreach (var resource in pull.set)
                            converter.AddPullInventory(new(resource));
                    }

                    foreach (var (_, ratio) in converter.outputs.KSPEnumerate())
                    {
                        var push = GetConnectedResources(
                            vessel,
                            part,
                            ratio.ResourceName,
                            ratio.FlowMode,
                            false
                        );

                        LogUtil.Debug(() =>
                            $"Found {push.set.Count} inventories attached to output resource {ratio.ResourceName}"
                        );

                        foreach (var resource in push.set)
                            converter.AddPushInventory(new(resource));
                    }

                    this.converters.Add(converter);
                }
            }
        }

        public void ClearVesselState()
        {
            converters.Clear();
            inventories.Clear();
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
        public void UpdateInventories(string name, double currentTime)
        {
            var deltaT = currentTime - lastUpdate;

            if (deltaT < 1.0 / 30)
            {
                LogUtil.Warn($"Background update period for vessel {name} was less than 32ms");
                return;
            }

            foreach (var entry in inventories)
            {
                var inventory = entry.Value;
                inventory.amount += inventory.rate * deltaT;

                if (inventory.Full)
                    inventory.amount = inventory.maxAmount;
                else if (inventory.Empty)
                    inventory.amount = 0.0;
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

            foreach (var entry in inventories)
            {
                var inventory = entry.Value;
                if (!parts.TryGetValue(inventory.partId, out var part))
                    continue;

                var resource = part.Resources.Get(inventory.resourceName);
                if (resource == null)
                    continue;

                resource.amount = Math.Min(inventory.amount, resource.maxAmount);
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
    }
}
