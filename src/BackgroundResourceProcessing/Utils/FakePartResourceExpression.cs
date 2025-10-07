using System;
using System.Collections.Generic;
using BackgroundResourceProcessing.Inventory;

namespace BackgroundResourceProcessing.Utils;

public struct FakePartResourceExpression()
{
    public ConditionalExpression condition = ConditionalExpression.Always;
    public FieldExpression<string> ResourceName;
    public FieldExpression<double> Amount = new(_ => 0.0, "0");
    public FieldExpression<double> MaxAmount = new(_ => 0.0, "0");

    public readonly bool Evaluate(PartModule module, out FakePartResource res)
    {
        if (!condition.Evaluate(module))
        {
            res = default;
            return false;
        }

        var resourceName = ResourceName.Evaluate(module);
        if (resourceName is null)
        {
            LogUtil.Error(
                "FakePartResource ResourceName evaluated to null. This resource expression will be ignored."
            );
            res = default;
            return false;
        }

        res = new()
        {
            ResourceName = resourceName,
            Amount = Amount.Evaluate(module) ?? 0.0,
            MaxAmount = MaxAmount.Evaluate(module) ?? 0.0,
        };

        return true;
    }

    public static FakePartResourceExpression Load(Type target, ConfigNode node)
    {
        FakePartResourceExpression result = new();

        node.TryGetCondition(nameof(condition), target, ref result.condition);
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
