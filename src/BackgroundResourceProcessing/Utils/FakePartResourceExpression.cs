using System;
using System.Collections.Generic;
using BackgroundResourceProcessing.Inventory;

namespace BackgroundResourceProcessing.Utils;

public struct FakePartResourceExpression()
{
    public FieldExpression<string> ResourceName;
    public FieldExpression<double> Amount = new(_ => 0.0, "0");
    public FieldExpression<double> MaxAmount = new(_ => 0.0, "0");

    public readonly FakePartResource Evaluate(PartModule module)
    {
        return new()
        {
            ResourceName = ResourceName.Evaluate(module),
            Amount = Amount.Evaluate(module) ?? 0.0,
            MaxAmount = MaxAmount.Evaluate(module) ?? 0.0,
        };
    }

    public static FakePartResourceExpression Load(Type target, ConfigNode node)
    {
        FakePartResourceExpression result = new();

        node.TryGetExpression(nameof(ResourceName), target, ref result.ResourceName);
        node.TryGetExpression(nameof(Amount), target, ref result.Amount);
        node.TryGetExpression(nameof(MaxAmount), target, ref result.MaxAmount);

        return result;
    }

    public static List<FakePartResourceExpression> LoadList(
        Type target,
        ConfigNode node,
        string nodeName = "RESOURCE"
    )
    {
        var nodes = node.GetNodes(nodeName);
        var exprs = new List<FakePartResourceExpression>(nodes.Length);
        foreach (var child in nodes)
            exprs.Add(Load(target, child));
        return exprs;
    }
}
