using BackgroundResourceProcessing.Expr;
using BackgroundResourceProcessing.Utils;
using KSP.Testing;

namespace BackgroundResourceProcessing.Test.Utils;

/// <summary>
/// Tests for string literal parsing and escape sequences in field expressions.
/// Tests the Lexer.TakeString method and related string handling.
/// </summary>
public sealed class FieldExpressionStringLiteralsTests : BRPTestBase
{
    class StringTestModule : PartModule
    {
        public string text = "hello";
        public string empty = "";
    }

    #region Basic String Literals

    [TestInfo("FieldExpressionStringLiteralsTests_BasicString_Simple")]
    public void BasicString_Simple()
    {
        var expr = BackgroundResourceProcessing.Utils.FieldExpression<string>.Compile(
            "\"hello\"",
            new(),
            typeof(StringTestModule)
        );
        Assert.AreEqual("hello", expr.Evaluate(new StringTestModule()));
    }

    [TestInfo("FieldExpressionStringLiteralsTests_BasicString_Empty")]
    public void BasicString_Empty()
    {
        var expr = BackgroundResourceProcessing.Utils.FieldExpression<string>.Compile(
            "\"\"",
            new(),
            typeof(StringTestModule)
        );
        Assert.AreEqual("", expr.Evaluate(new StringTestModule()));
    }

    [TestInfo("FieldExpressionStringLiteralsTests_BasicString_WithSpaces")]
    public void BasicString_WithSpaces()
    {
        var expr = BackgroundResourceProcessing.Utils.FieldExpression<string>.Compile(
            "\"hello world\"",
            new(),
            typeof(StringTestModule)
        );
        Assert.AreEqual("hello world", expr.Evaluate(new StringTestModule()));
    }

    [TestInfo("FieldExpressionStringLiteralsTests_BasicString_WithNumbers")]
    public void BasicString_WithNumbers()
    {
        var expr = BackgroundResourceProcessing.Utils.FieldExpression<string>.Compile(
            "\"test123\"",
            new(),
            typeof(StringTestModule)
        );
        Assert.AreEqual("test123", expr.Evaluate(new StringTestModule()));
    }

    #endregion

    #region Escape Sequences

    [TestInfo("FieldExpressionStringLiteralsTests_EscapeSequence_Quote")]
    public void EscapeSequence_Quote()
    {
        var expr = BackgroundResourceProcessing.Utils.FieldExpression<string>.Compile(
            "\"say \\\"hi\\\"\"",
            new(),
            typeof(StringTestModule)
        );
        Assert.AreEqual("say \"hi\"", expr.Evaluate(new StringTestModule()));
    }

    [TestInfo("FieldExpressionStringLiteralsTests_EscapeSequence_Backslash")]
    public void EscapeSequence_Backslash()
    {
        var expr = BackgroundResourceProcessing.Utils.FieldExpression<string>.Compile(
            "\"path\\\\file\"",
            new(),
            typeof(StringTestModule)
        );
        Assert.AreEqual("path\\file", expr.Evaluate(new StringTestModule()));
    }

    // Note: The lexer only supports \" and \\ escape sequences
    // It does not support \n, \t, \r, or other C-style escapes

    #endregion

    #region String Comparisons

    [TestInfo("FieldExpressionStringLiteralsTests_StringComparison_Equals")]
    public void StringComparison_Equals()
    {
        var expr = BackgroundResourceProcessing.Utils.FieldExpression<bool>.Compile(
            "\"test\" == \"test\"",
            new(),
            typeof(StringTestModule)
        );
        Assert.IsTrue(expr.Evaluate(new StringTestModule()));
    }

    [TestInfo("FieldExpressionStringLiteralsTests_StringComparison_NotEquals")]
    public void StringComparison_NotEquals()
    {
        var expr = BackgroundResourceProcessing.Utils.FieldExpression<bool>.Compile(
            "\"test\" != \"other\"",
            new(),
            typeof(StringTestModule)
        );
        Assert.IsTrue(expr.Evaluate(new StringTestModule()));
    }

    [TestInfo("FieldExpressionStringLiteralsTests_StringComparison_WithField")]
    public void StringComparison_WithField()
    {
        var module = new StringTestModule { text = "hello" };
        var expr = BackgroundResourceProcessing.Utils.FieldExpression<bool>.Compile(
            "%text == \"hello\"",
            new(),
            typeof(StringTestModule)
        );
        Assert.IsTrue(expr.Evaluate(module));
    }

    [TestInfo("FieldExpressionStringLiteralsTests_StringComparison_EmptyString")]
    public void StringComparison_EmptyString()
    {
        var module = new StringTestModule { empty = "" };
        var expr = BackgroundResourceProcessing.Utils.FieldExpression<bool>.Compile(
            "%empty == \"\"",
            new(),
            typeof(StringTestModule)
        );
        Assert.IsTrue(expr.Evaluate(module));
    }

