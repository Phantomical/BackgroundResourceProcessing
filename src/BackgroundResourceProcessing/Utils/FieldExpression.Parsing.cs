using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace BackgroundResourceProcessing.Utils;

public class EvaluationException(string message) : Exception(message) { }

public class CompilationException(string message) : Exception(message) { }

internal partial struct FieldExpression
{
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    // Static helper methods used to actually implement semantics
    internal static class Methods
    {
        const BindingFlags Flags =
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

        public static object DoFieldAccess(object obj, string member)
        {
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

        public static object DoMethodInvoke(object obj, string method, params object[] parameters)
        {
            var type = obj.GetType();
            Type[] ptypes = [.. parameters.Select(p => p.GetType())];
            try
            {
                var member = type.GetMethod(method, Flags, null, ptypes, []);
                return member.Invoke(obj, parameters);
            }
            catch (Exception e)
            {
                LogUtil.Warn($"Invoking method {type.Name}.{method} threw an exception: {e}");
                return null;
            }
        }

        public static object DoIndexAccess(object obj, object index)
        {
            if (obj == null || index == null)
                return null;

            var type = obj.GetType();

            try
            {
                var indexers = type.GetProperties(Flags)
                    .Where(prop => prop.GetIndexParameters().Length == 1);
                var indexType = index.GetType();

                var exactMatch = indexers
                    .Where(prop => prop.GetIndexParameters()[0].ParameterType == indexType)
                    .FirstOrDefault();

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

        public static Type DoGetType(object obj)
        {
            return obj.GetType();
        }

        public static object DoUnaryMinus(object obj)
        {
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

            throw new EvaluationException(
                $"Cannot use operator ~ on a value of type {obj.GetType()}"
            );
        }

        public static double PromoteToDouble(object obj)
        {
            double? promoted = TryPromoteToDouble(obj);
            if (promoted == null)
                throw new EvaluationException(
                    $"A value of type {obj.GetType()} cannot be promoted to a double"
                );

            return (double)promoted;
        }

        static double? TryPromoteToDouble(object obj)
        {
            return obj switch
            {
                double d => d,
                float f => f,
                byte b => b,
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
            {
                if (string.Equals(s, "true", StringComparison.OrdinalIgnoreCase))
                    return true;
                if (string.Equals(s, "false", StringComparison.OrdinalIgnoreCase))
                    return false;
            }

            return o != null;
        }

        public static T CoerceToEnum<T>(object o)
        {
            if (o is string s)
                return (T)Enum.Parse(typeof(T), s);

            throw new EvaluationException(
                $"cannot convert type `{o.GetType().Name}` to enum `{typeof(T).Name}`"
            );
        }

        public static T CoerceToTarget<T>(object o)
        {
            if (typeof(T) == typeof(double))
            {
                double? value = TryPromoteToDouble(o);
                if (value != null)
                    return (T)(object)(double)value;

                throw new EvaluationException(
                    $"cannot convert a value of type `{o.GetType().Name}` to double"
                );
            }

            if (typeof(T) == typeof(float))
            {
                double? value = TryPromoteToDouble(o);
                if (value != null)
                    return (T)(object)(float)(double)value;

                throw new EvaluationException(
                    $"cannot convert a value of type `{o.GetType().Name}` to float"
                );
            }

            if (typeof(T) == typeof(bool))
                return (T)(object)CoerceToBool(o);

            if (typeof(T).IsEnum)
            {
                if (o is string s)
                    return (T)Enum.Parse(typeof(T), s);
            }

            return (T)o;
        }

        static bool EqualsIgnoreCase(string a, string b)
        {
            return MemoryExtensions.Equals(
                a.AsSpan(),
                b.AsSpan(),
                StringComparison.OrdinalIgnoreCase
            );
        }
    }

    internal ref struct Parser(Lexer lexer, ConfigNode node, Type target)
    {
        Lexer lexer = lexer;
        readonly ConfigNode node = node;
        readonly Type target = target;
        readonly ParameterExpression module = Expression.Parameter(target, "module");

        readonly Token Current => lexer.Current;

        public Func<PartModule, T> Parse<T>()
        {
            var param = module;
            var block = ParseExpression();
            if (target != typeof(PartModule))
            {
                param = Expression.Parameter(typeof(PartModule), "moduleParam");
                block = Expression.Block(
                    [module],
                    Expression.Assign(module, Expression.Convert(param, target)),
                    CoerceToTarget<T>(block)
                );
            }
            else
            {
                block = CoerceToTarget<T>(block);
            }

            var expr = Expression.Lambda<Func<PartModule, T>>(block, [param]);

            if (Current != TokenKind.EOF)
                throw RenderError($"unexpected token `{Current.ToString()}`");

            return expr.Compile();
        }

        Expression ParseExpression()
        {
            return ParseNullCoalesceExpression();
        }

        Expression ParseNullCoalesceExpression()
        {
            Expression expr = ParseBoolOrExpression();

            while (true)
            {
                switch (Current.kind)
                {
                    case TokenKind.OP_NULL_COALESCE:
                        ExpectToken(TokenKind.OP_NULL_COALESCE);
                        var rhs = ParseBoolOrExpression();
                        expr = BuildNullCoalesce(expr, rhs);

                        break;

                    default:
                        return expr;
                }
            }
        }

        Expression ParseBoolOrExpression()
        {
            Expression expr = ParseBoolAndExpression();

            while (true)
            {
                Expression lhs;
                switch (Current.kind)
                {
                    case TokenKind.OP_BOOL_OR:
                        ExpectToken(TokenKind.OP_BOOL_OR);
                        lhs = ParseBoolAndExpression();
                        expr = Expression.OrElse(CoerceToBool(expr), CoerceToBool(lhs));
                        break;

                    default:
                        return expr;
                }
            }
        }

        Expression ParseBoolAndExpression()
        {
            Expression expr = ParseEqualityExpression();

            while (true)
            {
                Expression lhs;
                switch (Current.kind)
                {
                    case TokenKind.OP_BOOL_AND:
                        ExpectToken(TokenKind.OP_BOOL_AND);
                        lhs = ParseEqualityExpression();
                        expr = Expression.AndAlso(CoerceToBool(expr), CoerceToBool(lhs));
                        break;

                    default:
                        return expr;
                }
            }
        }

        Expression ParseEqualityExpression()
        {
            Expression expr = ParseAdditiveExpression();

            while (true)
            {
                Expression lhs;
                switch (Current.kind)
                {
                    case TokenKind.OP_EQ:
                        ExpectToken(TokenKind.OP_EQ);
                        lhs = ParseRelationalExpression();
                        expr = BuildEquality(expr, lhs);
                        break;

                    case TokenKind.OP_NE:
                        ExpectToken(TokenKind.OP_NE);
                        lhs = ParseRelationalExpression();
                        expr = Expression.Not(BuildEquality(expr, lhs));
                        break;

                    default:
                        return expr;
                }
            }
        }

        Expression ParseRelationalExpression()
        {
            Expression expr = ParseXorExpression();

            while (true)
            {
                Expression lhs;
                switch (Current.kind)
                {
                    case TokenKind.OP_LT:
                        ExpectToken(TokenKind.OP_LT);
                        lhs = ParseXorExpression();
                        expr = Expression.LessThan(
                            CallOverloadedMethod(nameof(Methods.DoCompareTo), expr, lhs),
                            Expression.Constant(0)
                        );
                        break;

                    case TokenKind.OP_LE:
                        ExpectToken(TokenKind.OP_LE);
                        lhs = ParseXorExpression();
                        expr = Expression.LessThanOrEqual(
                            CallOverloadedMethod(nameof(Methods.DoCompareTo), expr, lhs),
                            Expression.Constant(0)
                        );
                        break;

                    case TokenKind.OP_GT:
                        ExpectToken(TokenKind.OP_GT);
                        lhs = ParseXorExpression();
                        expr = Expression.GreaterThan(
                            CallOverloadedMethod(nameof(Methods.DoCompareTo), expr, lhs),
                            Expression.Constant(0)
                        );
                        break;

                    case TokenKind.OP_GE:
                        ExpectToken(TokenKind.OP_GE);
                        lhs = ParseXorExpression();
                        expr = Expression.GreaterThanOrEqual(
                            CallOverloadedMethod(nameof(Methods.DoCompareTo), expr, lhs),
                            Expression.Constant(0)
                        );
                        break;

                    default:
                        return expr;
                }
            }
        }

        Expression ParseXorExpression()
        {
            Expression expr = ParseAdditiveExpression();

            while (true)
            {
                Expression lhs;
                switch (Current.kind)
                {
                    case TokenKind.OP_XOR:
                        ExpectToken(TokenKind.OP_XOR);
                        lhs = ParseAdditiveExpression();
                        expr = Expression.ExclusiveOr(CoerceToBool(expr), CoerceToBool(lhs));
                        break;

                    default:
                        return expr;
                }
            }
        }

        Expression ParseAdditiveExpression()
        {
            Expression expr = ParseMultiplicativeExpression();

            while (true)
            {
                Expression lhs;
                switch (Current.kind)
                {
                    case TokenKind.OP_PLUS:
                        ExpectToken(TokenKind.OP_PLUS);
                        lhs = ParseMultiplicativeExpression();
                        expr = Expression.Add(CoerceToDouble(expr), CoerceToDouble(lhs));
                        break;

                    case TokenKind.OP_MINUS:
                        ExpectToken(TokenKind.OP_MINUS);
                        lhs = ParseMultiplicativeExpression();
                        expr = Expression.Subtract(CoerceToDouble(expr), CoerceToDouble(lhs));
                        break;

                    default:
                        return expr;
                }
            }
        }

        Expression ParseMultiplicativeExpression()
        {
            Expression expr = ParseUnaryExpression();

            while (true)
            {
                Expression lhs;
                switch (Current.kind)
                {
                    case TokenKind.OP_MULTIPLY:
                        ExpectToken(TokenKind.OP_MULTIPLY);
                        lhs = ParseUnaryExpression();
                        expr = Expression.Multiply(CoerceToDouble(expr), CoerceToDouble(lhs));
                        break;

                    case TokenKind.OP_DIVIDE:
                        ExpectToken(TokenKind.OP_DIVIDE);
                        lhs = ParseUnaryExpression();
                        expr = Expression.Divide(CoerceToDouble(expr), CoerceToDouble(lhs));
                        break;

                    case TokenKind.OP_FIELD_ACCESS:
                        ExpectToken(TokenKind.OP_FIELD_ACCESS);
                        lhs = ParseUnaryExpression();
                        expr = Expression.Modulo(CoerceToDouble(expr), CoerceToDouble(lhs));
                        break;

                    default:
                        return expr;
                }
            }
        }

        Expression ParseUnaryExpression()
        {
            switch (Current.kind)
            {
                case TokenKind.OP_MINUS:
                    ExpectToken(TokenKind.OP_MINUS);
                    return Expression.Negate(CoerceToDouble(ParseUnaryExpression()));

                case TokenKind.OP_PLUS:
                    ExpectToken(TokenKind.OP_PLUS);
                    return CoerceToDouble(ParseUnaryExpression());

                case TokenKind.OP_BOOL_NOT:
                    ExpectToken(TokenKind.OP_BOOL_NOT);
                    return Expression.Not(CoerceToBool(ParseUnaryExpression()));

                default:
                    return ParseAccessExpression();
            }
        }

        Expression ParseAccessExpression()
        {
            Expression obj = ParseValueExpression();

            while (true)
            {
                if (Current == TokenKind.OP_DOT)
                    obj = ParseNestedFieldAccess(obj);
                else if (Current == TokenKind.LBRACKET)
                    obj = ParseIndexAccess(obj);
                else
                    break;
            }

            return obj;
        }

        Expression ParseValueExpression()
        {
            switch (Current.kind)
            {
                case TokenKind.LPAREN:
                    ExpectToken(TokenKind.LPAREN);
                    var expr = ParseExpression();
                    ExpectToken(TokenKind.RPAREN);
                    return expr;

                case TokenKind.OP_FIELD_ACCESS:
                    return ParseBaseFieldAccess();

                case TokenKind.OP_CONFIG_ACCESS:
                    return ParseConfigAccess();

                case TokenKind.OP_BUILTIN_ACCESS:
                    return ParseBuiltinAccess();

                case TokenKind.TRUE:
                    lexer.MoveNext();
                    return Expression.Constant(true);

                case TokenKind.FALSE:
                    lexer.MoveNext();
                    return Expression.Constant(false);

                case TokenKind.NULL:
                    lexer.MoveNext();
                    return Expression.Constant(null);

                case TokenKind.NUMBER:
                    return ParseNumber();

                case TokenKind.STRING:
                    return ParseString();

                case TokenKind.TYPEOF:
                    return ParseTypeof();

                case TokenKind.IDENT:
                    var token = Current;
                    lexer.MoveNext();
                    return Expression.Constant(token.ToString());

                default:
                    throw RenderError($"unexpected token `{Current.ToString()}`");
            }
        }

        Expression ParseNumber()
        {
            var token = ExpectToken(TokenKind.NUMBER);
            var text = token.ToString();
            if (!double.TryParse(text, out var value))
                throw RenderError($"invalid number literal `{text}`");

            return Expression.Constant(value);
        }

        Expression ParseString()
        {
            var token = ExpectToken(TokenKind.STRING);
            var content = token.text.Slice(1, token.text.Length - 2);

            if (!content.Contains("\\", StringComparison.Ordinal))
                return Expression.Constant(content.ToString());

            string s = "";

            while (!content.IsEmpty)
            {
                int i = 0;
                for (; i < content.Length; ++i)
                {
                    if (content[i] == '\\')
                        break;
                }

                s += content.Slice(0, i).ToString();
                content = content.Slice(i);

                if (!content.IsEmpty)
                {
                    s += content[1];
                    content = content.Slice(2);
                }
            }

            return Expression.Constant(s);
        }

        Expression ParseTypeof()
        {
            ExpectToken(TokenKind.TYPEOF);
            ExpectToken(TokenKind.LPAREN);
            Expression obj = ParseExpression();
            ExpectToken(TokenKind.RPAREN);

            return CallMethod(() => Methods.DoGetType(null), obj);
        }

        Expression ParseFieldAccess()
        {
            Expression obj = ParseBaseFieldAccess();

            while (true)
            {
                if (Current == TokenKind.OP_DOT)
                    obj = ParseNestedFieldAccess(obj);
                else if (Current == TokenKind.LBRACKET)
                    obj = ParseIndexAccess(obj);
                else
                    break;
            }

            return obj;
        }

        Expression ParseBaseFieldAccess()
        {
            ExpectToken(TokenKind.OP_FIELD_ACCESS);
            switch (Current.kind)
            {
                // We allow true and false to be used as field names in this case
                case TokenKind.IDENT:
                case TokenKind.TRUE:
                case TokenKind.FALSE:
                    var field = Current;
                    lexer.MoveNext();

                    return BuildFieldAccess(module, field.ToString());

                default:
                    return module;
            }
        }

        Expression ParseNestedFieldAccess(Expression obj)
        {
            ExpectToken(TokenKind.OP_DOT);
            var field = ExpectToken(TokenKind.IDENT);

            if (Current == TokenKind.LPAREN)
            {
                List<Expression> args = [];

                ExpectToken(TokenKind.LPAREN);
                while (true)
                {
                    if (Current == TokenKind.RPAREN)
                        break;

                    args.Add(ParseExpression());

                    if (Current == TokenKind.COMMA)
                        lexer.MoveNext();
                }
                ExpectToken(TokenKind.RPAREN);

                return BuildMethodCall(obj, field.ToString(), args);
            }
            else
            {
                return BuildFieldAccess(obj, field.ToString());
            }
        }

        Expression ParseIndexAccess(Expression obj)
        {
            ExpectToken(TokenKind.LBRACKET);
            var index = ParseExpression();
            ExpectToken(TokenKind.RBRACKET);

            return BuildIndexAccess(obj, index);
        }

        Expression ParseConfigAccess()
        {
            ExpectToken(TokenKind.OP_CONFIG_ACCESS);
            var field = ExpectToken(TokenKind.IDENT);

            string value = null;
            node.TryGetValue(field.ToString(), ref value);

            return Expression.Constant(value);
        }

        Expression ParseBuiltinAccess()
        {
            ExpectToken(TokenKind.OP_BUILTIN_ACCESS);

            var builtins = Expression.Constant(Builtins.Instance);
            switch (Current.kind)
            {
                case TokenKind.IDENT:
                    var field = Current;
                    lexer.MoveNext();

                    return BuildFieldAccess(builtins, field.ToString());

                default:
                    return builtins;
            }
        }

        static MethodInfo GetMethodInfo(Expression<Action> expr) =>
            GetMethodInfo((LambdaExpression)expr);

        static MethodInfo GetMethodInfo<R>(Expression<Func<R>> expr) =>
            GetMethodInfo((LambdaExpression)expr);

        static MethodInfo GetMethodInfo(LambdaExpression expr)
        {
            if (expr.Body is not MethodCallExpression call)
                throw new ArgumentException("Expression should be a method call expression only");

            return call.Method;
        }

        static Expression CallMethod<R>(Expression<Func<R>> expr, params Expression[] ps) =>
            CallMethod(GetMethodInfo(expr), ps);

        static Expression CallMethod(LambdaExpression expr, params Expression[] ps) =>
            CallMethod(GetMethodInfo(expr), ps);

        static Expression CallMethod(MethodInfo method, params Expression[] exprs)
        {
            var parameters = method.GetParameters();
            if (parameters.Length < exprs.Length)
                throw new ArgumentException(
                    "Number of parameters did not match the number of parameters on the function"
                );

            for (int i = 0; i < exprs.Length; ++i)
            {
                var param = parameters[i];
                if (param.ParameterType != exprs[i].Type)
                    exprs[i] = Expression.Convert(exprs[i], param.ParameterType);
            }

            return Expression.Call(method, exprs);
        }

        static Expression CallOverloadedMethod(string name, Expression lhs, Expression rhs)
        {
            const BindingFlags Flags =
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

            var lt = lhs.Type;
            var rt = rhs.Type;
            var mt = typeof(Methods);

            var methods =
                from m in mt.GetMethods(Flags)
                where m.Name == name
                where ParametersCompatibleWith(m.GetParameters(), [lt, rt])
                select m;
            var method = methods.FirstOrDefault();

            if (method == null)
                throw new Exception(
                    $"No overload of {name} matches parameters ({lt.Name}, {rt.Name})"
                );

            return CallMethod(method, lhs, rhs);
        }

        readonly Expression TryBuildFieldAccess(Expression obj, string name)
        {
            var type = obj.Type;

            if (IsNullableT(type, out var param))
            {
                var variable = Expression.Variable(type);
                var result = BuildFieldAccess(Expression.Property(variable, "Value"), name);
                var nullty = GetNullableType(result.Type);

                if (nullty != result.Type)
                    result = Expression.Convert(result, nullty);

                return Expression.Block(
                    [variable],
                    Expression.Assign(variable, obj),
                    Condition(IsNotNull(variable), result, Expression.Constant(null, nullty))
                );
            }

            var field = type.GetField(name, Flags);
            if (field != null)
                return NullableAccess(obj, obj => Expression.Field(obj, field));

            var property = type.GetProperty(name, Flags);
            if (property != null)
            {
                if (!property.CanRead)
                    throw RenderError($"property {type.Name}.{property.Name} is not readable");

                return NullableAccess(obj, obj => Expression.Property(obj, property));
            }

            return null;
        }

        readonly Expression BuildFieldAccess(Expression obj, string name)
        {
            var access = TryBuildFieldAccess(obj, name);
            if (access != null)
                return access;

            return CallMethod(
                () => Methods.DoFieldAccess(null, null),
                obj,
                Expression.Constant(name)
            );
        }

        readonly Expression BuildIndexAccess(Expression obj, Expression index)
        {
            var indexType = index.Type;
            var type = obj.Type;

            var indexers = type.GetProperties(Flags)
                .Where(prop => prop.GetIndexParameters().Length == 1);

            foreach (var indexer in indexers)
            {
                var param = indexer.GetIndexParameters()[0];
                if (param.ParameterType != indexType)
                    continue;

                return NullableAccess(obj, obj => Expression.MakeIndex(obj, indexer, [index]));
            }

            if (
                indexType == typeof(int)
                || indexType == typeof(double)
                || indexType == typeof(float)
                || indexType == typeof(uint)
            )
            {
                Expression casted = null;
                if (indexType == typeof(double))
                    casted = Expression.Convert(index, typeof(int));
                else if (indexType == typeof(float))
                    casted = Expression.Convert(index, typeof(int));
                else if (indexType == typeof(uint))
                    casted = Expression.Convert(index, typeof(int));
                else
                    casted = index;

                if (type.IsArray)
                    return NullableAccess(obj, obj => Expression.ArrayIndex(obj, casted));

                foreach (var indexer in indexers)
                {
                    var param = indexer.GetIndexParameters()[0];
                    if (param.ParameterType != typeof(int))
                        continue;

                    return NullableAccess(
                        obj,
                        (obj) => Expression.MakeIndex(obj, indexer, [casted])
                    );
                }
            }

            if (index is ConstantExpression constant)
            {
                if (constant.Value is string s)
                {
                    var access = TryBuildFieldAccess(obj, s);
                    if (access != null)
                        return access;
                }
            }

            return CallMethod(() => Methods.DoIndexAccess(null, null), obj, index);
        }

        readonly Expression BuildNullCoalesce(Expression lhs, Expression rhs)
        {
            var lt = lhs.Type;
            var rt = rhs.Type;

            if (lt.IsValueType)
            {
                if (!lt.IsGenericType)
                    return lhs;

                var def = lt.GetGenericTypeDefinition();
                if (def != typeof(Nullable<>))
                    return lhs;

                var param = lt.GetGenericArguments()[0];
                if (param == rt)
                {
                    var p = Expression.Parameter(lt);
                    return Expression.Block(
                        [p],
                        Expression.Assign(p, lhs),
                        Condition(IsNotNull(p), Expression.Convert(p, param), rhs)
                    );
                }
            }

            if (lt == rt)
            {
                var p = Expression.Parameter(lhs.Type);
                return Expression.Block(
                    [p],
                    [Expression.Assign(p, lhs), Condition(IsNotNull(p), p, rhs)]
                );
            }

            lhs = CoerceToObject(lhs);
            rhs = CoerceToObject(rhs);

            {
                var p = Expression.Parameter(lhs.Type);
                return Expression.Block(
                    [p],
                    [Expression.Assign(p, lhs), Condition(IsNotNull(lhs), p, rhs)]
                );
            }
        }

        readonly Expression BuildEquality(Expression lhs, Expression rhs)
        {
            var lt = lhs.Type;
            var rt = rhs.Type;

            if (lt == rt)
                return Expression.Equal(lhs, rhs);

            if (lt == typeof(Type) && rt == typeof(string))
                return NullableAccess(
                    lhs,
                    rhs,
                    (lhs, rhs) => Expression.Equal(Expression.Property(lhs, "Name"), rhs)
                );
            if (lt == typeof(string) && rt == typeof(Type))
                return NullableAccess(
                    lhs,
                    rhs,
                    (lhs, rhs) => Expression.Equal(lhs, Expression.Property(rhs, "Name"))
                );

            return CallOverloadedMethod(nameof(Methods.DoEquals), lhs, rhs);
        }

        readonly Expression BuildMethodCall(
            Expression base_,
            string method,
            IEnumerable<Expression> args
        )
        {
            var info = GetMethodInfo(() => Methods.DoMethodInvoke(null, null));

            return Expression.Call(
                info,
                [
                    CoerceToObject(base_),
                    Expression.Constant(method),
                    Expression.NewArrayInit(
                        typeof(object),
                        args.Select(arg => CoerceToObject(arg))
                    ),
                ]
            );
        }

        static bool ParametersCompatibleWith(ParameterInfo[] parameters, Type[] types)
        {
            if (types.Length > parameters.Length)
                return false;

            int i = 0;
            for (; i < types.Length; ++i)
            {
                if (!IsCompatibleWith(parameters[i], types[i]))
                    return false;
            }

            for (; i < parameters.Length; ++i)
            {
                if (!parameters[i].HasDefaultValue)
                    return false;
            }

            return true;
        }

        static bool IsCompatibleWith(ParameterInfo parameter, Type type)
        {
            if (parameter.ParameterType.IsAssignableFrom(type))
                return true;

            if (parameter.ParameterType != typeof(object))
                return false;

            return true;
        }

        static Expression CoerceToBool(Expression expr)
        {
            if (expr.Type == typeof(bool))
                return expr;

            if (expr.Type == typeof(bool?))
            {
                var variable = Expression.Parameter(expr.Type);
                return Expression.Block(
                    [variable],
                    Expression.Assign(variable, expr),
                    Expression.AndAlso(
                        Expression.Property(variable, "HasValue"),
                        Expression.Property(variable, "Value")
                    )
                );
            }

            return Expression.Call(GetMethodInfo(() => Methods.CoerceToBool(null)), expr);
        }

        static Expression CoerceToObject(Expression expr)
        {
            var et = expr.Type;
            if (et == typeof(object))
                return expr;

            if (et.IsValueType && et.IsGenericType)
            {
                var def = et.GetGenericTypeDefinition();
                if (def == typeof(Nullable<>))
                {
                    var param = et.GetGenericArguments()[0];
                    var variable = Expression.Variable(et);

                    return Expression.Block(
                        [variable],
                        Expression.Assign(variable, expr),
                        Condition(
                            IsNotNull(variable),
                            Expression.Convert(Expression.Convert(variable, param), typeof(object)),
                            Expression.Constant(null, typeof(object))
                        )
                    );
                }
            }

            return Expression.Convert(expr, typeof(object));
        }

        static Expression CoerceToDouble(Expression expr)
        {
            if (expr is ConstantExpression constant)
            {
                var value = constant.Value;
                if (value is double)
                    return expr;
                if (value is float f)
                    return Expression.Constant((double)f);
                if (value is byte b)
                    return Expression.Constant((double)b);
                if (value is short s)
                    return Expression.Constant((double)s);
                if (value is ushort us)
                    return Expression.Constant((double)us);
                if (value is int i)
                    return Expression.Constant((double)i);
                if (value is uint ui)
                    return Expression.Constant((double)ui);
                if (value is long l)
                    return Expression.Constant((double)l);
                if (value is ulong ul)
                    return Expression.Constant((double)ul);
                if (value is string str)
                {
                    if (double.TryParse(str, out var parsed))
                        return Expression.Constant(parsed);
                }
            }

            var et = expr.Type;
            if (et == typeof(double))
                return expr;
            if (
                et == typeof(float)
                || et == typeof(byte)
                || et == typeof(short)
                || et == typeof(ushort)
                || et == typeof(int)
                || et == typeof(uint)
                || et == typeof(long)
                || et == typeof(ulong)
            )
                return Expression.Convert(expr, typeof(double));

            return CallMethod(() => Methods.PromoteToDouble(null), expr);
        }

        static Expression CoerceToTarget<T>(Expression expr)
        {
            if (typeof(T).IsAssignableFrom(expr.Type))
                return expr;

            if (typeof(T) == typeof(bool))
                return CoerceToBool(expr);

            if (typeof(T).IsEnum)
                return CoerceToEnum<T>(expr);

            if (typeof(T) == typeof(double))
                return CoerceToDouble(expr);
            if (typeof(T) == typeof(float))
                return Expression.Convert(CoerceToDouble(expr), typeof(double));

            return CallMethod(() => Methods.CoerceToTarget<T>(null), expr);
        }

        static Expression CoerceToEnum<T>(Expression expr)
        {
            if (expr.Type == typeof(string))
                return Expression.Convert(
                    Expression.Call(
                        GetMethodInfo(() => Enum.Parse(null, null)),
                        Expression.Constant(typeof(T)),
                        expr
                    ),
                    typeof(T)
                );

            return CallMethod(() => Methods.CoerceToEnum<T>(null), expr);
        }

        static Type GetNullableType(Type type)
        {
            if (!type.IsValueType)
                return type;

            if (IsNullableT(type, out var _))
                return type;

            return typeof(Nullable<>).MakeGenericType(type);
        }

        static Expression NullableAccess(Expression obj, Func<Expression, Expression> access)
        {
            if (obj is ParameterExpression)
            {
                var result = access(obj);
                var nullty = GetNullableType(result.Type);

                if (nullty != result.Type)
                    result = Expression.Convert(result, nullty);

                return Condition(IsNotNull(obj), result, Expression.Constant(null, nullty));
            }
            else
            {
                var variable = Expression.Variable(obj.Type);
                var result = access(variable);
                var nullty = GetNullableType(result.Type);

                if (nullty != result.Type)
                    result = Expression.Convert(result, nullty);

                return Expression.Block(
                    [variable],
                    Expression.Assign(variable, obj),
                    Condition(IsNotNull(variable), result, Expression.Constant(null, nullty))
                );
            }
        }

        static Expression NullableAccess(
            Expression lhs,
            Expression rhs,
            Func<Expression, Expression, Expression> func
        )
        {
            ParameterExpression plhs = Expression.Variable(lhs.Type);
            ParameterExpression prhs = Expression.Variable(rhs.Type);

            if (lhs is ParameterExpression || lhs is ConstantExpression)
                plhs = null;
            if (rhs is ParameterExpression || rhs is ConstantExpression)
                prhs = null;

            var result = func(plhs ?? lhs, prhs ?? rhs);
            var nullty = GetNullableType(result.Type);

            if (nullty != result.Type)
                result = Expression.Convert(result, nullty);

            List<ParameterExpression> vars = [];
            List<Expression> exprs = [];

            if (plhs != null)
            {
                vars.Add(plhs);
                exprs.Add(Expression.Assign(plhs, lhs));
            }

            if (prhs != null)
            {
                vars.Add(prhs);
                exprs.Add(Expression.Assign(prhs, rhs));
            }

            exprs.Add(
                Condition(
                    Expression.AndAlso(IsNotNull(plhs ?? lhs), IsNotNull(prhs ?? rhs)),
                    result,
                    Expression.Constant(null, nullty)
                )
            );

            if (exprs.Count == 1)
                return exprs[0];
            return Expression.Block(vars, exprs);
        }

        static Expression IsNull(Expression expr)
        {
            if (expr is ConstantExpression constant)
                return Expression.Constant(constant.Value is null);

            var et = expr.Type;
            if (!et.IsValueType)
                return Expression.ReferenceEqual(expr, Expression.Constant(null));

            if (IsNullableT(expr.Type, out var _))
                return Expression.Not(Expression.Property(expr, "HasValue"));

            return Expression.Constant(false);
        }

        static Expression IsNotNull(Expression expr)
        {
            if (expr is ConstantExpression constant)
                return Expression.Constant(constant.Value is not null);

            var et = expr.Type;
            if (!et.IsValueType)
                return Expression.ReferenceNotEqual(expr, Expression.Constant(null));

            if (IsNullableT(et, out var _))
                return Expression.Property(expr, "HasValue");

            return Expression.Constant(true);
        }

        static bool IsNullableT(Type type, out Type param)
        {
            param = null;
            if (!type.IsValueType)
                return false;
            if (!type.IsGenericType)
                return false;

            var def = type.GetGenericTypeDefinition();
            if (def != typeof(Nullable<>))
                return false;

            param = type.GenericTypeArguments[0];
            return true;
        }

        static Expression Condition(Expression cond, Expression ifTrue, Expression ifFalse)
        {
            if (cond is ConstantExpression constant)
            {
                if (constant.Value is bool b)
                {
                    if (b)
                        return ifTrue;
                    return ifFalse;
                }
            }

            return Expression.Condition(cond, ifTrue, ifFalse);
        }

        Token ExpectToken(TokenKind kind)
        {
            var token = Current;
            if (token != kind)
                throw RenderError(
                    $"expected {Token.TokenKindDescription(kind)}, got `{Current.ToString()}` instead"
                );
            lexer.MoveNext();
            return token;
        }

        public readonly CompilationException RenderError(string message, ReadOnlySpan<char> span) =>
            lexer.RenderError(message, span);

        public readonly CompilationException RenderError(string message, Token token) =>
            RenderError(message, token.text);

        public readonly CompilationException RenderError(string message) =>
            RenderError(message, Current.text);
    }

    internal ref struct Lexer(ReadOnlySpan<char> text)
    {
        public readonly ReadOnlySpan<char> original = text;
        ReadOnlySpan<char> span = text;

        public Token Current = new(default, TokenKind.EOF);

        public void MoveNext()
        {
            Current = GetNext();
        }

        private Token GetNext()
        {
            span = span.TrimStart();

            if (span.IsEmpty)
                return new(span, TokenKind.EOF);

            // Do the length-2 operators first, since some contain the length-1
            // operators within themselves.
            if (span.StartsWith("=="))
                return TakeFirst(2, TokenKind.OP_EQ);
            if (span.StartsWith("!="))
                return TakeFirst(2, TokenKind.OP_NE);
            if (span.StartsWith("<="))
                return TakeFirst(2, TokenKind.OP_LE);
            if (span.StartsWith(">="))
                return TakeFirst(2, TokenKind.OP_GE);
            if (span.StartsWith("&&"))
                return TakeFirst(2, TokenKind.OP_BOOL_AND);
            if (span.StartsWith("||"))
                return TakeFirst(2, TokenKind.OP_BOOL_OR);
            if (span.StartsWith("??"))
                return TakeFirst(2, TokenKind.OP_NULL_COALESCE);

            switch (span[0])
            {
                case '<':
                    return TakeFirst(1, TokenKind.OP_LT);
                case '>':
                    return TakeFirst(1, TokenKind.OP_GT);
                case '+':
                    return TakeFirst(1, TokenKind.OP_PLUS);
                case '-':
                    return TakeFirst(1, TokenKind.OP_MINUS);
                case '*':
                    return TakeFirst(1, TokenKind.OP_MULTIPLY);
                case '/':
                    return TakeFirst(1, TokenKind.OP_DIVIDE);
                case '%':
                    return TakeFirst(1, TokenKind.OP_FIELD_ACCESS);
                case '!':
                    return TakeFirst(1, TokenKind.OP_BOOL_NOT);
                case '.':
                    return TakeFirst(1, TokenKind.OP_DOT);
                case '&':
                    return TakeFirst(1, TokenKind.OP_AND);
                case '|':
                    return TakeFirst(1, TokenKind.OP_OR);
                case '^':
                    return TakeFirst(1, TokenKind.OP_XOR);
                case '~':
                    return TakeFirst(1, TokenKind.OP_NOT);
                case '@':
                    return TakeFirst(1, TokenKind.OP_CONFIG_ACCESS);
                case '$':
                    return TakeFirst(1, TokenKind.OP_BUILTIN_ACCESS);
                case ',':
                    return TakeFirst(1, TokenKind.COMMA);
                case '(':
                    return TakeFirst(1, TokenKind.LPAREN);
                case ')':
                    return TakeFirst(1, TokenKind.RPAREN);
                case '[':
                    return TakeFirst(1, TokenKind.LBRACKET);
                case ']':
                    return TakeFirst(1, TokenKind.RBRACKET);

                default:
                    break;
            }

            var c = span[0];
            if (c == '"')
                return TakeString();
            if (char.IsDigit(c))
                return TakeNumber();
            if (char.IsLetter(c))
                return TakeIdent();

            throw RenderError($"unexpected token `{c}`");
        }

        Token TakeFirst(int n, TokenKind kind)
        {
            Token token = new(span.Slice(0, n), kind);
            span = span.Slice(n);
            return token;
        }

        Token TakeString()
        {
            for (int i = 1; ; i++)
            {
                if (i >= span.Length)
                    throw RenderError("unterminated string");

                if (span[i] == '\\')
                {
                    if (i + 1 >= span.Length)
                        continue;

                    i += 2;
                    continue;
                }

                if (span[i] == '"')
                    return TakeFirst(i + 1, TokenKind.STRING);
            }
        }

        Token TakeIdent()
        {
            int i = 0;
            for (; i < span.Length; ++i)
            {
                char c = span[i];
                if (char.IsLetterOrDigit(c) || c == '_')
                    continue;
                break;
            }

            var token = TakeFirst(i, TokenKind.IDENT);
            if (token == "null")
                return new(token.text, TokenKind.NULL);
            if (token == "typeof")
                return new(token.text, TokenKind.TYPEOF);
            if (token.EqualsIgnoreCase("true"))
                return new(token.text, TokenKind.TRUE);
            if (token.EqualsIgnoreCase("false"))
                return new(token.text, TokenKind.FALSE);
            return token;
        }

        Token TakeNumber()
        {
            int i = 0;
            while (i < span.Length && char.IsDigit(span[i]))
                ++i;

            if (i >= span.Length)
                goto DONE;

            if (span[i] == '.')
            {
                i += 1;

                while (i < span.Length && char.IsDigit(span[i]))
                    ++i;
            }

            if (i >= span.Length)
                goto DONE;

            if (span[i] == 'e' || span[i] == 'E')
            {
                i += 1;

                if (i >= span.Length)
                    throw RenderError(
                        $"invalid number literal, expected '+', '-', or a digit after `{span[i - 1]}`"
                    );

                if (span[i] == '-' || span[i] == '+')
                    i += 1;

                while (i < span.Length && char.IsDigit(span[i]))
                    ++i;
            }

            DONE:
            var slice = span.Slice(0, i);

            try
            {
                double.Parse(slice.ToString());
            }
            catch (Exception e)
            {
                throw RenderError($"invalid number literal: {e.Message}");
            }

            return TakeFirst(i, TokenKind.NUMBER);
        }

        private readonly int CalculateOffset(ReadOnlySpan<char> span)
        {
            if (!span.Overlaps(original))
                return -1;

            unsafe
            {
                fixed (char* start = &original[0])
                {
                    fixed (char* inner = &span[0])
                    {
                        return Math.Abs((int)(inner - start));
                    }
                }
            }
        }

        public readonly CompilationException RenderError(string message, ReadOnlySpan<char> span)
        {
            var offset = CalculateOffset(span);
            var s = original.ToString();

            if (offset >= 0)
            {
                offset = MathUtil.Clamp(offset, 0, s.Length);

                return new CompilationException(
                    $"{message}\n | {s}\n | {new string(' ', offset)}^"
                );
            }
            else
            {
                return new CompilationException($"{message}\n | {s}");
            }
        }

        public readonly CompilationException RenderError(string message, Token token) =>
            RenderError(message, token.text);

        public readonly CompilationException RenderError(string message) =>
            RenderError(message, Current.text);
    }

    internal enum TokenKind
    {
        EOF = 0,

        // ==
        OP_EQ,

        // !=
        OP_NE,

        // <
        OP_LT,

        // <=
        OP_LE,

        // >
        OP_GT,

        // >=
        OP_GE,

        // +
        OP_PLUS,

        // -
        OP_MINUS,

        // *
        OP_MULTIPLY,

        // /
        OP_DIVIDE,

        // %
        OP_FIELD_ACCESS,

        // .
        OP_DOT,

        // &&
        OP_BOOL_AND,

        // ||
        OP_BOOL_OR,

        // !
        OP_BOOL_NOT,

        // &
        OP_AND,

        // |
        OP_OR,

        // ^
        OP_XOR,

        // ~
        OP_NOT,

        // ??
        OP_NULL_COALESCE,

        // @
        OP_CONFIG_ACCESS,

        // $
        OP_BUILTIN_ACCESS,

        // ,
        COMMA,

        // (
        LPAREN,

        // )
        RPAREN,

        // [
        LBRACKET,

        // ]
        RBRACKET,

        // An identifier
        IDENT,

        // A number literal
        NUMBER,

        // The literal string true (case-insensitive)
        TRUE,

        // The literal string false (case-insensitive)
        FALSE,

        // The literal string null
        NULL,

        // typeof operator
        TYPEOF,

        // A string literal
        STRING,
    }

    internal ref struct Token(ReadOnlySpan<char> text, TokenKind kind)
    {
        public ReadOnlySpan<char> text = text;
        public TokenKind kind = kind;

        public readonly bool EqualsIgnoreCase(ReadOnlySpan<char> text) =>
            MemoryExtensions.Equals(this.text, text, StringComparison.OrdinalIgnoreCase);

        public static bool operator ==(Token token, string text)
        {
            return token == (ReadOnlySpan<char>)text;
        }

        public static bool operator !=(Token token, string text)
        {
            return !(token == text);
        }

        public static bool operator ==(Token token, ReadOnlySpan<char> text)
        {
            return MemoryExtensions.Equals(token.text, text, StringComparison.Ordinal);
        }

        public static bool operator !=(Token token, ReadOnlySpan<char> text)
        {
            return !(token == text);
        }

        public static bool operator ==(Token token, TokenKind kind)
        {
            return token.kind == kind;
        }

        public static bool operator !=(Token token, TokenKind kind)
        {
            return !(token == kind);
        }

        public static bool operator ==(Token a, Token b)
        {
            return a == b.text && a == b.kind;
        }

        public static bool operator !=(Token a, Token b)
        {
            return !(a == b);
        }

        public override readonly string ToString()
        {
            if (kind == TokenKind.EOF)
                return "<EOF>";
            return text.ToString();
        }

        public override readonly bool Equals(object obj)
        {
            return false;
        }

        public override int GetHashCode()
        {
            throw new NotImplementedException();
        }

        public static string TokenKindDescription(TokenKind kind)
        {
            return kind switch
            {
                TokenKind.EOF => "<EOF>",
                TokenKind.OP_EQ => "'=='",
                TokenKind.OP_NE => "'!='",
                TokenKind.OP_LT => "'<'",
                TokenKind.OP_LE => "'<='",
                TokenKind.OP_GT => "'>'",
                TokenKind.OP_GE => "'>='",
                TokenKind.OP_PLUS => "'+'",
                TokenKind.OP_MINUS => "'-'",
                TokenKind.OP_MULTIPLY => "'*'",
                TokenKind.OP_DIVIDE => "'/'",
                TokenKind.OP_FIELD_ACCESS => "'%'",
                TokenKind.OP_DOT => "'.'",
                TokenKind.OP_BOOL_AND => "'&&'",
                TokenKind.OP_BOOL_OR => "'||'",
                TokenKind.OP_BOOL_NOT => "'!'",
                TokenKind.OP_NOT => "'~'",
                TokenKind.OP_AND => "'&'",
                TokenKind.OP_OR => "'|'",
                TokenKind.OP_XOR => "'^'",
                TokenKind.OP_NULL_COALESCE => "'??'",
                TokenKind.OP_CONFIG_ACCESS => "'@'",
                TokenKind.COMMA => "','",
                TokenKind.LPAREN => "'('",
                TokenKind.RPAREN => "')'",
                TokenKind.LBRACKET => "'['",
                TokenKind.RBRACKET => "']'",
                TokenKind.IDENT => "an ident",
                TokenKind.NUMBER => "a number",
                TokenKind.STRING => "a string",
                TokenKind.TRUE => "'true'",
                TokenKind.FALSE => "'false'",
                TokenKind.NULL => "'null'",
                TokenKind.TYPEOF => "'typeof'",
                _ => "<unknown>",
            };
        }
    }

    internal ref struct TypedExpression(Expression expr, Type type)
    {
        public Expression expr = expr;
        public Type type = type;
    }

    internal interface IExpressionPrinter
    {
        string ExpressionToString(Expression expr);
    }
}
