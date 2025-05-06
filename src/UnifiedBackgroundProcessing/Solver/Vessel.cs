using System;
using System.Collections.Generic;
using System.Linq;
using Smooth.Collections;
using UnifiedBackgroundProcessing.Behaviour;
using UnifiedBackgroundProcessing.Modules;

namespace UnifiedBackgroundProcessing.Solver
{
    internal static class DictUtil
    {
        internal static KeyValuePair<K, V> CreateKeyValuePair<K, V>(K key, V value)
        {
            return new KeyValuePair<K, V>(key, value);
        }
    }

    [Serializable]
    public struct InventoryId(uint partId, string resourceName)
    {
        public uint partId = partId;
        public string resourceName = resourceName;

        public InventoryId(PartResource resource)
            : this(resource.part.persistentId, resource.resourceName) { }

        public void Load(ConfigNode node)
        {
            node.TryGetValue("partId", ref partId);
            node.TryGetValue("resourceName", ref resourceName);
        }

        public void Save(ConfigNode node)
        {
            node.AddValue("partId", partId);
            node.AddValue("resourceName", resourceName);
        }
    }

    /// <summary>
    /// A modelled resource inventory within a part.
    /// </summary>
    [Serializable]
    public class ResourceInventory
    {
        /// <summary>
        /// The persistent part id. Used to find the part again when the
        /// vessel goes off the rails.
        /// </summary>
        public uint partId;

        /// <summary>
        /// The name of the resource stored in this inventory.
        /// </summary>
        public string resourceName;

        /// <summary>
        /// How many units of resource are stored in this inventory.
        /// </summary>
        public double amount;

        /// <summary>
        /// The maximum number of units of resource that can be stored in
        /// inventory.
        /// </summary>
        public double maxAmount;

        /// <summary>
        /// The rate at which resources are currently being added or removed
        /// from this inventory.
        /// </summary>
        public double rate = 0.0;

        public bool Full => maxAmount - amount < 1e-6;
        public bool Empty => amount < 1e-6;

        public InventoryId Id => new(partId, resourceName);

        public ResourceInventory(PartResource resource)
        {
            var part = resource.part;

            partId = part.persistentId;
            resourceName = resource.resourceName;
            amount = resource.amount;
            maxAmount = resource.maxAmount;
        }

        public void Save(ConfigNode node)
        {
            node.AddValue("partId", partId);
            node.AddValue("resourceName", resourceName);
            node.AddValue("amount", amount);
            node.AddValue("maxAmount", maxAmount);
        }

        public void Load(ConfigNode node)
        {
            node.TryGetValue("partId", ref partId);
            node.TryGetValue("resourceName", ref resourceName);
            node.TryGetValue("amount", ref amount);
            node.TryGetValue("maxAmount", ref maxAmount);
        }
    }

    /// <summary>
    /// Some common base properties shared between producers, consumers, and
    /// converters. Not meant to be used directly.
    /// </summary>
    public abstract class Base
    {
        public Dictionary<string, HashSet<uint>> push = [];
        public Dictionary<string, HashSet<uint>> pull = [];

        public double nextChangepoint = double.PositiveInfinity;

        public void Load(ConfigNode node)
        {
            node.TryGetValue("nextChangepoint", ref nextChangepoint);

            foreach (var inner in node.GetNodes("PUSH_INVENTORY"))
            {
                InventoryId id = new(0, "");
                id.Load(inner);

                AddPushInventory(id);
            }

            foreach (var inner in node.GetNodes("PULL_INVENTORY"))
            {
                InventoryId id = new(0, "");
                id.Load(inner);

                AddPullInventory(id);
            }
        }

