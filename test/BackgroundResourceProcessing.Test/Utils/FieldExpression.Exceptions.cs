using BackgroundResourceProcessing.Expr;
using BackgroundResourceProcessing.Utils;
using KSP.Testing;

namespace BackgroundResourceProcessing.Test.Utils;

/// <summary>
/// Tests for exception handling in field expressions.
/// </summary>
public sealed class FieldExpressionExceptionsTests : BRPTestBase
{
    class ExceptionTestModule : PartModule
    {
        public double value = 10.0;
        public string text = "hello";
        public object nullObject = null;
    }

    #region CompilationException Tests

    [TestInfo(
        "FieldExpressionExceptionsTests_CompilationException_InvalidSyntax_MissingClosingParen"
    )]
    public void CompilationException_InvalidSyntax_MissingClosingParen()
    {
        Assert.ThrowsException<Expr.CompilationException>(() =>
            BackgroundResourceProcessing.Utils.FieldExpression<double>.Compile(
                "(1 + 2",
                new(),
                typeof(ExceptionTestModule)
            )
        );
    }

    // Note: "1 + + 2" is actually valid - it parses as "1 + (+2)"
    // The parser accepts unary + operator

    [TestInfo("FieldExpressionExceptionsTests_CompilationException_InvalidSyntax_EmptyExpression")]
    public void CompilationException_InvalidSyntax_EmptyExpression()
    {
        Assert.ThrowsException<Expr.CompilationException>(() =>
            BackgroundResourceProcessing.Utils.FieldExpression<double>.Compile(
                "",
                new(),
                typeof(ExceptionTestModule)
            )
        );
    }

    [TestInfo(
        "FieldExpressionExceptionsTests_CompilationException_InvalidSyntax_UnterminatedString"
    )]
    public void CompilationException_InvalidSyntax_UnterminatedString()
    {
        Assert.ThrowsException<Expr.CompilationException>(() =>
            BackgroundResourceProcessing.Utils.FieldExpression<string>.Compile(
                "\"unterminated",
                new(),
                typeof(ExceptionTestModule)
            )
        );
    }

    [TestInfo("FieldExpressionExceptionsTests_CompilationException_InvalidSyntax_InvalidOperator")]
    public void CompilationException_InvalidSyntax_InvalidOperator()
    {
        Assert.ThrowsException<Expr.CompilationException>(() =>
            BackgroundResourceProcessing.Utils.FieldExpression<double>.Compile(
                "1 @ 2",
                new(),
                typeof(ExceptionTestModule)
            )
        );
    }

    // Note: String fields can be coerced to double
    // The type system is flexible and allows cross-type conversions

    [TestInfo("FieldExpressionExceptionsTests_CompilationException_MessageContainsDetails")]
    public void CompilationException_MessageContainsDetails()
    {
        try
        {
            BackgroundResourceProcessing.Utils.FieldExpression<double>.Compile(
                "(1 + 2",
                new(),
                typeof(ExceptionTestModule)
            );
            Assert.Fail("Should have thrown CompilationException");
        }
        catch (Expr.CompilationException ex)
        {
            Assert.IsNotNull(ex.Message);
            Assert.IsTrue(ex.Message.Length > 0);
        }
    }

    #endregion

    #region EvaluationException Tests

    [TestInfo("FieldExpressionExceptionsTests_EvaluationException_DivisionByZero_ReturnsInfinity")]
    public void EvaluationException_DivisionByZero_ReturnsInfinity()
    {
        var module = new ExceptionTestModule { value = 0.0 };
        var expr = BackgroundResourceProcessing.Utils.FieldExpression<double>.Compile(
            "10 / %value",
            new(),
            typeof(ExceptionTestModule)
        );

        var result = expr.Evaluate(module);
        Assert.IsTrue(double.IsPositiveInfinity(result.Value));
    }

    #endregion

    #region NullValueException Tests

    [TestInfo("FieldExpressionExceptionsTests_NullValueException_NullToNonNullableStruct")]
    public void NullValueException_NullToNonNullableStruct()
    {
        var module = new ExceptionTestModule { nullObject = null };
        // Trying to evaluate null as a non-nullable double
        var expr = BackgroundResourceProcessing.Utils.FieldExpression<double>.Compile(
            "%nullObject ?? 5.0",
            new(),
            typeof(ExceptionTestModule)
        );

        // Should use the fallback value
        var result = expr.Evaluate(module);
        Assert.AreEqual(5.0, result);
    }

