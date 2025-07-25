using System;
using System.Collections.Generic;

namespace BackgroundResourceProcessing.Utils;

public struct ResourceRatioExpression()
{
    public ResourceFlowMode FlowMode = ResourceFlowMode.NULL;

    public FieldExpression<string> ResourceName;

    public FieldExpression<double> Ratio = new(_ => 0.0, "0");

    public FieldExpression<bool> DumpExcess = new(_ => false, "false");

    public List<ConverterMultiplier> Multipliers = null;

    public ResourceRatio Evaluate(PartModule module)
    {
        var ratio = new ResourceRatio()
        {
            FlowMode = FlowMode,
            ResourceName = ResourceName.Evaluate(module),
            Ratio = Ratio.Evaluate(module) ?? 0.0,
            DumpExcess = DumpExcess.Evaluate(module) ?? false,
        };

        if (Multipliers != null)
        {
            foreach (var mult in Multipliers)
                ratio.Ratio *= mult.Evaluate(module);
        }

        return ratio;
    }

    public static ResourceRatioExpression Load(Type target, ConfigNode node)
    {
        ResourceRatioExpression result = new();

        node.TryGetExpression(nameof(ResourceName), ref result.ResourceName);
        node.TryGetExpression(nameof(Ratio), ref result.Ratio);
        node.TryGetExpression(nameof(DumpExcess), ref result.DumpExcess);

        string flowMode = null;
        if (node.TryGetValue(nameof(FlowMode), ref flowMode))
        {
            if (Enum.TryParse<ResourceFlowMode>(flowMode, out var parsed))
                result.FlowMode = parsed;
            else
            {
                LogUtil.Error($"GenericResourceRatio: Unknown flow mode `{flowMode}`");
                result.FlowMode = ResourceFlowMode.NULL;
            }
        }

        result.Multipliers = ConverterMultiplier.LoadAll(target, node);

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
