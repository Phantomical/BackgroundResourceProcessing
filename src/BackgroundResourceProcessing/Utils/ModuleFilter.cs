using System;
using System.Linq.Expressions;
using System.Reflection;

#nullable enable

namespace BackgroundResourceProcessing.Utils
{
    internal static class ModuleFilter
    {
        public static Func<PartModule, bool> Compile(string expression, ConfigNode node)
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

            public Func<PartModule, bool> Parse()
            {
                var expression = ParseOrExpression();
                var lambda = Expression.Lambda<Func<PartModule, bool>>(expression, module);
                return lambda.Compile();
            }

            private Expression ParseOrExpression()
            {
                Expression body = Expression.Constant(false);

                while (true)
                {
                    var expr = ParseAndExpression();

                    // total = total || expr(module)
                    body = Expression.OrElse(body, expr);

                    if (Current.Type == TokenType.Operator && Current.Span == "||")
                    {
                        lexer.Advance();
                        continue;
                    }

                    break;
                }

                return body;
            }

            private Expression ParseAndExpression()
            {
                Expression body = Expression.Constant(true);

                while (true)
                {
                    var expr = ParseBracedExpression();

                    // total = total && expr(module)
                    body = Expression.AndAlso(body, expr);

                    if (Current.Type == TokenType.Operator && Current.Span == "&&")
                    {
                        lexer.Advance();
                        continue;
                    }

                    break;
                }

                return body;
            }

            private Expression ParseBracedExpression()
            {
                if (Current.Type != TokenType.LParen)
                    return ParseComparisonExpression();

                lexer.Advance();
                var expr = ParseOrExpression();

                if (Current.Type != TokenType.RParen)
                    throw RenderError($"Unexpected token `{Current.ToString()}`");
                lexer.Advance();

                return expr;
            }

            private Expression ParseComparisonExpression()
            {
                var lhs = ParseValueExpression();
                var op = ParseComparision();
                var rhs = ParseValueExpression();

                return op switch
                {
                    Comparison.Equals => Expression.Equal(lhs, rhs),
                    Comparison.NotEquals => Expression.NotEqual(lhs, rhs),
                    _ => throw new NotImplementedException(
                        $"Unimplemented comparison operator {op}"
                    ),
                };
            }

            private Expression ParseValueExpression()
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
                        return Expression.Constant(value);

                    case TokenType.NullableConfigField:
                        name = Current.Span.Slice(1).ToString();
                        value = null;
                        node.TryGetValue(name, ref value);
                        lexer.Advance();
                        return Expression.Constant(value);

                    case TokenType.Null:
                        lexer.Advance();
                        return Expression.Constant(null);

                    case TokenType.True:
                        lexer.Advance();
                        return Expression.Constant("True");

                    case TokenType.False:
                        lexer.Advance();
                        return Expression.Constant("False");

                    case TokenType.TargetField:
                        var field = Current.Span.Slice(1).ToString();
                        lexer.Advance();

                        return InvokeFunc(module =>
                        {
                            var type = module.GetType();

                            if (field == "name")
                                return type.Name;

                            var info = type.GetField(
                                field,
                                BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance
                            );
                            if (info == null)
                                return null;

                            var value = info.GetValue(module);
                            var ret = value switch
                            {
                                null => null,
                                string s => s,
                                _ => value.ToString(),
                            };

                            return ret;
                        });

                    default:
                        throw RenderError(
                            $"Unexpected token `{Current.ToString()}`, expected `null`, `true`, `false`, `@<field>`, `%<field>`, or `?<field>`"
                        );
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

                    default:
                        index = 0;
                        break;
                }

                bool first = true;
                for (; index < span.Length; ++index)
                {
                    var ch = span[index];

                    if (!first && IsNumeric(ch))
                        continue;
                    first = false;
                    if (IsAlpha(ch) || ch == '_')
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
                            if (inner < start)
                                return -1;
                            return (int)(inner - start);
                        }
                    }
                }
            }

            private static bool IsAlpha(char c)
            {
                return ('a' <= c && c <= 'z') || ('A' <= c && c <= 'Z');
            }

            private static bool IsNumeric(char c)
            {
                return '0' <= c && c <= '9';
            }

            public ModuleFilterException RenderError(string message, int offset = -1)
            {
                if (offset == -1)
                    offset = current.Offset;

                return new ModuleFilterException(
                    $"{message}\n | {original.ToString()}\n | {new string(' ', offset)}^"
                );
            }
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

        enum TokenType
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
        }

        ref struct Token(TokenSpan span, TokenType type)
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
    }

    public class ModuleFilterException(string message) : Exception(message) { }
}
