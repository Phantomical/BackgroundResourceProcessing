using System;
using System.Collections.Generic;

namespace BackgroundResourceProcessing.Utils;

public struct ResourceConstraintExpression()
{
    public FieldExpression<string> ResourceName;
    public FieldExpression<double> Amount;
    public Constraint Constraint = Constraint.AT_LEAST;
    public ResourceFlowMode FlowMode = ResourceFlowMode.ALL_VESSEL;

    public ResourceConstraint Evaluate(PartModule module)
    {
        var constraint = new ResourceConstraint()
        {
            ResourceName = ResourceName.Evaluate(module),
            Amount = Amount.Evaluate(module) ?? 0.0,
            Constraint = Constraint,
            FlowMode = FlowMode,
        };

        return constraint;
    }

    public static ResourceConstraintExpression Load(Type target, ConfigNode node)
    {
        ResourceConstraintExpression result = new();

        node.TryGetExpression(nameof(ResourceName), ref result.ResourceName);
        node.TryGetExpression(nameof(Amount), ref result.Amount);

        string value = null;
        if (node.TryGetValue(nameof(Constraint), ref value))
        {
            if (Enum.TryParse<Constraint>(value, out var parsed))
                result.Constraint = parsed;
            else
            {
                LogUtil.Error($"GenericResourceRatio: Unknown constraint type `{value}`");
                result.Constraint = Constraint.AT_LEAST;
            }
        }

        value = null;
        if (node.TryGetValue(nameof(FlowMode), ref value))
        {
            if (Enum.TryParse<ResourceFlowMode>(value, out var parsed))
                result.FlowMode = parsed;
            else
            {
                LogUtil.Error($"GenericResourceRatio: Unknown flow mode `{value}`");
                result.FlowMode = ResourceFlowMode.NULL;
            }
        }

        return result;
    }

    public static List<ResourceConstraintExpression> LoadList(
        Type target,
        ConfigNode node,
        string nodeName
    )
    {
        var nodes = node.GetNodes(nodeName);
        List<ResourceConstraintExpression> ratios = new(nodes.Length);
        foreach (var child in nodes)
            ratios.Add(Load(target, child));
        return ratios;
    }

    public static List<ResourceConstraintExpression> LoadRequirements(Type target, ConfigNode node)
    {
        return LoadList(target, node, "REQUIRED_RESOURCE");
    }
}
