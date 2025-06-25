using System;
using System.Collections.Generic;
using System.Linq;
using BackgroundResourceProcessing.Collections;
using BackgroundResourceProcessing.Utils;
using Smooth.Collections;

namespace BackgroundResourceProcessing.Core
{
    /// <summary>
    /// The concrete resource converter used by the background solver.
    /// </summary>
    ///
    /// <remarks>
    /// This is how the
    /// </remarks>
    public class ResourceConverter(ConverterBehaviour behaviour)
    {
        /// <summary>
        /// Bitsets indicating which inventories this converter pushes
        /// resources to, for each resource.
        /// </summary>
        public Dictionary<string, DynamicBitSet> Push = [];

        /// <summary>
        /// Bitsets indicating which inventories this converter pulls
        /// resources from, for each resource.
        /// </summary>
        public Dictionary<string, DynamicBitSet> Pull = [];

        /// <summary>
        /// Bitsets indicating which inventories this converter uses to
        /// determine whether it is resource-constrained, for each resource.
        /// </summary>
        public Dictionary<string, DynamicBitSet> Constraint = [];

        /// <summary>
        /// The behaviour that indicates how this converter actually behaves.
        /// </summary>
        public ConverterBehaviour Behaviour { get; private set; } = behaviour;

        /// <summary>
        /// The resource inputs returned from the behaviour.
        /// </summary>
        public Dictionary<string, ResourceRatio> inputs = [];

        /// <summary>
        /// The resource outputs returned from the behaviour.
        /// </summary>
        public Dictionary<string, ResourceRatio> outputs = [];

        /// <summary>
        /// The resource requirements returned from the behaviour.
        /// </summary>
        public Dictionary<string, ResourceConstraint> required = [];

        /// <summary>
        /// The time at which the behaviour has said that its behaviour might
        /// change next.
        /// </summary>
        public double nextChangepoint = double.PositiveInfinity;

        /// <summary>
        /// The current rate at which this converter is running. This will
        /// always be a number in the range <c>[0, 1]</c>.
        /// </summary>
        public double rate = 0.0;

        /// <summary>
        /// The total amount of time that this converter has been active,
        /// taking into account the activation rate.
        /// </summary>
        ///
        /// <remarks>
        /// This isn't actually used for anything by background resource
        /// processing. Rather, it is provided for use by other users of the
        /// API.
        /// </remarks>
        public double activeTime = 0.0;

        public bool Refresh(VesselState state)
        {
            var resources = Behaviour.GetResources(state);
            nextChangepoint = Behaviour.GetNextChangepoint(state);

            bool changed = false;

            if (OverwriteRatios(ref inputs, resources.Inputs))
                changed = true;
            if (OverwriteRatios(ref outputs, resources.Outputs))
                changed = true;
            if (OverwriteConstraints(ref required, resources.Requirements))
                changed = true;

            return changed;
        }

