using System.Collections.Generic;
using System.Linq;
using BackgroundResourceProcessing.Collections;
using BackgroundResourceProcessing.Utils;
using Smooth.Collections;

namespace BackgroundResourceProcessing.Core
{
    public class ResourceConverter(ConverterBehaviour behaviour)
    {
        public Dictionary<string, HashSet<InventoryId>> push = [];
        public Dictionary<string, HashSet<InventoryId>> pull = [];

        public ConverterBehaviour behaviour = behaviour;

        public Dictionary<string, ResourceRatio> inputs = [];
        public Dictionary<string, ResourceRatio> outputs = [];
        public Dictionary<string, ResourceConstraint> required = [];

        /// <summary>
        /// A unique ID used to uniquely identify a converter in tests.
        /// </summary>
        public int id = -1;
        public double nextChangepoint = double.PositiveInfinity;

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

        public void Save(ConfigNode node)
        {
            node.AddValue("nextChangepoint", nextChangepoint);
            node.AddValue("id", id);

            foreach (var entry in push)
            {
                foreach (var id in entry.Value)
                    id.Save(node.AddNode("PUSH_INVENTORY"));
            }

            foreach (var entry in pull)
            {
                foreach (var id in entry.Value)
                    id.Save(node.AddNode("PULL_INVENTORY"));
            }

            behaviour.Save(node.AddNode("BEHAVIOUR"));
            ConfigUtil.SaveOutputResources(node, outputs.Select(output => output.Value));
            ConfigUtil.SaveInputResources(node, inputs.Select(input => input.Value));
            ConfigUtil.SaveRequiredResources(node, required.Select(required => required.Value));
        }

        public void AddPushInventory(InventoryId id)
        {
            if (!push.TryGetValue(id.resourceName, out var list))
            {
                list = [];
                push.Add(id.resourceName, list);
            }

            list.Add(id);
        }

        public void AddPullInventory(InventoryId id)
        {
            if (!pull.TryGetValue(id.resourceName, out var list))
            {
                list = [];
                pull.Add(id.resourceName, list);
            }

            list.Add(id);
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