        public void Save(ConfigNode node)
        {
            node.AddValue("nextChangepoint", nextChangepoint);

            foreach (var entry in push)
            {
                var resourceName = entry.Key;
                foreach (var partId in entry.Value)
                {
                    var inner = node.AddNode("PUSH_INVENTORY");
                    new InventoryId(partId, resourceName).Save(inner);
                }
            }

            foreach (var entry in pull)
            {
                var resourceName = entry.Key;
                foreach (var partId in entry.Value)
                {
                    var inner = node.AddNode("PULL_INVENTORY");
                    new InventoryId(partId, resourceName).Save(inner);
                }
            }
        }

        public void AddPushInventory(InventoryId id)
        {
            if (!push.TryGetValue(id.resourceName, out var list))
            {
                list = [];
                push.Add(id.resourceName, list);
            }

            list.Add(id.partId);
        }

        public void AddPullInventory(InventoryId id)
        {
            if (!pull.TryGetValue(id.resourceName, out var list))
            {
                list = [];
                pull.Add(id.resourceName, list);
            }

            list.Add(id.partId);
        }
    }

    public class Producer(ProducerBehaviour behaviour) : Base
    {
        public ProducerBehaviour behaviour = behaviour;

        public Dictionary<string, ResourceRatio> outputs = [];

        public void Refresh(VesselState state)
        {
            var outputList = behaviour.GetOutputs(state);
            nextChangepoint = behaviour.GetNextChangepoint(state);

            outputs.Clear();
            foreach (var output in outputList)
                outputs.Add(output.ResourceName, output);
        }

        public new void Load(ConfigNode node)
        {
            base.Load(node);

            behaviour = (ProducerBehaviour)BaseBehaviour.LoadStatic(node.GetNode("BEHAVIOUR"));

            outputs.Clear();
            outputs.AddAll(
                ConfigUtil
                    .LoadOutputResources(node)
                    .Select(ratio => DictUtil.CreateKeyValuePair(ratio.ResourceName, ratio))
            );
        }

        public new void Save(ConfigNode node)
        {
            base.Save(node);

            behaviour.Save(node.AddNode("BEHAVIOUR"));
            ConfigUtil.SaveOutputResources(node, outputs.Select(output => output.Value));
        }
    }

    public class Consumer(ConsumerBehaviour behaviour) : Base
    {
        public ConsumerBehaviour behaviour = behaviour;

        public Dictionary<string, ResourceRatio> inputs = [];

        public void Refresh(VesselState state)
        {
            var inputList = behaviour.GetInputs(state);
            nextChangepoint = behaviour.GetNextChangepoint(state);

            inputs.Clear();
            foreach (var input in inputList)
                inputs.Add(input.ResourceName, input);
        }

        public new void Load(ConfigNode node)
        {
            base.Load(node);

            behaviour = (ConsumerBehaviour)BaseBehaviour.LoadStatic(node.GetNode("BEHAVIOUR"));

            inputs.Clear();
            inputs.AddAll(
                ConfigUtil
                    .LoadInputResources(node)
                    .Select(ratio => DictUtil.CreateKeyValuePair(ratio.ResourceName, ratio))
            );
        }

        public new void Save(ConfigNode node)
        {
            base.Save(node);

            behaviour.Save(node.AddNode("BEHAVIOUR"));
            ConfigUtil.SaveInputResources(node, inputs.Select(input => input.Value));
        }
    }

    public class Converter(ConverterBehaviour behaviour) : Base
    {
        public ConverterBehaviour behaviour = behaviour;

        public Dictionary<string, ResourceRatio> inputs = [];
        public Dictionary<string, ResourceRatio> outputs = [];
        public Dictionary<string, ResourceRatio> required = [];

        public void Refresh(VesselState state)
        {
            var resources = behaviour.GetResources(state);
            nextChangepoint = behaviour.GetNextChangepoint(state);

            inputs.Clear();
            outputs.Clear();
            required.Clear();

            foreach (var input in resources.Inputs)
                inputs.Add(input.ResourceName, input);
            foreach (var output in resources.Outputs)
                outputs.Add(output.ResourceName, output);
            foreach (var req in resources.Requirements)
                required.Add(req.ResourceName, req);
        }

