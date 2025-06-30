using System;
using System.Reflection;

#nullable enable

namespace BackgroundResourceProcessing.Utils
{
    /// <summary>
    /// This is a helper type to wrap a fairly common pattern of having a
    /// `X` field which provides a default value and a `XField` field which
    /// allows users to specify a field to inspect on the target type.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class FieldExtractor<T>
    {
        const BindingFlags Flags =
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

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

            member = (MemberInfo)type.GetField(field, Flags) ?? type.GetProperty(field, Flags);
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
                return value;

            return (T)GetMemberValue(member, obj);
        }

        private static bool IsCompatibleType(Type type)
        {
            if (type == typeof(T))
                return true;

            // Special case, we can cast doubles to floats
            if (typeof(T) == typeof(double) && type == typeof(float))
                return true;

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
}