        public void Load(ConfigNode node)
        {
            node.TryGetDouble("nextChangepoint", ref nextChangepoint);
            node.TryGetValue("rate", ref rate);

            foreach (var inner in node.GetNodes("PUSH_INVENTORIES"))
            {
                string resourceName = null;
                if (!inner.TryGetValue("resourceName", ref resourceName))
                    continue;

                Push.Add(resourceName, LoadBitSet(inner));
            }

            foreach (var inner in node.GetNodes("PULL_INVENTORIES"))
            {
                string resourceName = null;
                if (!inner.TryGetValue("resourceName", ref resourceName))
                    continue;

                Pull.Add(resourceName, LoadBitSet(inner));
            }

            foreach (var inner in node.GetNodes("CONSTRAINT_INVENTORIES"))
            {
                string resourceName = null;
                if (!inner.TryGetValue("resourceName", ref resourceName))
                    continue;

                Constraint.Add(resourceName, LoadBitSet(inner));
            }

            var bNode = node.GetNode("BEHAVIOUR");
            if (bNode != null)
                Behaviour = ConverterBehaviour.LoadStatic(bNode);

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

        internal void LoadLegacyEdges(ConfigNode node, Dictionary<InventoryId, int> inventoryIds)
        {
            foreach (var inner in node.GetNodes("PUSH_INVENTORY"))
            {
                InventoryId id = default;
                id.Load(inner);

                if (!inventoryIds.TryGetValue(id, out var index))
                    continue;
                var set = Push.GetOrAdd(id.resourceName, () => new(inventoryIds.Count));
                set.Add(index);
            }

            foreach (var inner in node.GetNodes("PULL_INVENTORY"))
            {
                InventoryId id = default;
                id.Load(inner);

                if (!inventoryIds.TryGetValue(id, out var index))
                    continue;

                var set = Pull.GetOrAdd(id.resourceName, () => new(inventoryIds.Count));
                set.Add(index);
            }
        }

        public void Save(ConfigNode node)
        {
            node.AddValue("nextChangepoint", nextChangepoint);
            node.AddValue("rate", rate);

            foreach (var (resourceName, set) in Push)
            {
                var inner = node.AddNode("PUSH_INVENTORIES");
                inner.AddValue("resourceName", resourceName);
                SaveBitSet(inner, set);
            }

            foreach (var (resourceName, set) in Pull)
            {
                var inner = node.AddNode("PULL_INVENTORIES");
                inner.AddValue("resourceName", resourceName);
                SaveBitSet(inner, set);
            }

            foreach (var (resourceName, set) in Pull)
            {
                var inner = node.AddNode("CONSTRAINT_INVENTORIES");
                inner.AddValue("resourceName", resourceName);
                SaveBitSet(inner, set);
            }

            Behaviour.Save(node.AddNode("BEHAVIOUR"));
            ConfigUtil.SaveOutputResources(node, outputs.Select(output => output.Value));
            ConfigUtil.SaveInputResources(node, inputs.Select(input => input.Value));
            ConfigUtil.SaveRequiredResources(node, required.Select(required => required.Value));
        }

        private static DynamicBitSet LoadBitSet(ConfigNode node)
        {
            var indices = node.GetValues("index");
            int max = -1;

            for (int i = 0; i < indices.Length; ++i)
            {
                if (!uint.TryParse(indices[i], out var result))
                    continue;
                max = (int)result;
            }

            var set = new DynamicBitSet(max + 1);
            for (int i = 0; i < indices.Length; ++i)
            {
                if (!uint.TryParse(indices[i], out var result))
                    continue;
                set[(int)result] = true;
            }

            return set;
        }

        private static void SaveBitSet(ConfigNode node, DynamicBitSet set)
        {
            foreach (var index in set)
                node.AddValue("index", index);
        }

        public override string ToString()
        {
            return $"{string.Join(",", inputs.Keys)} => {string.Join(",", outputs.Keys)}";
        }

        private static bool OverwriteRatios(
            ref Dictionary<string, ResourceRatio> ratios,
            List<ResourceRatio> inputs
        )
        {
            var old = ratios;
            ratios = new(inputs.Count);

            foreach (var ratio in inputs)
                ratios.Add(ratio.ResourceName, ratio);

            return old == ratios;
        }

        private static bool OverwriteConstraints(
            ref Dictionary<string, ResourceConstraint> ratios,
            List<ResourceConstraint> inputs
        )
        {
            var old = ratios;
            ratios = new(inputs.Count);

            foreach (var ratio in inputs)
                ratios.Add(ratio.ResourceName, ratio);

            return old == ratios;
        }
    }

    internal static class ResourceRatioExtensions
    {
        public static ResourceRatio WithDefaultedFlowMode(this ResourceRatio res)
        {
            if (res.FlowMode != ResourceFlowMode.NULL)
                return res;

            int resourceId = res.ResourceName.GetHashCode();
            var definition = PartResourceLibrary.Instance.GetDefinition(resourceId);
            if (definition == null)
            {
                LogUtil.Error(
                    $"Resource {res.ResourceName} had no resource definition in PartResourceLibrary."
                );
                res.FlowMode = ResourceFlowMode.ALL_VESSEL_BALANCE;
            }
            else
            {
                res.FlowMode = definition.resourceFlowMode;
            }

            return res;
        }
    }
}
