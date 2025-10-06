using System;

namespace BackgroundResourceProcessing.Expr;

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
