using System;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.InteropServices;

#nullable enable

namespace BackgroundResourceProcessing.Utils
{
    internal class ModuleFilter(Func<PartModule, bool> filter, string expansion)
    {
        readonly string expansion = expansion;
        readonly Func<PartModule, bool> filter = filter;

        /// <summary>
        /// A filter that always returns false.
        /// </summary>
        internal static readonly ModuleFilter Never = new(_ => false, "false");

        /// <summary>
        /// A filter that always returns true.
        /// </summary>
        internal static readonly ModuleFilter Always = new(_ => true, "true");

        public bool Invoke(PartModule module)
        {
            return filter(module);
        }

        public override string ToString()
        {
            return expansion;
        }

        public static ModuleFilter Compile(string expression, ConfigNode node)
        {
            Parser parser = new(expression, node);
            return parser.Parse();
        }

        private ref struct Parser(string expr, ConfigNode node)
        {
            Lexer lexer = new(expr);
            readonly ConfigNode node = node;
            readonly ParameterExpression module = Expression.Parameter(
                typeof(PartModule),
                "module"
            );

            readonly Token Current => lexer.current;

            public ModuleFilter Parse()
            {
                var expression = ParseOrExpression();
                var lambda = Expression.Lambda<Func<PartModule, bool>>(expression.expr, module);
                return new(lambda.Compile(), expression.text);
            }

            private FilterExpr ParseOrExpression()
            {
                FilterExpr? body = null;

                while (true)
                {
                    var expr = ParseAndExpression();

                    // total = total || expr(module)
                    if (body == null)
                        body = expr;
                    else
                        body = FilterExpr.OrElse((FilterExpr)body, expr);

                    if (Current.Type == TokenType.Operator && Current.Span == "||")
                    {
                        lexer.Advance();
                        continue;
                    }

                    break;
                }

                return body ?? FilterExpr.Constant(false);
            }

            private FilterExpr ParseAndExpression()
            {
                FilterExpr? body = null;

                while (true)
                {
                    var expr = ParseNotExpression();

                    // total = total && expr(module)
                    if (body == null)
                        body = expr;
                    else
                        body = FilterExpr.AndAlso((FilterExpr)body, expr);

                    if (Current.Type == TokenType.Operator && Current.Span == "&&")
                    {
                        lexer.Advance();
                        continue;
                    }

                    break;
                }

                return body ?? FilterExpr.Constant(true);
            }

            private FilterExpr ParseNotExpression()
            {
                if (Current.Type != TokenType.Not)
                    return ParseBracedExpression();

                lexer.Advance();
                if (Current.Type == TokenType.LParen)
                    return FilterExpr.Not(ParseBracedExpression());

                var expr = ParseValueExpression();
                var func = (string value) =>
                {
                    if (value == null)
                        return true;
                    if (value == "False")
                        return true;
                    return false;
                };

                return new(
                    Expression.Invoke(Expression.Constant(func), expr.expr),
                    $"!{expr.text}"
                );
            }

            private FilterExpr ParseBracedExpression()
            {
                if (Current.Type != TokenType.LParen)
                    return ParseComparisonExpression();

                lexer.Advance();
                var expr = ParseOrExpression();

                if (Current.Type != TokenType.RParen)
                    throw RenderError($"Unexpected token `{Current.ToString()}`");
                lexer.Advance();

                return new(expr.expr, $"({expr.text})");
            }

            private FilterExpr ParseComparisonExpression()
            {
                var lhs = ParseValueExpression();
                if (Current.Type != TokenType.Operator)
                {
                    var func = (string value) =>
                    {
                        if (value == "False" || value == null)
                            return false;
                        return true;
                    };

                    return new(Expression.Invoke(Expression.Constant(func), lhs.expr), lhs.text);
                }

                var op = ParseComparision();
                var rhs = ParseValueExpression();

                return op switch
                {
                    Comparison.Equals => FilterExpr.Equal(lhs, rhs),
                    Comparison.NotEquals => FilterExpr.NotEqual(lhs, rhs),
                    _ => throw new NotImplementedException(
                        $"Unimplemented comparison operator {op}"
                    ),
                };
            }

            private FilterExpr ParseValueExpression()
            {
                string name;
                string? value;
                switch (Current.Type)
                {
                    case TokenType.ConfigField:
                        name = Current.Span.Slice(1).ToString();
                        value = null;
                        if (!node.TryGetValue(name, ref value))
                            throw RenderError(
                                $"Expression refers to field `{name}` on the current ConfigNode but no such field exists."
                            );

                        lexer.Advance();
                        return FilterExpr.Constant(value);

                    case TokenType.NullableConfigField:
                        name = Current.Span.Slice(1).ToString();
                        value = null;
                        node.TryGetValue(name, ref value);
                        lexer.Advance();
                        return FilterExpr.Constant(value);

                    case TokenType.Null:
                        lexer.Advance();
                        return FilterExpr.Null();

                    case TokenType.True:
                        lexer.Advance();
                        return FilterExpr.Constant("True");

                    case TokenType.False:
                        lexer.Advance();
                        return FilterExpr.Constant("False");

                    case TokenType.TargetField:
                        var field = Current.Span.Slice(1).ToString();
                        lexer.Advance();

                        var func = InvokeFunc(module =>
                        {
                            var type = module.GetType();

                            if (field == "name")
                                return type.Name;

                            var value = GetMemberValue(module, field);
                            var ret = value switch
                            {
                                null => null,
                                string s => s,
                                _ => value.ToString(),
                            };

                            return ret;
                        });

                        return new(func, Current.Span.ToString());

                    default:
                        throw RenderError(
                            $"Unexpected token `{Current.ToString()}`, expected `null`, `true`, `false`, `@<field>`, `%<field>`, or `?<field>`"
                        );
                }
            }

            private static object? GetMemberValue(object obj, string member)
            {
                const BindingFlags Flags =
                    BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance;

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

            private Comparison ParseComparision()
            {
                if (Current.Type != TokenType.Operator)
                    throw RenderError($"Unexpected `{Current.ToString()}`, expected `==` or `!=`");

                var copy = lexer;
                var token = Current;
                lexer.Advance();
                if (token.Span == "==".AsSpan())
                    return Comparison.Equals;
                if (token.Span == "!=".AsSpan())
                    return Comparison.NotEquals;

                lexer = copy;
                throw RenderError(
                    $"Unexpected operator `{token.ToString()}`, expected `==` or `!=`",
                    token.Offset
                );
            }

            private readonly Expression InvokeFunc(Func<PartModule, string?> func)
            {
                return Expression.Invoke(Expression.Constant(func), module);
            }

            private ModuleFilterException RenderError(string message, int offset = -1)
            {
                return lexer.RenderError(message, offset);
            }

            enum Comparison
            {
                Equals,
                NotEquals,
            }
        }

        private ref struct Lexer
        {
            public readonly ReadOnlySpan<char> original;
            ReadOnlySpan<char> span;

            public Token current = default;

            public Lexer(string expr)
            {
                original = expr;
                span = expr;

                Advance();
            }

            public void Advance()
            {
                DoAdvance();
                current.Offset = CalculateOffset(current.Span.span);
            }

            private void DoAdvance()
            {
                span = span.TrimStart();

                if (span.IsEmpty)
                {
                    current = new(span, TokenType.Eof);
                    return;
                }

                if (
                    span.StartsWith("==")
                    || span.StartsWith("!=")
                    || span.StartsWith("&&")
                    || span.StartsWith("||")
                )
                {
                    current = new(span.Slice(0, 2), TokenType.Operator);
                    span = span.Slice(2);
                    return;
                }

                var index = 1;
                switch (span[0])
                {
                    case '@':
                    case '%':
                    case '?':
                        break;

                    case '(':
                        current = new(span.Slice(0, 1), TokenType.LParen);
                        span = span.Slice(1);
                        return;

                    case ')':
                        current = new(span.Slice(0, 1), TokenType.RParen);
                        span = span.Slice(1);
                        return;

                    case '!':
                        current = new(span.Slice(0, 1), TokenType.Not);
                        span = span.Slice(1);
                        return;

                    default:
                        index = 0;

                        if (char.IsLetter(span[0]))
                            break;

                        for (; index < span.Length; index++)
                        {
                            if (char.IsWhiteSpace(span[index]))
                                break;
                        }

                        var tok = span.Slice(0, index);
                        throw RenderError($"Unexpected token `{tok.ToString()}`", tok);
                }

                bool first = true;
                for (; index < span.Length; ++index)
                {
                    var ch = span[index];

                    if (!first && char.IsDigit(ch))
                        continue;
                    first = false;
                    if (char.IsLetter(ch) || ch == '_')
                        continue;

                    break;
                }

                if (first)
                {
                    if (index == span.Length)
                    {
                        throw RenderError(
                            $"`{span.ToString()}` is not a valid value or field reference",
                            index - 1
                        );
                    }

                    throw RenderError(
                        $"Unexpected token `{span[index]}`. Expected an identifier matching `[A-Za-z_][A-Za-z0-9_]+`",
                        index
                    );
                }

                var token = new TokenSpan(span.Slice(0, index));
                TokenType type;

                if (token == "null")
                    type = TokenType.Null;
                else if (token.EqualsIgnoreCase("true"))
                    type = TokenType.True;
                else if (token.EqualsIgnoreCase("false"))
                    type = TokenType.False;
                else if (token.StartsWith("%"))
                    type = TokenType.TargetField;
                else if (token.StartsWith("@"))
                    type = TokenType.ConfigField;
                else if (token.StartsWith("?"))
                    type = TokenType.NullableConfigField;
                else
                {
                    throw RenderError($"Unexpected token `{token.ToString()}`", index);
                }

                current = new(token, type);
                span = span.Slice(index);
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

            public readonly ModuleFilterException RenderError(string message, int offset = -1)
            {
                if (offset < -1)
                    LogUtil.Warn($"RenderError called with invalid offset ({offset})");

                if (offset < 0)
                    offset = current.Offset;

                var s = original.ToString();
                offset = MathUtil.Clamp(offset, 0, s.Length);

                return new ModuleFilterException(
                    $"{message}\n | {s}\n | {new string(' ', offset)}^"
                );
            }

            public readonly ModuleFilterException RenderError(
                string message,
                ReadOnlySpan<char> span
            ) => RenderError(message, CalculateOffset(span));
        }

        private readonly ref struct TokenSpan(ReadOnlySpan<char> span)
        {
            public readonly ReadOnlySpan<char> span = span;

            public static bool operator ==(TokenSpan span, string str) =>
                MemoryExtensions.Equals(span.span, str, StringComparison.Ordinal);

            public static bool operator ==(TokenSpan span, ReadOnlySpan<char> str) =>
                MemoryExtensions.Equals(span.span, str, StringComparison.Ordinal);

            public static bool operator ==(TokenSpan span, Span<char> str) =>
                MemoryExtensions.Equals(span.span, str, StringComparison.Ordinal);

            public static bool operator !=(TokenSpan span, string str) => !(span == str);

            public static bool operator !=(TokenSpan span, ReadOnlySpan<char> str) =>
                !(span == str);

            public static bool operator !=(TokenSpan span, Span<char> str) => !(span == str);

            public bool EqualsIgnoreCase(ReadOnlySpan<char> str) =>
                MemoryExtensions.Equals(span, str, StringComparison.OrdinalIgnoreCase);

            public TokenSpan Slice(int start)
            {
                return new(span.Slice(start));
            }

            public TokenSpan Slice(int start, int length)
            {
                return new(span.Slice(start, length));
            }

            public bool StartsWith(ReadOnlySpan<char> value)
            {
                return span.StartsWith(value);
            }

            public override string ToString()
            {
                return span.ToString();
            }

            public override bool Equals(object obj)
            {
                return obj switch
                {
                    string s => this == s,
                    _ => false,
                };
            }

            public override int GetHashCode()
            {
                throw new NotImplementedException();
            }
        }

        private enum TokenType
        {
            // The end of the file.
            Eof = 0,

            // A reference to a field on the target type.
            //
            // This is an ident prefixed with `%`.
            TargetField,

            // An operator (==, !=, &&, ||)
            Operator,

            // A reference to a key on the current ConfigNode.
            //
            // This is an ident prefixed with `@`.
            ConfigField,

            // A reference to a key on the current ConfigNode that resolves to
            // `null` if that key is not present.
            //
            // This is an ident prefixed with `?`.
            NullableConfigField,

            // A null constant.
            Null,

            // The constant string "true"
            True,

            // The constant string "false"
            False,

            // An opening parenthesis `(`
            LParen,

            // A closing parenthesis `)`
            RParen,

            // The operator `!`
            Not,
        }

        private ref struct Token(TokenSpan span, TokenType type)
        {
            public TokenSpan Span = span;
            public TokenType Type = type;
            public int Offset;

            public Token(ReadOnlySpan<char> span, TokenType type)
                : this(new TokenSpan(span), type) { }

            public override readonly string ToString()
            {
                if (Type == TokenType.Eof)
                    return "<eof>";
                return Span.ToString();
            }
        }

        private readonly struct FilterExpr(Expression expr, string text)
        {
            public readonly Expression expr = expr;
            public readonly string text = text;

            public static FilterExpr Constant(bool value)
            {
                return new(Expression.Constant(value), value.ToString());
            }

            public static FilterExpr Constant(string value)
            {
                return new(Expression.Constant(value), Quoted(value));
            }

            public static FilterExpr Null()
            {
                return new(Expression.Constant(null), "null");
            }

            public static FilterExpr Not(FilterExpr expr)
            {
                return new(Expression.Not(expr.expr), $"!{expr.text}");
            }

            public static FilterExpr Equal(FilterExpr lhs, FilterExpr rhs)
            {
                return new(Expression.Equal(lhs.expr, rhs.expr), $"{lhs.text} == {rhs.text}");
            }

            public static FilterExpr NotEqual(FilterExpr lhs, FilterExpr rhs)
            {
                return new(Expression.NotEqual(lhs.expr, rhs.expr), $"{lhs.text} != {rhs.text}");
            }

            public static FilterExpr AndAlso(FilterExpr lhs, FilterExpr rhs)
            {
                return new(Expression.AndAlso(lhs.expr, rhs.expr), $"{lhs.text} && {rhs.text}");
            }

            public static FilterExpr OrElse(FilterExpr lhs, FilterExpr rhs)
            {
                return new(Expression.OrElse(lhs.expr, rhs.expr), $"{lhs.text} || {rhs.text}");
            }

            public override string ToString()
            {
                return text;
            }

            private static string Quoted(string value)
            {
                if (value == null)
                    return "null";
                return $"\"{value.Replace("\"", "\\\"")}\"";
            }
        }
    }

    public class ModuleFilterException(string message) : Exception(message) { }
}
