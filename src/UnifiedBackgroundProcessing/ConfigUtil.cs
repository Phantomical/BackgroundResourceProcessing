using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace UnifiedBackgroundProcessing
{
    internal static class ConfigUtil
    {
        public static IEnumerable<ResourceRatio> LoadResourceRatios(ConfigNode node, string name)
        {
            if (!node.HasNode(name))
                return [];

            return node.GetNodes(name)
                .Where(node =>
                {
                    var present = node.HasValue("ResourceName");
                    if (!present)
                        Debug.Log("Resource must have value 'ResourceName'");
                    return present;
                })
                .Select(node =>
                {
                    ResourceRatio ratio = default;
                    ratio.FlowMode = ResourceFlowMode.NULL;
                    ratio.Load(node);
                    return ratio;
                });
        }

        public static void SaveResourceRatios(
            ConfigNode node,
            string name,
            IEnumerable<ResourceRatio> ratios
        )
        {
            foreach (var ratio in ratios)
            {
                ratio.Save(node.AddNode(name));
            }
        }

        public static IEnumerable<ResourceRatio> LoadOutputResources(ConfigNode node)
        {
            return LoadResourceRatios(node, "OUTPUT_RESOURCE");
        }

        public static IEnumerable<ResourceRatio> LoadInputResources(ConfigNode node)
        {
            return LoadResourceRatios(node, "INPUT_RESOURCE");
        }

        public static IEnumerable<ResourceRatio> LoadRequiredResources(ConfigNode node)
        {
            return LoadResourceRatios(node, "REQUIRED_RESOURCE");
        }

        public static void SaveOutputResources(ConfigNode node, IEnumerable<ResourceRatio> outputs)
        {
            SaveResourceRatios(node, "OUTPUT_RESOURCE", outputs);
        }

        public static void SaveInputResources(ConfigNode node, IEnumerable<ResourceRatio> inputs)
        {
            SaveResourceRatios(node, "INPUT_RESOURCE", inputs);
        }

        public static void SaveRequiredResources(
            ConfigNode node,
            IEnumerable<ResourceRatio> required
        )
        {
            SaveResourceRatios(node, "REQUIRED_RESOURCE", required);
        }
    }
}
