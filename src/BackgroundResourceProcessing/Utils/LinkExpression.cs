using System;
using System.Collections.Generic;
using BackgroundResourceProcessing.Converter;

namespace BackgroundResourceProcessing.Utils;

public struct LinkExpression()
{
    public enum LinkRelation
    {
        INVALID = 0,

        PUSH = 0x1,
        PULL = 0x2,
        REQUIRED = 0x4,

        PUSH_AND_PULL = 0x3,
        ALL = 0x7,
    }

    public ConditionalExpression condition = ConditionalExpression.Always;

    public LinkRelation Relation = LinkRelation.INVALID;

    public FieldExpression<PartModule> Target = new();

    public readonly void Evaluate(PartModule module, ModuleBehaviour behaviour)
    {
        if (!condition.Evaluate(module))
            return;

        if (!Target.TryEvaluate(module, out var target))
            return;
        if (target == null)
            return;

        if (Relation.HasFlag(LinkRelation.PUSH))
            behaviour.AddPushModule(target);

        if (Relation.HasFlag(LinkRelation.PULL))
            behaviour.AddPullModule(target);

        if (Relation.HasFlag(LinkRelation.REQUIRED))
            behaviour.AddConstraintModule(target);
    }

    public static LinkExpression Load(Type target, ConfigNode node)
    {
        LinkExpression expr = new();

        node.TryGetCondition(nameof(condition), target, ref expr.condition);
        node.TryGetEnum(nameof(Relation), ref expr.Relation, LinkRelation.INVALID);
        node.TryGetExpression(nameof(Target), target, ref expr.Target);

        return expr;
    }

    public static List<LinkExpression> LoadList(
        Type target,
        ConfigNode node,
        string nodeName = "INVENTORY_LINK"
    )
    {
        var nodes = node.GetNodes(nodeName);
        List<LinkExpression> links = new(nodes.Length);
        foreach (var child in nodes)
            links.Add(Load(target, child));
        return links;
    }
}
