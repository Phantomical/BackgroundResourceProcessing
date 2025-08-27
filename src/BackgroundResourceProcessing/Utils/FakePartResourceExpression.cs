using System;
using System.Collections.Generic;
using BackgroundResourceProcessing.Inventory;

namespace BackgroundResourceProcessing.Utils;

public struct FakePartResourceExpression()
{
    public ConditionalExpression condition = ConditionalExpression.Always;
    public string ResourceName;
    public FieldExpression<double> Amount = new(_ => 0.0, "0");
    public FieldExpression<double> MaxAmount = new(_ => 0.0, "0");

    public readonly bool Evaluate(PartModule module, out FakePartResource res)
    {
        if (!condition.Evaluate(module))
        {
            res = default;
            return false;
        }

        res = new()
        {
            ResourceName = ResourceName,
            Amount = Amount.Evaluate(module) ?? 0.0,
            MaxAmount = MaxAmount.Evaluate(module) ?? 0.0,
        };

        return true;
    }

    public static FakePartResourceExpression Load(Type target, ConfigNode node)
    {
        FakePartResourceExpression result = new();

        node.TryGetValue(nameof(ResourceName), ref result.ResourceName);
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
