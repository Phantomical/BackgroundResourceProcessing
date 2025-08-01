using System;
using System.Collections.Generic;

namespace BackgroundResourceProcessing.Utils;

public struct ResourceRatioExpression()
{
    public FieldExpression<ResourceFlowMode> FlowMode = new(_ => ResourceFlowMode.NULL, "NULL");

    public FieldExpression<string> ResourceName;

    public FieldExpression<double> Ratio = new(_ => 0.0, "0");

    public FieldExpression<bool> DumpExcess = new(_ => false, "false");

    public ResourceRatio Evaluate(PartModule module)
    {
        var ratio = new ResourceRatio()
        {
            FlowMode = FlowMode.Evaluate(module) ?? ResourceFlowMode.NULL,
            ResourceName = ResourceName.Evaluate(module),
            Ratio = Ratio.Evaluate(module) ?? 0.0,
            DumpExcess = DumpExcess.Evaluate(module) ?? false,
        };

        return ratio;
    }

    public static ResourceRatioExpression Load(Type target, ConfigNode node)
    {
        ResourceRatioExpression result = new();

        node.TryGetExpression(nameof(ResourceName), target, ref result.ResourceName);
        node.TryGetExpression(nameof(Ratio), target, ref result.Ratio);
        node.TryGetExpression(nameof(DumpExcess), target, ref result.DumpExcess);
        node.TryGetExpression(nameof(FlowMode), target, ref result.FlowMode);

        return result;
    }

    public static List<ResourceRatioExpression> LoadList(
        Type target,
        ConfigNode node,
        string nodeName
    )
    {
        var nodes = node.GetNodes(nodeName);
        List<ResourceRatioExpression> ratios = new(nodes.Length);
        foreach (var child in nodes)
            ratios.Add(Load(target, child));
        return ratios;
    }

    public static List<ResourceRatioExpression> LoadInputs(Type target, ConfigNode node)
    {
        return LoadList(target, node, "INPUT_RESOURCE");
    }

    public static List<ResourceRatioExpression> LoadOutputs(Type target, ConfigNode node)
    {
        return LoadList(target, node, "OUTPUT_RESOURCE");
    }
}
