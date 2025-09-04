using System;
using System.Linq.Expressions;
using System.Reflection;

namespace BackgroundResourceProcessing.Integration.Kerbalism;

internal static class SymbolUtils
{
    public static FieldInfo GetFieldInfo(LambdaExpression expr)
    {
        if (expr.Body is not MemberExpression body)
            throw new ArgumentException(
                "Invalid expression. Expression should consist of a field access only."
            );

        if (body.Member is not FieldInfo field)
            throw new ArgumentException(
                $"Invalid expression. Member {body.Member.Name} is not a field"
            );

        return field;
    }

    public static PropertyInfo GetPropertyInfo(LambdaExpression expr)
    {
        if (expr.Body is not MemberExpression body)
            throw new ArgumentException(
                "Invalid expression. Expression should consist of a field access only."
            );

        if (body.Member is not PropertyInfo prop)
            throw new ArgumentException(
                $"Invalid expression. Member {body.Member.Name} is not a property"
            );

        return prop;
    }
}
