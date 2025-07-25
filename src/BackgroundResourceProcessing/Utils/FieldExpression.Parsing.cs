using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace BackgroundResourceProcessing.Utils;

public class EvaluationException(string message) : Exception(message) { }

public class CompilationException(string message) : Exception(message) { }

public readonly partial struct FieldExpression<T>
{
    // Static helper methods used to actually implement semantics
    static class Methods
    {
        const BindingFlags Flags =
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

        internal class Settings
        {
            internal static readonly Settings Instance = new();

            public SectionSettings this[string section]
            {
                get
                {
                    var parameters = HighLogic.CurrentGame?.Parameters;
                    if (parameters == null)
                        return null;

                    var selected = GetCustomParams(parameters)
                        .Values.Where(node => node.Section == section);

                    return new(selected);
                }
            }

            private Settings() { }

            private static readonly FieldInfo CustomParamsField = typeof(GameParameters).GetField(
                "customParams",
                Flags
            );

            private static Dictionary<Type, GameParameters.CustomParameterNode> GetCustomParams(
                GameParameters parameters
            )
            {
                return (Dictionary<Type, GameParameters.CustomParameterNode>)
                    CustomParamsField.GetValue(parameters);
            }
        };

        internal class SectionSettings(IEnumerable<GameParameters.CustomParameterNode> nodes)
        {
            internal GameParameters.CustomParameterNode this[string name] =>
                nodes
                    .Where(node => node.GetType().Name == name || node.Title == name)
                    .FirstOrDefault();
        }

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

        static double PromoteToDouble(object obj)
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

            return o != null;
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

    ref struct Parser(Lexer lexer, ConfigNode node)
    {
        Lexer lexer = lexer;
        readonly ConfigNode node = node;
        readonly ParameterExpression module = Expression.Parameter(typeof(PartModule), "module");

        readonly Token Current => lexer.Current;

        public Func<PartModule, object> Parse()
        {
            var expr = Expression.Lambda<Func<PartModule, object>>(
                CoerceToObject(ParseExpression()),
                [module]
            );

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
                        var lhs = Expression.Parameter(expr.Type);

                        expr = Expression.Block(
                            [lhs],
                            [
                                Expression.Assign(lhs, expr),
                                Expression.Condition(
                                    Expression.Equal(lhs, Expression.Constant(null)),
                                    lhs,
                                    rhs
                                ),
                            ]
                        );

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
                        expr = CallOverloadedMethod(nameof(Methods.DoEquals), expr, lhs);
                        break;

                    case TokenKind.OP_NE:
                        ExpectToken(TokenKind.OP_NE);
                        lhs = ParseRelationalExpression();
                        expr = Expression.Not(
                            CallOverloadedMethod(nameof(Methods.DoEquals), expr, lhs)
                        );
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
                        expr = CallMethod(
                            GetMethodInfo(() => Methods.DoXor(null, null)),
                            expr,
                            lhs
                        );
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
                        expr = CallMethod(
                            GetMethodInfo(() => Methods.DoAdd(null, null)),
                            expr,
                            lhs
                        );
                        break;

                    case TokenKind.OP_MINUS:
                        ExpectToken(TokenKind.OP_MINUS);
                        lhs = ParseMultiplicativeExpression();
                        expr = CallMethod(
                            GetMethodInfo(() => Methods.DoSub(null, null)),
                            expr,
                            lhs
                        );
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
                        expr = CallMethod(
                            GetMethodInfo(() => Methods.DoMultiply(null, null)),
                            expr,
                            lhs
                        );
                        break;

                    case TokenKind.OP_DIVIDE:
                        ExpectToken(TokenKind.OP_DIVIDE);
                        lhs = ParseUnaryExpression();
                        expr = CallMethod(
                            GetMethodInfo(() => Methods.DoDivide(null, null)),
                            expr,
                            lhs
                        );
                        break;

                    case TokenKind.OP_FIELD_ACCESS:
                        ExpectToken(TokenKind.OP_FIELD_ACCESS);
                        lhs = ParseUnaryExpression();
                        expr = CallMethod(
                            GetMethodInfo(() => Methods.DoMod(null, null)),
                            expr,
                            lhs
                        );
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
                    return CallMethod(
                        GetMethodInfo(() => Methods.DoUnaryMinus(null)),
                        ParseUnaryExpression()
                    );

                case TokenKind.OP_PLUS:
                    ExpectToken(TokenKind.OP_PLUS);
                    return CallMethod(
                        GetMethodInfo(() => Methods.DoUnaryPlus(null)),
                        ParseUnaryExpression()
                    );

                case TokenKind.OP_BOOL_NOT:
                    ExpectToken(TokenKind.OP_BOOL_NOT);
                    return CallMethod(
                        GetMethodInfo(() => Methods.DoBoolNot(null)),
                        ParseUnaryExpression()
                    );

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
                    if (Current.EqualsIgnoreCase("Settings"))
                    {
                        lexer.MoveNext();
                        return Expression.Constant(Methods.Settings.Instance);
                    }
                    else
                    {
                        var token = Current;
                        lexer.MoveNext();
                        return Expression.Constant(token.ToString());
                    }

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

                    return CallMethod(
                        GetMethodInfo(() => Methods.DoFieldAccess(null, null)),
                        module,
                        Expression.Constant(field.ToString())
                    );

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
                List<Expression> args = [obj, Expression.Constant(field.ToString())];

                while (true)
                {
                    args.Add(ParseExpression());

                    if (Current == TokenKind.COMMA)
                        lexer.MoveNext();
                    if (Current == TokenKind.RPAREN)
                        break;
                }

                lexer.MoveNext();

                return CallMethod(() => Methods.DoMethodInvoke(null, null), [.. args]);
            }
            else
            {
                return CallMethod(
                    () => Methods.DoFieldAccess(null, null),
                    obj,
                    Expression.Constant(field.ToString())
                );
            }
        }

        Expression ParseIndexAccess(Expression obj)
        {
            ExpectToken(TokenKind.LBRACKET);
            var index = ParseExpression();
            ExpectToken(TokenKind.RBRACKET);

            return Expression.Call(
                GetMethodInfo(() => Methods.DoIndexAccess(null, null)),
                obj,
                index
            );
        }

        Expression ParseConfigAccess()
        {
            ExpectToken(TokenKind.OP_CONFIG_ACCESS);
            var field = ExpectToken(TokenKind.IDENT);

            string value = null;
            node.TryGetValue(field.ToString(), ref value);

            return Expression.Constant(value);
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

            return Expression.Invoke(Expression.Constant((object)Methods.CoerceToBool), expr);
        }

        static Expression CoerceToObject(Expression expr)
        {
            if (expr.Type == typeof(object))
                return expr;

            return Expression.Convert(expr, typeof(object));
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

    ref struct Lexer(ReadOnlySpan<char> text)
    {
        readonly ReadOnlySpan<char> original = text;
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

                if (i >= span.Length || (span[i] != '-' && span[i] != '+'))
                    throw RenderError(
                        $"invalid number literal, expected '+' or '-' after `{span[i - 1]}`"
                    );

                i += 1;

                while (i < span.Length && char.IsDigit(span[i]))
                    ++i;
            }

            DONE:
            if (!double.TryParse(span.ToString(), out var _))
                throw RenderError("invalid number literal");

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

            if (offset < 0)
                offset = CalculateOffset(Current.text);
            if (offset < 0)
                offset = 0;

            var s = original.ToString();
            offset = MathUtil.Clamp(offset, 0, s.Length);

            return new CompilationException($"{message}\n | {s}\n | {new string(' ', offset)}^");
        }

        public readonly CompilationException RenderError(string message, Token token) =>
            RenderError(message, token.text);

        public readonly CompilationException RenderError(string message) =>
            RenderError(message, Current.text);
    }

    enum TokenKind
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

    ref struct Token(ReadOnlySpan<char> text, TokenKind kind)
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

    ref struct TypedExpression(Expression expr, Type type)
    {
        public Expression expr = expr;
        public Type type = type;
    }
}