    [TestInfo("FieldExpressionExceptionsTests_NullValueException_NullField_HandledGracefully")]
    public void NullValueException_NullField_HandledGracefully()
    {
        var module = new ExceptionTestModule { nullObject = null };
        var expr = BackgroundResourceProcessing.Utils.FieldExpression<object>.Compile(
            "%nullObject",
            new(),
            typeof(ExceptionTestModule)
        );

        // Null should be returned for reference types
        var result = expr.Evaluate(module);
        Assert.IsNull(result);
    }

    #endregion

    #region Exception Handling in Evaluate Methods

    [TestInfo("FieldExpressionExceptionsTests_Evaluate_StructType_CatchesExceptionsReturnsNull")]
    public void Evaluate_StructType_CatchesExceptionsReturnsNull()
    {
        var module = new ExceptionTestModule();
        // Create expression that will fail during evaluation
        var expr = BackgroundResourceProcessing.Utils.FieldExpression<double>.Compile(
            "%nonExistentField ?? 42",
            new(),
            typeof(ExceptionTestModule)
        );

        var result = expr.Evaluate(module);
        Assert.AreEqual(42.0, result);
    }

    [TestInfo("FieldExpressionExceptionsTests_Evaluate_ClassType_CatchesExceptionsReturnsNull")]
    public void Evaluate_ClassType_CatchesExceptionsReturnsNull()
    {
        var module = new ExceptionTestModule();
        var expr = BackgroundResourceProcessing.Utils.FieldExpression<string>.Compile(
            "%nonExistentField ?? \"default\"",
            new(),
            typeof(ExceptionTestModule)
        );

        var result = expr.Evaluate(module);
        Assert.AreEqual("default", result);
    }

    [TestInfo("FieldExpressionExceptionsTests_Evaluate_NullableType_CatchesExceptionsReturnsNull")]
    public void Evaluate_NullableType_CatchesExceptionsReturnsNull()
    {
        var module = new ExceptionTestModule();
        var expr = BackgroundResourceProcessing.Utils.FieldExpression<double?>.Compile(
            "%nonExistentField ?? 99",
            new(),
            typeof(ExceptionTestModule)
        );

        var result = expr.Evaluate(module);
        Assert.AreEqual(99.0, result);
    }

    #endregion

    #region Error Recovery

    [TestInfo(
        "FieldExpressionExceptionsTests_ErrorRecovery_MultipleExpressionsOneFailsOneSucceeds"
    )]
    public void ErrorRecovery_MultipleExpressionsOneFailsOneSucceeds()
    {
        var module = new ExceptionTestModule { value = 10.0 };

        // First expression succeeds
        var expr1 = BackgroundResourceProcessing.Utils.FieldExpression<double>.Compile(
            "%value",
            new(),
            typeof(ExceptionTestModule)
        );
        Assert.AreEqual(10.0, expr1.Evaluate(module));

        // Second expression fails to compile
        try
        {
            BackgroundResourceProcessing.Utils.FieldExpression<double>.Compile(
                "invalid syntax!",
                new(),
                typeof(ExceptionTestModule)
            );
            Assert.Fail("Should have thrown");
        }
        catch (Expr.CompilationException)
        {
            // Expected
        }

        // Third expression should still work
        var expr3 = BackgroundResourceProcessing.Utils.FieldExpression<double>.Compile(
            "%value * 2",
            new(),
            typeof(ExceptionTestModule)
        );
        Assert.AreEqual(20.0, expr3.Evaluate(module));
    }

    #endregion

    #region Edge Cases

    [TestInfo("FieldExpressionExceptionsTests_EdgeCase_OnlyOperator")]
    public void EdgeCase_OnlyOperator()
    {
        Assert.ThrowsException<Expr.CompilationException>(() =>
            BackgroundResourceProcessing.Utils.FieldExpression<double>.Compile(
                "+",
                new(),
                typeof(ExceptionTestModule)
            )
        );
    }

    [TestInfo("FieldExpressionExceptionsTests_EdgeCase_MismatchedParentheses_TooManyClosing")]
    public void EdgeCase_MismatchedParentheses_TooManyClosing()
    {
        Assert.ThrowsException<Expr.CompilationException>(() =>
            BackgroundResourceProcessing.Utils.FieldExpression<double>.Compile(
                "(1 + 2))",
                new(),
                typeof(ExceptionTestModule)
            )
        );
    }

    [TestInfo("FieldExpressionExceptionsTests_EdgeCase_ComplexExpressionWithNullFields")]
    public void EdgeCase_ComplexExpressionWithNullFields()
    {
        var module = new ExceptionTestModule { nullObject = null, value = 5.0 };
        var expr = BackgroundResourceProcessing.Utils.FieldExpression<double>.Compile(
            "(%nullObject ?? %value) * 2",
            new(),
            typeof(ExceptionTestModule)
        );

        var result = expr.Evaluate(module);
        Assert.AreEqual(10.0, result);
    }

    #endregion
}
