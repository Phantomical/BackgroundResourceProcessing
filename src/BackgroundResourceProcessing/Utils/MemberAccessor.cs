using System;
using System.Linq.Expressions;
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

    public const Access Read = Access.Read;
    public const Access Write = Access.Write;
    public const Access ReadWrite = Access.ReadWrite;

    readonly MemberInfo member;
    readonly Func<object, T> getter = null;
    readonly Action<object, T> setter = null;

    public readonly string Name => member.Name;
    public readonly MemberInfo Member => member;

    public MemberAccessor(Type type, string name, Access access = Access.ReadWrite)
    {
        const BindingFlags Flags =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        member = GetMember(type, name, Flags);

        if (member == null)
            throw new Exception($"type {type.Name} has no field or property named `{name}`");

        if (member is PropertyInfo property)
        {
            if (access.HasFlag(Access.Read) && !property.CanRead)
                throw new Exception(
                    $"property {type.Name}.{property.Name} is required to be readable, but was not readable"
                );

            if (access.HasFlag(Access.Write) && !property.CanWrite)
                throw new Exception(
                    $"property {type.Name}.{property.Name} is required to be writable, but was not writable"
                );
        }

        if (access.HasFlag(Access.Read))
            getter = CreateGetter(member);

        if (access.HasFlag(Access.Write))
            setter = CreateSetter(member);
    }

    public T GetValue(object obj)
    {
        if (getter == null)
            throw new InvalidOperationException(
                "Cannot read a field value using a MemberAccessor not configured for read access"
            );

        return getter(obj);
    }

    public void SetValue(object obj, T value)
    {
        if (setter == null)
            throw new InvalidOperationException(
                "Cannot write a field value using a MemberAccessor not configured for write access"
            );

        setter(obj, value);
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

    private static Func<object, T> CreateGetter(MemberInfo member)
    {
        var parent = member.DeclaringType;
        var param = Expression.Parameter(typeof(object));
        var casted = Expression.Convert(param, parent);

        Expression access;
        if (member is FieldInfo field)
            access = Expression.Field(casted, field);
        else if (member is PropertyInfo property)
            access = Expression.Property(casted, property);
        else
            throw new NotSupportedException();

        if (access.Type != typeof(T))
        {
            if (typeof(T) == typeof(double) && access.Type == typeof(float))
                access = Expression.Convert(access, typeof(T));
            else if (typeof(T) == typeof(float) && access.Type == typeof(double))
                access = Expression.Convert(access, typeof(T));
            else
                throw new NotSupportedException(
                    $"Cannot convert a field {parent.Name}.{member.Name} of type {access.Type.Name} to type {typeof(T).Name}"
                );
        }

        return Expression.Lambda<Func<object, T>>(access, param).Compile();
    }

    private static Action<object, T> CreateSetter(MemberInfo member)
    {
        var parent = member.DeclaringType;
        var param = Expression.Parameter(typeof(object));
        var casted = Expression.Convert(param, parent);
        var value = Expression.Parameter(typeof(T));

        if (parent.IsValueType)
            throw new NotSupportedException(
                $"Cannot use MemberAccessor to set a field on value type {parent.Name}"
            );

        Expression converted;
        var memberType = GetMemberType(member);
        if (typeof(T) == memberType)
            converted = value;
        else if (typeof(T) == typeof(double) && memberType == typeof(float))
            converted = Expression.Convert(value, memberType);
        else if (typeof(T) == typeof(float) && memberType == typeof(double))
            converted = Expression.Convert(value, memberType);
        else
            throw new NotSupportedException(
                $"Cannot use MemberAccessor to set a field or property {parent.Name}.{member.Name} of type {memberType.Name} with a value of type {typeof(T).Name}"
            );

        Expression setter;
        if (member is FieldInfo field)
            setter = Expression.Assign(Expression.Field(casted, field), converted);
        else if (member is PropertyInfo property)
            setter = Expression.Assign(Expression.Property(casted, property), converted);
        else
            throw new NotSupportedException();

        return Expression.Lambda<Action<object, T>>(setter, param, value).Compile();
    }
}