    #endregion

    #region String Concatenation (if supported)

    [TestInfo("FieldExpressionStringLiteralsTests_StringConcatenation_TwoLiterals")]
    public void StringConcatenation_TwoLiterals()
    {
        // Test if + operator works for strings
        var expr = BackgroundResourceProcessing.Utils.FieldExpression<string>.Compile(
            "\"hello\" + \" world\"",
            new(),
            typeof(StringTestModule)
        );
        Assert.AreEqual("hello world", expr.Evaluate(new StringTestModule()));
    }

    [TestInfo("FieldExpressionStringLiteralsTests_StringConcatenation_WithField")]
    public void StringConcatenation_WithField()
    {
        var module = new StringTestModule { text = "hello" };
        var expr = BackgroundResourceProcessing.Utils.FieldExpression<string>.Compile(
            "%text + \" world\"",
            new(),
            typeof(StringTestModule)
        );
        Assert.AreEqual("hello world", expr.Evaluate(module));
    }

    [TestInfo("FieldExpressionStringLiteralsTests_StringConcatenation_Multiple")]
    public void StringConcatenation_Multiple()
    {
        var expr = BackgroundResourceProcessing.Utils.FieldExpression<string>.Compile(
            "\"a\" + \"b\" + \"c\"",
            new(),
            typeof(StringTestModule)
        );
        Assert.AreEqual("abc", expr.Evaluate(new StringTestModule()));
    }

    #endregion

    #region Special Characters

    [TestInfo("FieldExpressionStringLiteralsTests_SpecialChars_Punctuation")]
    public void SpecialChars_Punctuation()
    {
        var expr = BackgroundResourceProcessing.Utils.FieldExpression<string>.Compile(
            "\"Hello, World!\"",
            new(),
            typeof(StringTestModule)
        );
        Assert.AreEqual("Hello, World!", expr.Evaluate(new StringTestModule()));
    }

    [TestInfo("FieldExpressionStringLiteralsTests_SpecialChars_Symbols")]
    public void SpecialChars_Symbols()
    {
        var expr = BackgroundResourceProcessing.Utils.FieldExpression<string>.Compile(
            "\"@#$%^&*()\"",
            new(),
            typeof(StringTestModule)
        );
        Assert.AreEqual("@#$%^&*()", expr.Evaluate(new StringTestModule()));
    }

    [TestInfo("FieldExpressionStringLiteralsTests_SpecialChars_BracesAndBrackets")]
    public void SpecialChars_BracesAndBrackets()
    {
        var expr = BackgroundResourceProcessing.Utils.FieldExpression<string>.Compile(
            "\"{}[]<>\"",
            new(),
            typeof(StringTestModule)
        );
        Assert.AreEqual("{}[]<>", expr.Evaluate(new StringTestModule()));
    }

    #endregion

    #region Null Coalescing with Strings

    [TestInfo("FieldExpressionStringLiteralsTests_NullCoalesce_StringField")]
    public void NullCoalesce_StringField()
    {
        var module = new StringTestModule { text = null };
        var expr = BackgroundResourceProcessing.Utils.FieldExpression<string>.Compile(
            "%text ?? \"default\"",
            new(),
            typeof(StringTestModule)
        );
        Assert.AreEqual("default", expr.Evaluate(module));
    }

    [TestInfo("FieldExpressionStringLiteralsTests_NullCoalesce_StringFieldNotNull")]
    public void NullCoalesce_StringFieldNotNull()
    {
        var module = new StringTestModule { text = "value" };
        var expr = BackgroundResourceProcessing.Utils.FieldExpression<string>.Compile(
            "%text ?? \"default\"",
            new(),
            typeof(StringTestModule)
        );
        Assert.AreEqual("value", expr.Evaluate(module));
    }

    [TestInfo("FieldExpressionStringLiteralsTests_NullCoalesce_EmptyStringNotNull")]
    public void NullCoalesce_EmptyStringNotNull()
    {
        var module = new StringTestModule { empty = "" };
        var expr = BackgroundResourceProcessing.Utils.FieldExpression<string>.Compile(
            "%empty ?? \"default\"",
            new(),
            typeof(StringTestModule)
        );
        Assert.AreEqual("", expr.Evaluate(module)); // Empty string is not null
    }

    #endregion

    #region Error Cases

    [TestInfo("FieldExpressionStringLiteralsTests_ErrorCase_UnterminatedString")]
    public void ErrorCase_UnterminatedString()
    {
        Assert.ThrowsException<CompilationException>(() =>
            BackgroundResourceProcessing.Utils.FieldExpression<string>.Compile(
                "\"unterminated",
                new(),
                typeof(StringTestModule)
            )
        );
    }

