using System;
using System.CodeDom;
using System.Collections.Generic;

namespace BackgroundResourceProcessing.Utils;

/// <summary>
/// A multiplier that can optionally be applied to a converter.
/// </summary>
///
/// <remarks>
/// This corresponds to the `MULTIPLIER` block in the documentation.
/// </remarks>
public struct ConverterMultiplier()
{
    ConditionalExpression condition = ConditionalExpression.Always;
    FieldExpression<double> value = FieldExpression<double>.Constant(1.0);

    public readonly double Evaluate(PartModule module)
    {
        if (!condition.Evaluate(module))
            return 1.0;

        return value.Evaluate(module) ?? 1.0;
    }

    public static ConverterMultiplier Load(Type target, ConfigNode node)
    {
        ConverterMultiplier mult = new();
        string condition = null;
        string field = null;
        string value = null;

        if (
            node.TryGetValue("Condition", ref condition)
            || node.TryGetValue("condition", ref condition)
        )
            mult.condition = ConditionalExpression.Compile(condition, node, target);
        if (node.TryGetValue("Field", ref field))
            mult.value = FieldExpression<double>.Field(field, target);
        if (node.TryGetValue("Value", ref value))
            mult.value = FieldExpression<double>.Compile(value, node, target);

        return mult;
    }

    public static List<ConverterMultiplier> LoadAll(Type target, ConfigNode node)
    {
        var children = node.GetNodes("MULTIPLIER");
        List<ConverterMultiplier> list = new(children.Length);

        foreach (var child in children)
            list.Add(Load(target, child));
        return list;
    }
}
