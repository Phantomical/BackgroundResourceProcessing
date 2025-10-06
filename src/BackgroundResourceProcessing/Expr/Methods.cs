using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace BackgroundResourceProcessing.Expr;

// Static helper methods used to actually implement semantics
internal static class Methods
{
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    #region Field Access
    internal static object DoFieldAccess(object obj, string member)
    {
        if (obj == null)
            return null;

        var type = obj.GetType();
        try
        {
            var field = type.GetField(member, Flags);
            if (field != null)
                return field.GetValue(obj);

            var prop = type.GetProperty(member, Flags);
            if (prop == null)
                return null;
            if (!prop.CanRead)
                return null;
            return prop.GetValue(obj);
        }
        catch (Exception e)
        {
            LogUtil.Warn($"Access of field {type.Name}.{member} threw an excpetion: {e}");
            return null;
        }
    }
    #endregion

    #region Index Access
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static T? DoArrayIndexAccessStruct<T>(T[] array, int index)
        where T : struct
    {
        if (array is null)
            return null;
        if (index < 0 || index >= array.Length)
            return null;
        return array[index];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static T? DoArrayIndexAccessNullableStruct<T>(T?[] array, int index)
        where T : struct
    {
        if (array is null)
            return null;
        if (index < 0 || index >= array.Length)
            return null;
        return array[index];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static T DoArrayIndexAccessClass<T>(T[] array, int index)
        where T : class
    {
        if (array is null)
            return null;
        if (index < 0 || index >= array.Length)
            return null;
        return array[index];
    }

    internal static object DoIndexAccess(object obj, object index)
    {
        static bool FindMatchingIndexer(
            PropertyInfo[] indexers,
            Type type,
            out PropertyInfo indexer
        )
        {
            foreach (var prop in indexers)
            {
                var ps = prop.GetIndexParameters();
                if (ps.Length != 1)
                    continue;

                if (ps[0].ParameterType != type)
                    continue;

                indexer = prop;
                return true;
            }

            indexer = null;
            return false;
        }

        if (obj == null || index == null)
            return null;

        var type = obj.GetType();

        try
        {
            var indexType = index.GetType();
            if (obj is Array array)
            {
                if (!CastToIndexType(typeof(int), index, out var casted))
                    return null;
                return array.GetValue((int)casted);
            }

            var indexers = type.GetProperties(Flags);

            if (FindMatchingIndexer(indexers, indexType, out var exactMatch))
                return exactMatch.GetGetMethod().Invoke(obj, [index]);

            if (exactMatch != null)
                return exactMatch.GetGetMethod().Invoke(obj, [index]);

            foreach (var indexer in indexers)
            {
                var param = indexer.GetIndexParameters()[0];
                if (CastToIndexType(param.ParameterType, index, out var casted))
                    return indexer.GetGetMethod().Invoke(obj, [casted]);
            }

            if (index is string field)
                return DoFieldAccess(obj, field);

            // A special case for float curves that allows accessing the key frames.
            if (obj is FloatCurve fc)
            {
                if (CastToIndexType(typeof(int), index, out var casted))
                    return fc.Curve[(int)casted];
            }

            return null;
        }
        // Handle some common cases that may just be intentional.
        catch (IndexOutOfRangeException)
        {
            return null;
        }
        catch (KeyNotFoundException)
        {
            return null;
        }
        catch (Exception e)
        {
            LogUtil.Warn($"Indexer for {type.Name} threw an exception: {e}");
            return null;
        }
    }

    static bool CastToIndexType(Type indexType, object index, out object casted)
    {
        var type = index.GetType();
        if (indexType == type)
        {
            casted = index;
            return true;
        }

        if (indexType == typeof(int))
        {
            if (type == typeof(double))
                casted = (int)(double)index;
            else if (type == typeof(float))
                casted = (int)(float)index;
            else if (type == typeof(uint))
                casted = (int)(uint)index;
            else
            {
                casted = null;
                return false;
            }

            return true;
        }

        casted = null;
        return false;
    }
    #endregion

    #region Method Invoke
    public static object DoMethodInvoke(object obj, string method, params object[] parameters)
    {
        var type = obj.GetType();
        Type[] ptypes = [.. parameters.Select(p => p.GetType())];

        // First try exact match
        var member = type.GetMethod(method, Flags, null, ptypes, []);

        // If no exact match, try to find compatible methods
        if (member == null)
        {
            var methods = type.GetMethods(Flags).Where(m => m.Name == method);
            foreach (var candidateMethod in methods)
            {
                var candidateParams = candidateMethod.GetParameters();
                if (candidateParams.Length != parameters.Length)
                    continue;

                bool compatible = true;
                object[] convertedParams = new object[parameters.Length];

                for (int i = 0; i < parameters.Length; i++)
                {
                    var param = parameters[i];
                    var targetType = candidateParams[i].ParameterType;

                    if (param == null)
                    {
                        convertedParams[i] = null;
                        continue;
                    }

                    var paramType = param.GetType();
                    if (targetType.IsAssignableFrom(paramType))
                    {
                        convertedParams[i] = param;
                    }
                    else if (TryConvertParameter(param, targetType, out var converted))
                    {
                        convertedParams[i] = converted;
                    }
                    else
                    {
                        compatible = false;
                        break;
                    }
                }

                if (compatible)
                {
                    member = candidateMethod;
                    parameters = convertedParams;
                    break;
                }
            }
        }

        if (member == null)
            throw new Exception(
                $"There is no method {type.Name}.{method}({string.Join(", ", ptypes.Select(t => t.Name))})"
            );

        return member.Invoke(obj, parameters);
    }

    static bool TryConvertParameter(object param, Type targetType, out object converted)
    {
        converted = null;

        if (param == null)
        {
            converted = null;
            return !targetType.IsValueType || Nullable.GetUnderlyingType(targetType) != null;
        }

        var paramType = param.GetType();

        // Handle numeric conversions
        if (IsNumericType(paramType) && IsNumericType(targetType))
        {
            try
            {
                converted = Convert.ChangeType(param, targetType);
                return true;
            }
            catch
            {
                return false;
            }
        }

        return false;
    }
    #endregion

    static bool IsNumericType(Type type)
    {
        return type == typeof(byte)
            || type == typeof(sbyte)
            || type == typeof(short)
            || type == typeof(ushort)
            || type == typeof(int)
            || type == typeof(uint)
            || type == typeof(long)
            || type == typeof(ulong)
            || type == typeof(float)
            || type == typeof(double)
            || type == typeof(decimal);
    }

    public static Type DoGetType(object obj)
    {
        return obj?.GetType();
    }

    public static object DoUnaryMinus(object obj)
    {
        if (obj == null)
            return null;
        if (obj is double d)
            return -d;
        if (obj is float f)
            return -f;
        if (obj is int i)
            return -i;
        if (obj is long l)
            return -l;

        throw new EvaluationException($"Cannot negate a value of type {obj.GetType()}");
    }

    public static object DoUnaryPlus(object obj)
    {
        if (obj is double d)
            return d;
        if (obj is float f)
            return f;
        if (obj is int i)
            return i;
        if (obj is long l)
            return l;

        throw new EvaluationException(
            $"Cannot evaluate operator + on a value of type {obj.GetType()}"
        );
    }

    public static object DoBoolNot(object obj)
    {
        return !CoerceToBool(obj);
    }

    public static object DoBitNot(object obj)
    {
        if (obj is bool b)
            return !b;
        if (obj is int i)
            return ~i;
        if (obj is long l)
            return ~l;
        if (obj is uint ui)
            return ~ui;
        if (obj is ulong ul)
            return ~ul;

        throw new EvaluationException($"Cannot use operator ~ on a value of type {obj.GetType()}");
    }

    public static double PromoteToDouble(object obj)
    {
        double? promoted = TryPromoteToDouble(obj);
        if (promoted == null)
            throw new EvaluationException(
                $"A value of type {obj?.GetType()?.Name ?? "null"} cannot be promoted to a double"
            );

        return (double)promoted;
    }

    static double? TryPromoteToDouble(object obj)
    {
        // Handle null input explicitly
        if (obj == null)
            return null;

        return obj switch
        {
            double d => d,
            float f => f,
            byte b => b,
            sbyte sb => sb,
            short s => s,
            ushort us => us,
            int i => i,
            uint ui => ui,
            long l => l,
            ulong ul => ul,
            string s => double.TryParse(s, out var sd) ? sd : null,
            _ => null,
        };
    }

    public static object DoMultiply(object a, object b)
    {
        return PromoteToDouble(a) * PromoteToDouble(b);
    }

    public static object DoDivide(object a, object b)
    {
        return PromoteToDouble(a) / PromoteToDouble(b);
    }

    public static object DoAdd(object a, object b)
    {
        if (a is string sa && b is string sb)
            return sa + sb;
        if (a is string a2)
            return a2 + b.ToString();
        if (b is string b2)
            return a.ToString() + b2;

        return PromoteToDouble(a) + PromoteToDouble(b);
    }

    public static object DoSub(object a, object b)
    {
        return PromoteToDouble(a) - PromoteToDouble(b);
    }

    public static object DoMod(object a, object b)
    {
        return PromoteToDouble(a) % PromoteToDouble(b);
    }

    public static object DoXor(object a, object b)
    {
        return CoerceToBool(a) ^ CoerceToBool(b);
    }

    public static int DoCompareTo(object a, object b)
    {
        if (a.GetType() == b.GetType())
        {
            if (a is IComparable ca)
                return ca.CompareTo(b);
        }

        var da = TryPromoteToDouble(a);
        var db = TryPromoteToDouble(b);
        if (da != null && db != null)
            return ((double)da).CompareTo((double)db);

        throw new EvaluationException(
            $"cannot compare values of type {a.GetType().Name} and {b.GetType().Name}"
        );
    }

    public static bool DoEquals(Type t, string name)
    {
        return t.Name == name;
    }

    public static bool DoEquals(string name, Type t)
    {
        return DoEquals(t, name);
    }

    public static bool DoEquals(Type l, Type r)
    {
        return l == r;
    }

    public static bool DoEquals(bool l, object r)
    {
        if (r is bool b)
            return l == b;

        if (r is string s)
        {
            if (l)
                return EqualsIgnoreCase("true", s);
            else
                return EqualsIgnoreCase("false", s);
        }

        return false;
    }

    public static bool DoEquals(object l, bool r)
    {
        return DoEquals(r, l);
    }

    public static bool DoEquals(object a, object b)
    {
        if (a == null || b == null)
            return (a == null) == (b == null);

        if (a.Equals(b))
            return true;

        var aType = a.GetType();
        var bType = b.GetType();

        // In this case we just believe a.Equals(b)
        if (aType == bType)
            return false;

        // We consider enums to be equal to the string value of their variants.
        if (aType.IsEnum && bType == typeof(string))
            return a.ToString().Equals(b);
        if (bType.IsEnum && aType == typeof(string))
            return b.ToString().Equals(a);

        if (aType == typeof(bool) && bType == typeof(string))
            return EqualsIgnoreCase(a.ToString(), (string)b);
        if (bType == typeof(bool) && aType == typeof(string))
            return EqualsIgnoreCase((string)a, b.ToString());

        if (a is Type ta && b is string sb)
            return DoEquals(ta, sb);
        if (b is Type tb && a is string sa)
            return DoEquals(sa, tb);

        var da = TryPromoteToDouble(a);
        var db = TryPromoteToDouble(b);

        if (da != null && db != null)
            return (double)da == (double)db;

        return a.ToString() == b.ToString();
    }

    public static bool CoerceToBool(object o)
    {
        if (o is bool b)
            return b;

        if (o is string s)
            return CoerceToBool(s);

        return o != null;
    }

    public static bool CoerceToBool(string s)
    {
        if (s is null)
            return false;
        if (string.Equals(s, "true", StringComparison.OrdinalIgnoreCase))
            return true;
        if (string.Equals(s, "false", StringComparison.OrdinalIgnoreCase))
            return false;
        return true;
    }

    public static T CoerceToEnum<T>(object o)
    {
        // If already the correct type, return as-is
        if (o is T t)
            return t;

        if (o is string s)
            return (T)Enum.Parse(typeof(T), s);

        throw new EvaluationException(
            $"cannot convert type `{o.GetType().Name}` to enum `{typeof(T).Name}`"
        );
    }

    public static T CoerceToTarget<T>(object o)
    {
        if (typeof(T).IsValueType)
        {
            var inner = Nullable.GetUnderlyingType(typeof(T));
            var target = typeof(T);
            if (inner != null)
            {
                if (o == null)
                    return default;

                target = inner;
            }
            else
            {
                if (o == null)
                    throw new NullValueException();
            }

            var type = o.GetType();

            if (target == type)
            {
                if (inner == null)
                    return (T)o;

                return (T)Activator.CreateInstance(typeof(T), [o]);
            }

            if (target == typeof(bool))
                return (T)(object)CoerceToBool(o);

            if (IsNumericType(target))
            {
                double? nval = TryPromoteToDouble(o);
                if (inner != null && nval is null)
                    return default;

                if (nval is null)
                    throw new NullValueException();

                double val = (double)nval;
                return (T)Convert.ChangeType(val, target);
            }

            if (target.IsEnum)
            {
                if (o is string s)
                    return (T)Enum.Parse(target, s);
            }
        }
        else
        {
            if (o == null)
                return default;
        }

        try
        {
            return (T)o;
        }
        catch (InvalidCastException e)
        {
            throw new EvaluationException(
                $"Cannot cast a value of type `{o?.GetType()?.Name ?? "null"}` to type `{typeof(T).Name}`",
                e
            );
        }
    }

    static bool EqualsIgnoreCase(string a, string b)
    {
        return MemoryExtensions.Equals(a.AsSpan(), b.AsSpan(), StringComparison.OrdinalIgnoreCase);
    }
}