    [TestInfo("FieldExpressionStringLiteralsTests_ErrorCase_UnterminatedStringWithEscape")]
    public void ErrorCase_UnterminatedStringWithEscape()
    {
        Assert.ThrowsException<CompilationException>(() =>
            BackgroundResourceProcessing.Utils.FieldExpression<string>.Compile(
                "\"text\\",
                new(),
                typeof(StringTestModule)
            )
        );
    }

    [TestInfo("FieldExpressionStringLiteralsTests_ErrorCase_StringWithOnlyEscapes")]
    public void ErrorCase_StringWithOnlyEscapes()
    {
        var expr = BackgroundResourceProcessing.Utils.FieldExpression<string>.Compile(
            "\"\\n\\t\\r\"",
            new(),
            typeof(StringTestModule)
        );
        Assert.AreEqual("\n\t\r", expr.Evaluate(new StringTestModule()));
    }

    #endregion

    #region Complex String Expressions

    [TestInfo("FieldExpressionStringLiteralsTests_Complex_StringInComparison")]
    public void Complex_StringInComparison()
    {
        var module = new StringTestModule { text = "test" };
        var expr = BackgroundResourceProcessing.Utils.FieldExpression<bool>.Compile(
            "(%text == \"test\") && (\"a\" != \"b\")",
            new(),
            typeof(StringTestModule)
        );
        Assert.IsTrue(expr.Evaluate(module));
    }

    [TestInfo("FieldExpressionStringLiteralsTests_Complex_StringConcatenationWithEscapes")]
    public void Complex_StringConcatenationWithEscapes()
    {
        var expr = BackgroundResourceProcessing.Utils.FieldExpression<string>.Compile(
            "\"line1\\n\" + \"line2\"",
            new(),
            typeof(StringTestModule)
        );
        Assert.AreEqual("line1\nline2", expr.Evaluate(new StringTestModule()));
    }

    [TestInfo("FieldExpressionStringLiteralsTests_Complex_MultipleStringsAndFields")]
    public void Complex_MultipleStringsAndFields()
    {
        var module = new StringTestModule { text = "middle" };
        var expr = BackgroundResourceProcessing.Utils.FieldExpression<string>.Compile(
            "\"start-\" + %text + \"-end\"",
            new(),
            typeof(StringTestModule)
        );
        Assert.AreEqual("start-middle-end", expr.Evaluate(module));
    }

    #endregion

    #region Edge Cases

    [TestInfo("FieldExpressionStringLiteralsTests_EdgeCase_VeryLongString")]
    public void EdgeCase_VeryLongString()
    {
        var longString = new string('a', 1000);
        var expr = BackgroundResourceProcessing.Utils.FieldExpression<string>.Compile(
            $"\"{longString}\"",
            new(),
            typeof(StringTestModule)
        );
        Assert.AreEqual(longString, expr.Evaluate(new StringTestModule()));
    }

    [TestInfo("FieldExpressionStringLiteralsTests_EdgeCase_StringWithOnlySpaces")]
    public void EdgeCase_StringWithOnlySpaces()
    {
        var expr = BackgroundResourceProcessing.Utils.FieldExpression<string>.Compile(
            "\"     \"",
            new(),
            typeof(StringTestModule)
        );
        Assert.AreEqual("     ", expr.Evaluate(new StringTestModule()));
    }

    [TestInfo("FieldExpressionStringLiteralsTests_EdgeCase_ConsecutiveEscapes")]
    public void EdgeCase_ConsecutiveEscapes()
    {
        var expr = BackgroundResourceProcessing.Utils.FieldExpression<string>.Compile(
            "\"\\\\\\\\\"",
            new(),
            typeof(StringTestModule)
        );
        Assert.AreEqual("\\\\", expr.Evaluate(new StringTestModule()));
    }

    [TestInfo("FieldExpressionStringLiteralsTests_EdgeCase_QuoteAtEnd")]
    public void EdgeCase_QuoteAtEnd()
    {
        var expr = BackgroundResourceProcessing.Utils.FieldExpression<string>.Compile(
            "\"end\\\"\"",
            new(),
            typeof(StringTestModule)
        );
        Assert.AreEqual("end\"", expr.Evaluate(new StringTestModule()));
    }

    [TestInfo("FieldExpressionStringLiteralsTests_EdgeCase_QuoteAtStart")]
    public void EdgeCase_QuoteAtStart()
    {
        var expr = BackgroundResourceProcessing.Utils.FieldExpression<string>.Compile(
            "\"\\\"start\"",
            new(),
            typeof(StringTestModule)
        );
        Assert.AreEqual("\"start", expr.Evaluate(new StringTestModule()));
    }

    #endregion
}