        public new void Load(ConfigNode node)
        {
            base.Load(node);

            node.AddValue("nextChangepoint", nextChangepoint);
            behaviour.Save(node.AddNode("BEHAVIOUR"));

            outputs.Clear();
            inputs.Clear();
            required.Clear();

            outputs.AddAll(
                ConfigUtil
                    .LoadOutputResources(node)
                    .Select(ratio => DictUtil.CreateKeyValuePair(ratio.ResourceName, ratio))
            );
            inputs.AddAll(
                ConfigUtil
                    .LoadInputResources(node)
                    .Select(ratio => DictUtil.CreateKeyValuePair(ratio.ResourceName, ratio))
            );
            required.AddAll(
                ConfigUtil
                    .LoadRequiredResources(node)
                    .Select(ratio => DictUtil.CreateKeyValuePair(ratio.ResourceName, ratio))
            );
        }

        public new void Save(ConfigNode node)
        {
            base.Save(node);
            behaviour.Save(node.AddNode("BEHAVIOUR"));
            ConfigUtil.SaveOutputResources(node, outputs.Select(output => output.Value));
            ConfigUtil.SaveInputResources(node, inputs.Select(input => input.Value));
            ConfigUtil.SaveRequiredResources(node, required.Select(required => required.Value));
        }
    }

    public class BackgroundProcessorModule : VesselModule
    {
        private List<Producer> producers = [];
        private List<Consumer> consumers = [];
        private List<Converter> converters = [];
        public Dictionary<InventoryId, ResourceInventory> inventories = [];

        private double lastUpdate = 0.0;

        /// <summary>
        /// Whether background processing is actively running on this module.
        /// </summary>
        ///
        /// <remarks>
        /// Generally, this will be true when the vessel is on rails, and false
        /// otherwise.
        /// </remarks>
        public bool BackgroundProcessingActive { get; private set; } = false;

        public override Activation GetActivation()
        {
            return Activation.LoadedOrUnloaded;
        }

        public override int GetOrder()
        {
            return base.GetOrder();
        }

        public override void OnUnloadVessel()
        {
            RecordVesselState();
        }

        public override void OnLoadVessel()
        {
            ApplyInventories();
        }

        internal void OnChangepoint(double changepoint)
        {
            // We do nothing for active vessels.
            if (vessel.loaded)
                return;

            UpdateInventories(changepoint);
        }

        /// <summary>
        /// Find all background processing modules and resources on this vessel
        /// and update the module state accordingly.
        /// </summary>
        private void RecordVesselState()
        {
            producers.Clear();
            consumers.Clear();
            converters.Clear();
            inventories.Clear();

            lastUpdate = Planetarium.GetUniversalTime();

            foreach (var part in vessel.Parts)
            {
                var partId = part.persistentId;
                foreach (var resource in part.Resources)
                {
                    inventories.Add(
                        new InventoryId(partId, resource.resourceName),
                        new ResourceInventory(resource)
                    );
                }
            }

            var state = new VesselState
            {
                Vessel = vessel,
                CurrentTime = Planetarium.GetUniversalTime(),
            };

            foreach (var part in vessel.Parts)
            {
                var producers = part.FindModulesImplementing<BackgroundProducer>();
                var consumers = part.FindModulesImplementing<BackgroundConsumer>();
                var converters = part.FindModulesImplementing<BackgroundConverter>();

                // No point calculating linked inventories if there are no
                // background modules on the current part.
                if (producers.Count == 0 && consumers.Count == 0 && converters.Count == 0)
                    continue;

                foreach (var module in producers)
                {
                    var behaviour = module.GetBehaviour();
                    if (behaviour == null)
                        continue;

                    var producer = new Producer(behaviour);
                    producer.Refresh(state);

                    foreach (var entry in producer.outputs)
                    {
                        var ratio = entry.Value;
                        var resourceSet = GetConnectedResources(
                            part,
                            ratio.ResourceName,
                            ratio.FlowMode,
                            false
                        );

                        foreach (var resource in resourceSet.set)
                            producer.AddPushInventory(new(resource));
                    }

                    this.producers.Add(producer);
                }

                foreach (var module in consumers)
                {
                    var behaviour = module.GetBehaviour();
                    if (behaviour == null)
                        continue;

                    var consumer = new Consumer(behaviour);
                    consumer.Refresh(state);

                    foreach (var entry in consumer.inputs)
                    {
                        var ratio = entry.Value;
                        var resourceSet = GetConnectedResources(
                            part,
                            ratio.ResourceName,
                            ratio.FlowMode,
                            true
                        );

                        foreach (var resource in resourceSet.set)
                            consumer.AddPullInventory(new(resource));
                    }

                    this.consumers.Add(consumer);
                }

                foreach (var module in converters)
                {
                    var behaviour = module.GetBehaviour();
                    if (behaviour == null)
                        continue;

                    var converter = new Converter(behaviour);
                    converter.Refresh(state);

                    foreach (var entry in converter.inputs)
                    {
                        var ratio = entry.Value;
                        var pull = GetConnectedResources(
                            part,
                            ratio.ResourceName,
                            ratio.FlowMode,
                            true
                        );
                        var push = GetConnectedResources(
                            part,
                            ratio.ResourceName,
                            ratio.FlowMode,
                            true
                        );

                        foreach (var resource in pull.set)
                            converter.AddPullInventory(new(resource));

                        foreach (var resource in push.set)
                            converter.AddPushInventory(new(resource));
                    }

                    this.converters.Add(converter);
                }
            }
        }

