using System;
using System.Collections.Generic;
using KSPAchievements;

namespace BackgroundResourceProcessing.Utils;

public struct ResourceConstraintExpression()
{
    public ConditionalExpression condition = ConditionalExpression.Always;

    public FieldExpression<string> ResourceName;

    public FieldExpression<double> Amount;

    public FieldExpression<Constraint> Constraint = new(
        _ => BackgroundResourceProcessing.Constraint.AT_LEAST,
        "AT_LEAST"
    );

    public FieldExpression<ResourceFlowMode> FlowMode = new(
        _ => ResourceFlowMode.ALL_VESSEL,
        "ALL_VESSEL"
    );

    public bool Evaluate(PartModule module, out ResourceConstraint constraint)
    {
        if (!condition.Evaluate(module))
        {
            constraint = default;
            return false;
        }

        constraint = new ResourceConstraint()
        {
            ResourceName = ResourceName.Evaluate(module),
            Amount = Amount.Evaluate(module) ?? 0.0,
            Constraint =
                Constraint.Evaluate(module) ?? BackgroundResourceProcessing.Constraint.AT_LEAST,
            FlowMode = FlowMode.Evaluate(module) ?? ResourceFlowMode.NULL,
        };

        return true;
    }

    public static ResourceConstraintExpression Load(Type target, ConfigNode node)
    {
        ResourceConstraintExpression result = new();

        node.TryGetCondition(nameof(condition), target, ref result.condition);
        node.TryGetExpression(nameof(ResourceName), target, ref result.ResourceName);
        node.TryGetExpression(nameof(Amount), target, ref result.Amount);
        node.TryGetExpression(nameof(Constraint), target, ref result.Constraint);
        node.TryGetExpression(nameof(FlowMode), target, ref result.FlowMode);

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
