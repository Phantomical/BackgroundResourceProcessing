using System;
using System.Reflection;

#nullable enable

namespace BackgroundResourceProcessing.Utils;

/// <summary>
/// This is a helper type to wrap a fairly common pattern of having a
/// `X` field which provides a default value and a `XField` field which
/// allows users to specify a field to inspect on the target type.
/// </summary>
/// <typeparam name="T"></typeparam>
public class FieldExtractor<T>
{
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    readonly T value;
    readonly MemberInfo? member;

    public FieldExtractor(Type type, string field, T defaultValue)
    {
        value = defaultValue;

        if (field == null)
            return;

        if (type == null)
            throw new ArgumentNullException(
                "type",
                "Attempted to create a FieldExtractor from a null type"
            );

        member = GetMember(type, field, Flags);
        if (member == null)
            throw new Exception($"There is no member on {type.Name} named `{field}`");

        if (member is PropertyInfo property)
        {
            if (!property.CanRead)
                throw new Exception($"Property {type.Name}.{field} is not readable");
        }

        var memberType = GetMemberType(member);
        if (!IsCompatibleType(memberType))
            throw new Exception(
                $"{type.Name}.{field} is not of type {typeof(T).Name} (found {memberType.Name} instead)"
            );
    }

    public T? GetValue(object obj)
    {
        if (member == null)
            return this.value;

        var value = GetCompatibleValue(GetMemberValue(member, obj));
        if (value == null)
            return (T?)value;

        if (value is T downcasted)
            return downcasted;

        throw new InvalidCastException(
            $"Value of type {value.GetType().Name} cannot be casted to {typeof(T).Name}"
        );
    }

    public object? GetCompatibleValue(object? value)
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

        return value;
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

        return false;
    }

    private static Type GetMemberType(MemberInfo member)
    {
        return member switch
        {
            FieldInfo fieldInfo => fieldInfo.FieldType,
            PropertyInfo propertyInfo => propertyInfo.PropertyType,
            _ => throw new NotImplementedException(
                $"Cannot handle a member of type {member.MemberType}"
            ),
        };
    }

    private static MemberInfo? GetMember(Type type, string name, BindingFlags flags)
    {
        var field = type.GetField(name, flags);
        if (field != null)
            return field;

        var property = type.GetProperty(name, flags);
        if (property != null)
            return property;

        return null;
    }

    private static object GetMemberValue(MemberInfo member, object obj)
    {
        return member switch
        {
            FieldInfo fieldInfo => fieldInfo.GetValue(obj),
            PropertyInfo propertyInfo => propertyInfo.GetValue(obj),
            _ => throw new NotImplementedException(
                $"Cannot handle a member of type {member.MemberType}"
            ),
        };
    }
}
