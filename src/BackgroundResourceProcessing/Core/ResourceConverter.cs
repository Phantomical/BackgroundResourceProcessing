using System;
using System.Collections.Generic;
using System.Linq;
using BackgroundResourceProcessing.Collections;
using BackgroundResourceProcessing.Utils;
using Smooth.Collections;

namespace BackgroundResourceProcessing.Core
{
    public class ResourceConverter(ConverterBehaviour behaviour)
    {
        public Dictionary<string, DynamicBitSet> push = [];
        public Dictionary<string, DynamicBitSet> pull = [];
        public Dictionary<string, DynamicBitSet> constraint = [];

        public ConverterBehaviour behaviour = behaviour;

        public Dictionary<string, ResourceRatio> inputs = [];
        public Dictionary<string, ResourceRatio> outputs = [];
        public Dictionary<string, ResourceConstraint> required = [];

        /// <summary>
        /// A unique ID used to uniquely identify a converter in tests.
        /// </summary>
        public int id = -1;
        public double nextChangepoint = double.PositiveInfinity;
        public double rate = 0.0;

        public void Refresh(VesselState state)
        {
            var resources = behaviour.GetResources(state);
            nextChangepoint = behaviour.GetNextChangepoint(state);

            inputs.Clear();
            outputs.Clear();
            required.Clear();

            foreach (var input in resources.Inputs)
                inputs.Add(input.ResourceName, input.WithDefaultedFlowMode());
            foreach (var output in resources.Outputs)
                outputs.Add(output.ResourceName, output.WithDefaultedFlowMode());
            foreach (var req in resources.Requirements)
                required.Add(req.ResourceName, req);
        }

        public void Load(ConfigNode node)
        {
            node.TryGetDouble("nextChangepoint", ref nextChangepoint);
            node.TryGetValue("id", ref id);
            node.TryGetValue("rate", ref rate);

            foreach (var inner in node.GetNodes("PUSH_INVENTORIES"))
            {
                string resourceName = null;
                if (!inner.TryGetValue("resourceName", ref resourceName))
                    continue;

                push.Add(resourceName, LoadBitSet(inner));
            }

            foreach (var inner in node.GetNodes("PULL_INVENTORIES"))
            {
                string resourceName = null;
                if (!inner.TryGetValue("resourceName", ref resourceName))
                    continue;

                pull.Add(resourceName, LoadBitSet(inner));
            }

            foreach (var inner in node.GetNodes("CONSTRAINT_INVENTORIES"))
            {
                string resourceName = null;
                if (!inner.TryGetValue("resourceName", ref resourceName))
                    continue;

                constraint.Add(resourceName, LoadBitSet(inner));
            }

            var bNode = node.GetNode("BEHAVIOUR");
            if (bNode != null)
                behaviour = ConverterBehaviour.LoadStatic(bNode);

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
                var set = push.GetOrAdd(id.resourceName, () => new(inventoryIds.Count));
                set.Add(index);
            }

            foreach (var inner in node.GetNodes("PULL_INVENTORY"))
            {
                InventoryId id = default;
                id.Load(inner);

                if (!inventoryIds.TryGetValue(id, out var index))
                    continue;

                var set = pull.GetOrAdd(id.resourceName, () => new(inventoryIds.Count));
                set.Add(index);
            }
        }

        public void Save(ConfigNode node)
        {
            node.AddValue("nextChangepoint", nextChangepoint);
            node.AddValue("id", id);
            node.AddValue("rate", rate);

            foreach (var (resourceName, set) in push)
            {
                var inner = node.AddNode("PUSH_INVENTORIES");
                inner.AddValue("resourceName", resourceName);
                SaveBitSet(inner, set);
            }

            foreach (var (resourceName, set) in pull)
            {
                var inner = node.AddNode("PULL_INVENTORIES");
                inner.AddValue("resourceName", resourceName);
                SaveBitSet(inner, set);
            }

            foreach (var (resourceName, set) in pull)
            {
                var inner = node.AddNode("CONSTRAINT_INVENTORIES");
                inner.AddValue("resourceName", resourceName);
                SaveBitSet(inner, set);
            }

            behaviour.Save(node.AddNode("BEHAVIOUR"));
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
