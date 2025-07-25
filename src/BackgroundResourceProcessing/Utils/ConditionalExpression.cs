using System;
using System.Linq.Expressions;

namespace BackgroundResourceProcessing.Utils;

/// <summary>
/// A conditional expression that can be evaluated against a <see cref="PartModule"/>.
/// </summary>
///
/// <remarks>
/// There are lots of cases where you might want to make some section of a
/// config conditional on one thing or another. Normally, this would require
/// writing code. This would result in tons of specific modules. Instead,
/// conditional expressions act as a midpoint that allows evaluating reasonable
/// conditions without having to write a custom plugin.
/// </remarks>
public readonly struct ConditionalExpression(FieldExpression<bool> expr)
{
    readonly FieldExpression<bool> expression = expr;

    public static ConditionalExpression Always => new(_ => true, "true");
    public static ConditionalExpression Never => new(_ => false, "false");

    private ConditionalExpression(Expression<Func<PartModule, bool>> func, string text)
        : this(new FieldExpression<bool>(func, text)) { }

    /// <summary>
    /// Evaluate this expression on the provided module.
    /// </summary>
    /// <param name="module"></param>
    /// <returns></returns>
    public readonly bool Evaluate(PartModule module)
    {
        return expression.Evaluate(module) ?? false;
    }

    public override readonly string ToString()
    {
        return expression.ToString();
    }

    /// <summary>
    /// Compile a <see cref="ConditionalExpression"/> from the provided
    /// expression and config node context.
    /// </summary>
    /// <returns></returns>
    public static ConditionalExpression Compile(
        string expression,
        ConfigNode node,
        Type target = null
    )
    {
        return new(FieldExpression<bool>.Compile(expression, node, target));
    }
}
