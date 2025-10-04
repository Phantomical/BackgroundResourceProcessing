using System;

namespace BackgroundResourceProcessing.Utils;

public readonly struct FieldExpression<T>
{
    readonly Expr.FieldExpression<T> Inner;

    internal FieldExpression(Expr.FieldExpression<T> inner) => Inner = inner;

    public FieldExpression(Func<PartModule, T> func, string text)
        : this(new(func, text)) { }

    public readonly bool TryEvaluate(PartModule module, out T value) =>
        Inner.TryEvaluate(module, out value);

    public static FieldExpression<T> Compile(
        string expression,
        ConfigNode node,
        Type target = null
    ) => new(Expr.FieldExpression<T>.Compile(expression, node, target));

    public static FieldExpression<T> Constant(T value) =>
        new(Expr.FieldExpression<T>.Constant(value));

    public static FieldExpression<T> Field(string name, Type type) =>
        new(Expr.FieldExpression<T>.Field(name, type));

    public override readonly string ToString() => Inner.ToString();
}

public static class FieldExpression
{
    public static FieldExpression<bool> CompileMany(
        string[] expressions,
        ConfigNode node,
        Type target = null
    ) => new(Expr.FieldExpression<bool>.CompileMany(expressions, node, target));
}

public static class FieldExpressionExtensions
{
    public static T? Evaluate<T>(this FieldExpression<T> expr, PartModule module)
        where T : struct
    {
        if (expr.TryEvaluate(module, out var value))
            return value;
        return null;
    }

    public static string Evaluate(this FieldExpression<string> expr, PartModule module)
    {
        if (expr.TryEvaluate(module, out var value))
            return value;
        return null;
    }
}
