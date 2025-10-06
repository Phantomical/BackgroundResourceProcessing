using System;
using BackgroundResourceProcessing.Utils;

namespace BackgroundResourceProcessing.Expr;

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

            char c = span[i];

            if (c == '\\')
            {
                i += 1;
                continue;
            }

            if (c == '"')
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

            return new CompilationException($"{message}\n | {s}\n | {new string(' ', offset)}^");
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