        /// <summary>
        /// Update the amount of resource stored in each resource.
        /// </summary>
        /// <param name="currentTime"></param>
        private void UpdateInventories(double currentTime)
        {
            var deltaT = currentTime - lastUpdate;

            if (deltaT < 1.0 / 30)
            {
                LogUtil.Warn(
                    $"Background update period for vessel {vessel.name} was less than 32ms"
                );
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
        /// Update the sets of
        /// </summary>
        /// <param name="currentTime"></param>
        private void UpdateBehaviours(double currentTime)
        {
            VesselState state = new() { CurrentTime = currentTime, Vessel = Vessel };

            foreach (var producer in producers)
            {
                if (producer.nextChangepoint > currentTime)
                    continue;

                producer.Refresh(state);
            }

            foreach (var consumer in consumers)
            {
                if (consumer.nextChangepoint > currentTime)
                    continue;

                consumer.Refresh(state);
            }

            foreach (var converter in converters)
            {
                if (converter.nextChangepoint > currentTime)
                    continue;

                converter.Refresh(state);
            }
        }

        private void ForceUpdateBehaviours(double currentTime)
        {
            VesselState state = new() { CurrentTime = currentTime, Vessel = Vessel };

            foreach (var producer in producers)
                producer.Refresh(state);
            foreach (var consumer in consumers)
                consumer.Refresh(state);
            foreach (var converter in converters)
                converter.Refresh(state);
        }

        private void RecalculateRates()
        {
            /**
            What we want to do here is to calculate the rates at which the
            resources in each inventory are changing. That is really the only
            information we care about here.

            This can be represented as a linear programming problem.
            
            To start lets define some variables:
            - let I be the number of inventories,
            - let N be the number of converters/producers/consumers,
            - let R be the number of resources.

            We can lump together converters, producers, and consumers by treating
            producers as converters with no inputs and consumers as
            

            */
        }

        /// <summary>
        /// Update all inventories on the vessel to reflect those stored within
        /// this module.
        /// </summary>
        private void ApplyInventories()
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

        private PartSet.ResourcePrioritySet BuildResourcePrioritySet(List<PartResource> resources)
        {
            PartSet.ResourcePrioritySet set = new();
            set.lists.Add(resources);
            set.set.AddAll(resources);
            return set;
        }

        static double Clamp(double value, double min, double max)
        {
            return Math.Max(Math.Min(value, max), min);
        }
    }
}
