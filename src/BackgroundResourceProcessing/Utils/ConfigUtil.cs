using System.Collections.Generic;
using System.Linq;
using BackgroundResourceProcessing.Utils;
using Steamworks;

namespace BackgroundResourceProcessing;

public static class ConfigUtil
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
                {
                    LogUtil.Error(
                        $"{name} node must have ResourceName field. Resource will be skipped."
                    );
                }
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
            ratio.Save(node.AddNode(name));
    }

    public static IEnumerable<ResourceConstraint> LoadResourceConstraints(
        ConfigNode node,
        string name
    )
    {
        if (!node.HasNode(name))
            return [];

        return node.GetNodes(name)
            .Where(node =>
            {
                var present = node.HasValue("ResourceName");
                if (!present)
                    LogUtil.Error(
                        $"{name} node must have ResourceName field. Resource will be skipped."
                    );
                return present;
            })
            .Select(node =>
            {
                ResourceConstraint constraint = new();
                constraint.Load(node);
                return constraint;
            });
    }

    public static void SaveResourceConstraints(
        ConfigNode node,
        string name,
        IEnumerable<ResourceConstraint> constraints
    )
    {
        foreach (var constraint in constraints)
            constraint.Save(node.AddNode(name));
    }

    public static IEnumerable<ResourceRatio> LoadOutputResources(ConfigNode node)
    {
        return LoadResourceRatios(node, "OUTPUT_RESOURCE");
    }

    public static IEnumerable<ResourceRatio> LoadInputResources(ConfigNode node)
    {
        return LoadResourceRatios(node, "INPUT_RESOURCE");
    }

    public static IEnumerable<ResourceConstraint> LoadRequiredResources(ConfigNode node)
    {
        return LoadResourceConstraints(node, "REQUIRED_RESOURCE");
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
        IEnumerable<ResourceConstraint> required
    )
    {
        SaveResourceConstraints(node, "REQUIRED_RESOURCE", required);
    }

    public static void AddModuleId(ConfigNode node, string name, uint? moduleId)
    {
        if (moduleId == null)
            return;

        node.AddValue(name, (uint)moduleId);
    }

    public static void TryGetModuleId(ConfigNode node, string name, out uint? moduleId)
    {
        uint id = 0;
        if (node.TryGetValue(name, ref id))
            moduleId = id;
        else
            moduleId = null;
    }

    public static bool TryGetCondition(
        this ConfigNode node,
        string name,
        ref ConditionalExpression expr
    )
    {
        string cond = null;
        if (node.TryGetValue(name, ref cond))
        {
            expr = ConditionalExpression.Compile(cond, node);
            return true;
        }

        return false;
    }

    public static bool TryGetCondition2(
        this ConfigNode node,
        string name,
        out ConditionalExpression expr
    )
    {
        expr = default;
        return TryGetCondition(node, name, ref expr);
    }

    public static bool TryGetExpression<T>(
        this ConfigNode node,
        string name,
        ref FieldExpression<T> expr
    )
    {
        string cond = null;
        if (node.TryGetValue(name, ref cond))
        {
            expr = FieldExpression<T>.Compile(cond, node);
            return true;
        }

        return false;
    }
}
