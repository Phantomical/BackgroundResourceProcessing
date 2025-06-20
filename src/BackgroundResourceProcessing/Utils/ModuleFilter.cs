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
                var module = Expression.Parameter(typeof(PartModule), "module");

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

                return Expression.Lambda<Func<PartModule, bool>>(body, module);
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
                        return Expression.Constant("true");

                    case TokenType.False:
                        lexer.Advance();
                        return Expression.Constant("false");

                    case TokenType.TargetField:
                        var field = Current.Span.ToString();
                        lexer.Advance();

                        return InvokeFunc(module =>
                        {
                            var type = module.GetType();

                            if (field == "name")
                                return type.Name;

                            var info = type.GetField(field, BindingFlags.NonPublic);
                            if (info == null)
                                return null;

                            var value = info.GetValue(module);
                            return value switch
                            {
                                null => null,
                                string s => s,
                                _ => value.ToString(),
                            };
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
                if (token.Span == "==")
                    return Comparison.Equals;
                if (token.Span == "!=")
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

        private ref struct Lexer(string expr)
        {
            public readonly ReadOnlySpan<char> original = expr;
            ReadOnlySpan<char> span = expr;

            public Token current = default;

            public void Advance()
            {
                DoAdvance();
                current.Offset = CalculateOffset(current.Span);
            }

            private void DoAdvance()
            {
                span = span.TrimStart();

                if (span.IsEmpty)
                {
                    current = new() { Span = span, Type = TokenType.Eof };
                    return;
                }

                if (
                    span.StartsWith("==")
                    || span.StartsWith("!=")
                    || span.StartsWith("&&")
                    || span.StartsWith("||")
                )
                {
                    current = new() { Span = span.Slice(0, 2), Type = TokenType.Operator };
                    span = span.Slice(2);
                    return;
                }

                if (span[0] == '(')
                {
                    current = new() { Span = span.Slice(0, 1), Type = TokenType.LParen };
                    span = span.Slice(1);
                    return;
                }

                if (span[0] == ')')
                {
                    current = new() { Span = span.Slice(0, 1), Type = TokenType.RParen };
                    span = span.Slice(1);
                    return;
                }

                var index = 1;
                switch (span[0])
                {
                    case '@':
                    case '%':
                    case '?':
                        break;

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

                var token = span.Slice(0, index);
                TokenType type;

                if (current.Span == "null")
                    type = TokenType.Null;
                else if (current.Span == "true")
                    type = TokenType.True;
                else if (current.Span == "false")
                    type = TokenType.False;
                else if (current.Span.StartsWith("@"))
                    type = TokenType.TargetField;
                else if (current.Span.StartsWith("%"))
                    type = TokenType.ConfigField;
                else if (current.Span.StartsWith("?"))
                    type = TokenType.NullableConfigField;
                else
                {
                    throw RenderError($"Unexpected token `{token.ToString()}`", index);
                }

                current = new() { Span = token, Type = type };
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
                            return (int)(start - inner);
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

        ref struct Token
        {
            public ReadOnlySpan<char> Span;
            public TokenType Type;
            public int Offset;

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
