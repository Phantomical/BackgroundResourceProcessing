using System;
using System.Collections.Generic;

namespace BackgroundResourceProcessing.Utils;

public static class ConfigUtil
{
    public static List<ResourceRatio> LoadResourceRatios(ConfigNode node, string name)
    {
        int count = node.nodes.Count;
        List<ResourceRatio> list = new(4);
        for (int i = 0; i < count; ++i)
        {
            var child = node.nodes[i];
            if (child.name != name)
                continue;
            if (!child.HasValue("ResourceName"))
            {
                LogUtil.Error(
                    $"{name} node must have ResourceName field. Resource will be skipped."
                );
                continue;
            }

            ResourceRatio ratio = default;
            ratio.FlowMode = ResourceFlowMode.NULL;
            ratio.Load(child);
            list.Add(ratio);
        }

        return list;
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

    public static void SaveResourceRatios(ConfigNode node, string name, List<ResourceRatio> ratios)
    {
        foreach (var ratio in ratios)
            ratio.Save(node.AddNode(name));
    }

    public static List<ResourceConstraint> LoadResourceConstraints(ConfigNode node, string name)
    {
        int count = node.nodes.Count;
        List<ResourceConstraint> list = new(4);
        for (int i = 0; i < count; ++i)
        {
            var child = node.nodes[i];
            if (child.name != name)
                continue;
            if (!child.HasValue("ResourceName"))
            {
                LogUtil.Error(
                    $"{name} node must have ResourceName field. Resource will be skipped."
                );
                continue;
            }

            ResourceConstraint constraint = new();
            constraint.Load(child);
            list.Add(constraint);
        }

        return list;
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

    public static void SaveResourceConstraints(
        ConfigNode node,
        string name,
        List<ResourceConstraint> constraints
    )
    {
        foreach (var constraint in constraints)
            constraint.Save(node.AddNode(name));
    }

    public static List<ResourceRatio> LoadOutputResources(ConfigNode node)
    {
        return LoadResourceRatios(node, "OUTPUT_RESOURCE");
    }

    public static List<ResourceRatio> LoadInputResources(ConfigNode node)
    {
        return LoadResourceRatios(node, "INPUT_RESOURCE");
    }

    public static List<ResourceConstraint> LoadRequiredResources(ConfigNode node)
    {
        return LoadResourceConstraints(node, "REQUIRED_RESOURCE");
    }

    public static void SaveOutputResources(ConfigNode node, IEnumerable<ResourceRatio> outputs)
    {
        SaveResourceRatios(node, "OUTPUT_RESOURCE", outputs);
    }

    public static void SaveOutputResources(ConfigNode node, List<ResourceRatio> outputs)
    {
        SaveResourceRatios(node, "OUTPUT_RESOURCE", outputs);
    }

    public static void SaveInputResources(ConfigNode node, IEnumerable<ResourceRatio> inputs)
    {
        SaveResourceRatios(node, "INPUT_RESOURCE", inputs);
    }

    public static void SaveInputResources(ConfigNode node, List<ResourceRatio> inputs)
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

    public static void SaveRequiredResources(ConfigNode node, List<ResourceConstraint> required)
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
        Type target,
        ref ConditionalExpression expr
    )
    {
        var conditions = node.GetValues(name);
        if (conditions == null || conditions.Length == 0)
            return false;

        expr = ConditionalExpression.Compile(conditions, node, target);
        return true;
    }

    public static bool TryGetCondition2(
        this ConfigNode node,
        string name,
        Type target,
        out ConditionalExpression expr
    )
    {
        expr = default;
        return TryGetCondition(node, name, target, ref expr);
    }

    public static bool TryGetExpression<T>(
        this ConfigNode node,
        string name,
        Type target,
        ref FieldExpression<T> expr
    )
    {
        string cond = null;
        if (node.TryGetValue(name, ref cond))
        {
            expr = FieldExpression<T>.Compile(cond, node, target);
            return true;
        }

        return false;
    }

    public interface IConfigLoadable
    {
        void Load(ConfigNode node);
        void Save(ConfigNode node);
    }

    public static List<T> LoadNodeList<T>(ConfigNode node, string nodeName)
        where T : IConfigLoadable
    {
        var list = new List<T>(4);

        int count = node.nodes.Count;
        for (int i = 0; i < count; ++i)
        {
            var child = node.nodes[i];
            if (child.name != nodeName)
                continue;
            T item = Activator.CreateInstance<T>();
            item.Load(child);
            list.Add(item);
        }

        return list;
    }

    public static void SaveNodeList<T>(ConfigNode node, string nodeName, IEnumerable<T> items)
        where T : IConfigLoadable
    {
        foreach (var item in items)
            item.Save(node.AddNode(nodeName));
    }

    public static void SaveNodeList<T>(ConfigNode node, string nodeName, List<T> items)
        where T : IConfigLoadable
    {
        foreach (var item in items)
            item.Save(node.AddNode(nodeName));
    }
}
