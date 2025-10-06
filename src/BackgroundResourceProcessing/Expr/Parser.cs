using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using KSPAchievements;
using Steamworks;
using UnityEngine;

namespace BackgroundResourceProcessing.Expr;

internal ref struct Parser(Lexer lexer, ConfigNode node, Type target)
{
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

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

    public Expression ParseFragment<T>(ParameterExpression param)
    {
        var block = Expression.Block(
            [module],
            Expression.Assign(module, Expression.Convert(param, target)),
            CoerceToTarget<T>(ParseExpression())
        );

        if (Current != TokenKind.EOF)
            throw RenderError($"unexpected token `{Current.ToString()}`");

        return block;
    }

    #region Parse AST
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
        Expression expr = ParseXorExpression();

        while (true)
        {
            Expression lhs;
            switch (Current.kind)
            {
                case TokenKind.OP_BOOL_AND:
                    ExpectToken(TokenKind.OP_BOOL_AND);
                    lhs = ParseXorExpression();
                    expr = Expression.AndAlso(CoerceToBool(expr), CoerceToBool(lhs));
                    break;

                default:
                    return expr;
            }
        }
    }

    Expression ParseEqualityExpression()
    {
        Expression expr = ParseRelationalExpression();

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
        Expression expr = ParseAdditiveExpression();

        while (true)
        {
            Expression lhs;
            switch (Current.kind)
            {
                case TokenKind.OP_LT:
                    ExpectToken(TokenKind.OP_LT);
                    lhs = ParseAdditiveExpression();
                    expr = Expression.LessThan(
                        CallOverloadedMethod(nameof(Methods.DoCompareTo), expr, lhs),
                        Expression.Constant(0)
                    );
                    break;

                case TokenKind.OP_LE:
                    ExpectToken(TokenKind.OP_LE);
                    lhs = ParseAdditiveExpression();
                    expr = Expression.LessThanOrEqual(
                        CallOverloadedMethod(nameof(Methods.DoCompareTo), expr, lhs),
                        Expression.Constant(0)
                    );
                    break;

                case TokenKind.OP_GT:
                    ExpectToken(TokenKind.OP_GT);
                    lhs = ParseAdditiveExpression();
                    expr = Expression.GreaterThan(
                        CallOverloadedMethod(nameof(Methods.DoCompareTo), expr, lhs),
                        Expression.Constant(0)
                    );
                    break;

                case TokenKind.OP_GE:
                    ExpectToken(TokenKind.OP_GE);
                    lhs = ParseAdditiveExpression();
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
        Expression expr = ParseEqualityExpression();

        while (true)
        {
            Expression lhs;
            switch (Current.kind)
            {
                case TokenKind.OP_XOR:
                    ExpectToken(TokenKind.OP_XOR);
                    lhs = ParseEqualityExpression();
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
            Expression rhs;
            switch (Current.kind)
            {
                case TokenKind.OP_PLUS:
                    ExpectToken(TokenKind.OP_PLUS);
                    rhs = ParseMultiplicativeExpression();
                    expr = MapNullable(expr, rhs, BuildBinaryAdd);
                    break;

                case TokenKind.OP_MINUS:
                    ExpectToken(TokenKind.OP_MINUS);
                    rhs = ParseMultiplicativeExpression();
                    expr = MapNullable(
                        expr,
                        rhs,
                        (lhs, rhs) =>
                            BuildBinaryArithOp(
                                lhs,
                                rhs,
                                (lhs, rhs) => lhs - rhs,
                                Expression.Subtract
                            )
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
            Expression rhs;
            switch (Current.kind)
            {
                case TokenKind.OP_MULTIPLY:
                    ExpectToken(TokenKind.OP_MULTIPLY);
                    rhs = ParseUnaryExpression();
                    expr = MapNullable(
                        expr,
                        rhs,
                        (lhs, rhs) =>
                            BuildBinaryArithOp(
                                lhs,
                                rhs,
                                (lhs, rhs) => lhs * rhs,
                                Expression.Multiply
                            )
                    );
                    break;

                case TokenKind.OP_DIVIDE:
                    ExpectToken(TokenKind.OP_DIVIDE);
                    rhs = ParseUnaryExpression();
                    expr = MapNullable(
                        expr,
                        rhs,
                        (lhs, rhs) =>
                            BuildBinaryArithOp(lhs, rhs, (lhs, rhs) => lhs / rhs, Expression.Divide)
                    );
                    break;

                case TokenKind.OP_FIELD_ACCESS:
                    ExpectToken(TokenKind.OP_FIELD_ACCESS);
                    rhs = ParseUnaryExpression();
                    expr = MapNullable(
                        expr,
                        rhs,
                        (lhs, rhs) =>
                            BuildBinaryArithOp(lhs, rhs, (lhs, rhs) => lhs % rhs, Expression.Modulo)
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
                return MapNullable(ParseUnaryExpression(), BuildUnaryMinus);

            case TokenKind.OP_PLUS:
                ExpectToken(TokenKind.OP_PLUS);
                return MapNullable(ParseUnaryExpression(), BuildUnaryPlus);

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

    ConstantExpression ParseNumber()
    {
        var token = ExpectToken(TokenKind.NUMBER);
        var text = token.ToString();
        if (!double.TryParse(text, out var value))
            throw RenderError($"invalid number literal `{text}`");

        return Expression.Constant(value);
    }

    ConstantExpression ParseString()
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

            if (content.IsEmpty)
                continue;

            s += content[1] switch
            {
                '"' => '"',
                '\'' => '\'',
                '\\' => '\\',
                '0' => '\0',
                'a' => '\a',
                'b' => '\b',
                'n' => '\n',
                'r' => '\r',
                't' => '\t',
                'v' => '\v',
                _ => throw RenderError($"Invalid character escape `\\{content[1]}`"),
            };
            content = content.Slice(2);
        }

        return Expression.Constant(s);
    }

    Expression ParseTypeof()
    {
        ExpectToken(TokenKind.TYPEOF);
        ExpectToken(TokenKind.LPAREN);
        Expression obj = ParseExpression();
        ExpectToken(TokenKind.RPAREN);

        var type = obj.GetType();
        if (type.IsValueType || type.IsSealed)
            return Expression.Constant(type, typeof(Type));

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

                    return BuildMethodCall(module, field.ToString(), args);
                }
                else
                {
                    return BuildFieldAccess(module, field.ToString());
                }

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

    ConstantExpression ParseConfigAccess()
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
    #endregion

    static readonly ConstantExpression KnownNull = Expression.Constant(null, typeof(object));

    #region Field Access
    readonly Expression BuildFieldAccess(Expression obj, string name)
    {
        // If we know that the expression is null already then we can just
        // constant fold that on up.
        if (obj is ConstantExpression c && c.Value is null)
            return KnownNull;

        Expression expr = TryBuildFieldAccess(obj, name);
        if (expr is not null)
            return expr;

        var type = obj.Type;

        // If we're working with a struct then we know we have the base type
        // and we can just return null.
        if (type.IsValueType)
            return KnownNull;

        return CallMethod(() => Methods.DoFieldAccess(null, null), obj, Expression.Constant(name));
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
    #endregion

    #region Index Access
    readonly Expression BuildIndexAccess(Expression obj, Expression index)
    {
        static bool FindMatchingIndexer(
            List<PropertyInfo> indexers,
            Type pty,
            out PropertyInfo indexer
        )
        {
            foreach (var item in indexers)
            {
                var arg = item.GetIndexParameters()[0];
                if (arg.ParameterType == pty)
                {
                    indexer = item;
                    return true;
                }
            }

            indexer = null;
            return false;
        }

        static Expression BuildIndexTryCatch(
            Expression obj,
            Expression index,
            Func<Expression, Expression, Expression> func
        )
        {
            var param = Expression.Parameter(index.Type);
            var result = NullableAccess(obj, index, func);
            return Expression.Block(
                [param],
                Expression.Assign(param, index),
                Expression.TryCatch(
                    result,
                    [Expression.Catch(typeof(Exception), Expression.Constant(null, result.Type))]
                )
            );
        }

        var indexType = index.Type;
        var type = obj.Type;

        if (IsConstantNull(obj))
            return KnownNull;

        var indexers = type.GetProperties(Flags)
            .Where(prop => prop.GetIndexParameters().Length == 1)
            .ToList();

        if (FindMatchingIndexer(indexers, indexType, out var indexer))
            return BuildIndexTryCatch(
                obj,
                index,
                (obj, index) => Expression.MakeIndex(obj, indexer, [index])
            );

        if (IsNumericType(indexType))
        {
            if (type.IsArray)
                return BuildIndexTryCatch(obj, index, BuildArrayIndexAccess);

            if (FindMatchingIndexer(indexers, typeof(int), out indexer))
                return BuildIndexTryCatch(
                    obj,
                    index,
                    (obj, index) =>
                        Expression.MakeIndex(obj, indexer, [Expression.Convert(index, typeof(int))])
                );
        }
        else
        {
            if (index is ConstantExpression c && c.Value is string s)
            {
                var access = TryBuildFieldAccess(obj, s);
                if (access != null)
                    return access;
            }
        }

        return CallMethod(() => Methods.DoIndexAccess(null, null), obj, index);
    }

    static MethodCallExpression BuildArrayIndexAccess(Expression array, Expression index)
    {
        index = Expression.Convert(index, typeof(int));

        var type = array.Type;
        if (!type.IsArray)
            throw new ArgumentException("target was not an array", nameof(array));

        var elem = type.GetElementType();
        if (!elem.IsValueType)
        {
            var method = GetMethodInfo(() => Methods.DoArrayIndexAccessClass<object>(null, 0))
                .GetGenericMethodDefinition()
                .MakeGenericMethod(elem);

            return CallMethod(method, array, index);
        }
        else if (IsNullableT(elem, out var inner))
        {
            var method = GetMethodInfo(() => Methods.DoArrayIndexAccessNullableStruct<int>(null, 0))
                .GetGenericMethodDefinition()
                .MakeGenericMethod(inner);

            return CallMethod(method, array, index);
        }
        else
        {
            var method = GetMethodInfo(() => Methods.DoArrayIndexAccessStruct<int>(null, 0))
                .GetGenericMethodDefinition()
                .MakeGenericMethod(elem);

            return CallMethod(method, array, index);
        }
    }
    #endregion

    #region Arithmetic
    static Expression BuildUnaryMinus(Expression value)
    {
        var promoted = CoerceToDouble(value);
        if (promoted is ConstantExpression c)
        {
            if (c.Value is double d)
                return Expression.Constant(-d);
        }

        return Expression.Negate(promoted);
    }

    static Expression BuildUnaryPlus(Expression value)
    {
        return CoerceToDouble(value);
    }

    static Expression BuildBinaryAdd(Expression lhs, Expression rhs)
    {
        static Expression BuildConcat(Expression lhs, Expression rhs) =>
            CallMethod(() => string.Concat(default(string), default(string)), lhs, rhs);

        if (lhs.Type == typeof(object) || rhs.Type == typeof(object))
        {
            return CallMethod(
                () => Methods.DoAdd(null, null),
                CoerceToObject(lhs),
                CoerceToObject(rhs)
            );
        }

        if (lhs.Type == typeof(string) && rhs.Type == typeof(string))
        {
            if (lhs is ConstantExpression lc && rhs is ConstantExpression rc)
                return Expression.Constant((string)lc.Value + (string)rc.Value);

            return BuildConcat(lhs, rhs);
        }
        if (lhs.Type == typeof(string))
            return BuildConcat(lhs, CallMethod((object o) => o.ToString(), rhs));
        if (rhs.Type == typeof(string))
            return BuildConcat(CallMethod((object o) => o.ToString(), lhs), rhs);

        return BuildBinaryArithOp(lhs, rhs, (lhs, rhs) => lhs + rhs, Expression.Add);
    }

    static Expression BuildBinaryArithOp(
        Expression lhs,
        Expression rhs,
        Func<double, double, double> eval,
        Func<Expression, Expression, Expression> build
    )
    {
        lhs = CoerceToDouble(lhs);
        rhs = CoerceToDouble(rhs);

        if (lhs is ConstantExpression cl && rhs is ConstantExpression cr)
        {
            if (cl.Value is double ld && cr.Value is double rd)
                return Expression.Constant(eval(ld, rd));
        }

        return build(lhs, rhs);
    }
    #endregion

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

    static MethodCallExpression CallMethod<R>(Expression<Func<R>> expr, params Expression[] ps) =>
        CallMethod(GetMethodInfo(expr), ps);

    static MethodCallExpression CallMethod(LambdaExpression expr, params Expression[] ps) =>
        CallMethod(GetMethodInfo(expr), ps);

    static MethodCallExpression CallMethod(MethodInfo method, params Expression[] exprs)
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

    static MethodCallExpression CallOverloadedMethod(string name, Expression lhs, Expression rhs)
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
            throw new Exception($"No overload of {name} matches parameters ({lt.Name}, {rt.Name})");

        return CallMethod(method, lhs, rhs);
    }

    static Expression BuildNullCoalesce(Expression lhs, Expression rhs)
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
                Expression.Assign(p, lhs),
                Condition(IsNotNull(p), p, rhs)
            );
        }

        lhs = CoerceToObject(lhs);
        rhs = CoerceToObject(rhs);

        {
            var p = Expression.Parameter(lhs.Type);
            return Expression.Block(
                [p],
                Expression.Assign(p, lhs),
                Condition(IsNotNull(lhs), p, rhs)
            );
        }
    }

    static Expression BuildEquality(Expression lhs, Expression rhs)
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

    static MethodCallExpression BuildMethodCall(
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
                Expression.NewArrayInit(typeof(object), args.Select(arg => CoerceToObject(arg))),
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
        var et = expr.Type;
        if (et == typeof(bool))
            return expr;

        if (et == typeof(bool?))
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

        if (Nullable.GetUnderlyingType(et) != null)
            return Expression.Property(expr, "HasValue");
        if (et.IsValueType)
            return Expression.Constant(true);

        if (expr is ConstantExpression c)
            return Expression.Constant(Methods.CoerceToBool(c.Value));

        if (et == typeof(object))
            return Expression.Call(
                GetMethodInfo(() => Methods.CoerceToBool(default(object))),
                expr
            );
        if (et == typeof(string))
            return Expression.Call(
                GetMethodInfo(() => Methods.CoerceToBool(default(string))),
                expr
            );

        return Expression.ReferenceEqual(expr, Expression.Constant(null, et));
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
        var et = expr.Type;
        if (et == typeof(double))
            return expr;

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

        if (IsNumericType(et))
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
        // If already the correct enum type, no conversion needed
        if (expr.Type == typeof(T))
            return expr;

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

    static Expression MapNullable(Expression value, Func<Expression, Expression> func)
    {
        var type = value.Type;
        var inner = Nullable.GetUnderlyingType(type);

        if (value is ConstantExpression c)
        {
            if (c.Value == null)
                return Expression.Constant(null, type);
            if (inner == null)
                return func(value);

            var inval = type.GetProperty("Value").GetGetMethod().Invoke(c.Value, []);

            return func(Expression.Constant(inval));
        }
        else if (inner == null)
        {
            return func(value);
        }

        var param = Expression.Parameter(type);
        var result = func(param);
        var resty = result.Type;
        var nullty = resty;
        if (resty.IsValueType && Nullable.GetUnderlyingType(resty) == null)
        {
            nullty = typeof(Nullable<>).MakeGenericType(resty);
            result = Expression.New(nullty.GetConstructor([resty]), result);
        }

        return Expression.Block(
            [param],
            Expression.Assign(param, value),
            Expression.Condition(
                Expression.Property(param, "HasValue"),
                result,
                Expression.Constant(null, nullty)
            )
        );
    }

    static Expression MapNullable(
        Expression lhs,
        Expression rhs,
        Func<Expression, Expression, Expression> func
    )
    {
        return MapNullable(lhs, lhs => MapNullable(rhs, rhs => func(lhs, rhs)));
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

    static bool IsConstantNull(Expression expression)
    {
        if (expression is not ConstantExpression c)
            return false;

        return c.Value == null;
    }

    static readonly Type[] NumericTypes =
    [
        typeof(float),
        typeof(double),
        typeof(sbyte),
        typeof(short),
        typeof(int),
        typeof(long),
        typeof(byte),
        typeof(ushort),
        typeof(uint),
        typeof(ulong),
    ];

    static bool IsNumericType(Type t) => NumericTypes.Contains(t);

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
