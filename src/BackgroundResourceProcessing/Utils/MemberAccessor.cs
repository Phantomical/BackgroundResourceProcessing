using System;
using System.Reflection;

namespace BackgroundResourceProcessing.Utils;

public readonly struct MemberAccessor<T>
{
    public enum Access
    {
        Read = 1,
        Write = 2,
        ReadWrite = Read | Write,
    }

    readonly Access access;
    readonly MemberInfo member;

    public readonly string Name => member.Name;
    public readonly MemberInfo Member => member;

    public MemberAccessor(Type type, string name, Access access = Access.ReadWrite)
    {
        const BindingFlags Flags =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        this.member = GetMember(type, name, Flags);
        this.access = access;

        if (member == null)
            throw new Exception($"type {type.Name} has no field or property named `{name}`");

        if (member is PropertyInfo property)
        {
            if ((access & Access.Read) != 0 && !property.CanRead)
                throw new Exception(
                    $"property {type.Name}.{property.Name} is required to be readable, but was not readable"
                );

            if ((access & Access.Write) != 0 && !property.CanWrite)
                throw new Exception(
                    $"property {type.Name}.{property.Name} is required to be writable, but was not writable"
                );
        }

        var memberType = GetMemberType(member);
        if (!IsCompatibleType(memberType))
            throw new Exception(
                $"property {type.Name}.{name} had type {memberType.Name} which was not compatible with expected type {typeof(T).Name}"
            );
    }

    public T GetValue(object obj)
    {
        if ((access & Access.Read) == 0)
            throw new InvalidOperationException(
                "Cannot read a field value using a MemberAccessor not configured for read access"
            );

        var value = GetMemberValue(member, obj);

        if (typeof(T) == typeof(double))
        {
            if (value is float f)
                return (T)(object)(double)f;
        }

        return (T)value;
    }

    public void SetValue(object obj, T value)
    {
        if ((access & Access.Write) == 0)
            throw new InvalidOperationException(
                "Cannot write a field value using a MemberAccessor not configured for write access"
            );

        SetMemberValue(member, obj, value);
    }

    private static MemberInfo GetMember(Type type, string name, BindingFlags flags)
    {
        var field = type.GetField(name, flags);
        if (field != null)
            return field;

        var property = type.GetProperty(name, flags);
        if (property != null)
            return property;

        return null;
    }

    private static Type GetMemberType(MemberInfo member)
    {
        return member switch
        {
            FieldInfo field => field.FieldType,
            PropertyInfo property => property.PropertyType,
            _ => throw new NotImplementedException(
                $"unsupported member type {member.GetType().Name}"
            ),
        };
    }

    private static object GetMemberValue(MemberInfo member, object obj)
    {
        return member switch
        {
            FieldInfo field => field.GetValue(obj),
            PropertyInfo property => property.GetValue(obj),
            _ => throw new NotImplementedException(
                $"unsupported member type {member.GetType().Name}"
            ),
        };
    }

    private static void SetMemberValue(MemberInfo member, object obj, object value)
    {
        if (member is FieldInfo field)
            field.SetValue(obj, value);
        else if (member is PropertyInfo property)
            property.SetValue(obj, value);
        else
            throw new NotImplementedException($"unsupported member type {member.GetType().Name}");
    }

    private static bool IsCompatibleType(Type memberType)
    {
        if (typeof(T) == memberType)
            return true;

        if (typeof(T).IsAssignableFrom(memberType))
            return true;

        if (typeof(T) == typeof(double))
        {
            if (memberType == typeof(float))
                return true;
        }

        return false;
    }
}
