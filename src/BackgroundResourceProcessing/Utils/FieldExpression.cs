using System;
using System.Linq.Expressions;
using System.Reflection;

namespace BackgroundResourceProcessing.Utils;

public readonly partial struct FieldExpression<T>(Func<PartModule, T> func, string text)
{
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    readonly string text = text;
    readonly Func<PartModule, T> func = func;

    public readonly bool TryEvaluate(PartModule module, out T value)
    {
        value = default;
        if (func == null)
            return false;

        try
        {
            value = func(module);
            return true;
        }
        catch (Exception e)
        {
            LogUtil.Error($"An exception was thrown while evaluating expression `{text}`: {e}");
        }

        return false;
    }

    public static FieldExpression<T> Compile(string expression, ConfigNode node, Type target = null)
    {
        target ??= typeof(PartModule);

        FieldExpression.Lexer lexer = new(expression);
        lexer.MoveNext();

        FieldExpression.Parser parser = new(lexer, node, target);
        var func = parser.Parse<T>();

        return new(func, expression);
    }

    public static FieldExpression<T> Constant(T value)
    {
        return new(_ => value, value.ToString());
    }

    public static FieldExpression<T> Field(string name, Type type)
    {
        if (name == null)
            throw new ArgumentNullException(nameof(name));
        if (type == null)
            throw new ArgumentNullException(nameof(type));

        var field = type.GetField(name, Flags);
        if (field != null)
        {
            if (!IsCompatibleType(field.FieldType))
                throw new Exception(
                    $"{type.Name}.{field} is not of type {typeof(T).Name} (found {field.FieldType.Name} instead)"
                );

            return new(module => (T)GetCompatibleValue(field.GetValue(module)), name);
        }

        var property = type.GetProperty(name, Flags);
        if (property != null)
        {
            if (!IsCompatibleType(property.PropertyType))
                throw new Exception(
                    $"{type.Name}.{field} is not of type {typeof(T).Name} (found {property.PropertyType.Name} instead)"
                );

            if (!property.CanRead)
                throw new Exception($"Property {type.Name}.{field} is not readable");

            return new(module => (T)GetCompatibleValue(property.GetValue(module)), name);
        }

        throw new Exception($"Type {type.Name} has no field or property named `{field}`");
    }

    private static bool IsCompatibleType(Type type)
    {
        if (type == typeof(T))
            return true;

        // Special case, we can cast a whole bunch of types to double/float
        if (typeof(T) == typeof(double) || typeof(T) == typeof(float))
        {
            return type == typeof(float)
                || type == typeof(double)
                || type == typeof(ushort)
                || type == typeof(uint)
                || type == typeof(ulong)
                || type == typeof(short)
                || type == typeof(int)
                || type == typeof(long);
        }

        if (typeof(T).IsEnum)
            return type == typeof(string);

        if (typeof(T) == typeof(bool))
            return true;

        return false;
    }

    private static object GetCompatibleValue(object value)
    {
        if (typeof(T) == typeof(double))
        {
            return value switch
            {
                double v => v,
                float v => (double)v,
                ushort v => (double)v,
                uint v => (double)v,
                ulong v => (double)v,
                short v => (double)v,
                int v => (double)v,
                long v => (double)v,
                _ => value,
            };
        }

        if (typeof(T) == typeof(float))
        {
            return value switch
            {
                float v => v,
                double v => (float)v,
                ushort v => (float)v,
                uint v => (float)v,
                ulong v => (float)v,
                short v => (float)v,
                int v => (float)v,
                long v => (float)v,
                _ => value,
            };
        }

        if (typeof(T) == typeof(bool))
            return FieldExpression.Methods.CoerceToBool(value);

        if (typeof(T).IsEnum)
        {
            if (value is string s)
                return (T)Enum.Parse(typeof(T), s);
        }

        return value;
    }

    private static Func<PartModule, object> MakeLambdaGeneric(Expression<Func<PartModule, T>> expr)
    {
        if (expr is not LambdaExpression lambda)
            throw new Exception($"Expected a lambda expression but got `{expr}` instead");

        var result = Expression.Lambda<Func<PartModule, object>>(
            Expression.Convert(lambda.Body, typeof(object)),
            lambda.Parameters
        );

        return result.Compile();
    }

    public override string ToString()
    {
        return text ?? "";
    }
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
