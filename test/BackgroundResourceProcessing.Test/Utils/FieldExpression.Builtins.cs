using BackgroundResourceProcessing.Utils;

namespace BackgroundResourceProcessing.Test.Utils;

/// <summary>
/// Tests for builtin objects accessible in field expressions ($Math, $Infinity, etc.).
/// </summary>
[TestClass]
public sealed class FieldExpressionBuiltinsTests
{
    class BuiltinsTestModule : PartModule
    {
        public double value = 10.0;
    }

    #region $Infinity Constant

    [TestMethod]
    public void Infinity_Constant()
    {
        var expr = FieldExpression<double>.Compile("$Infinity", new(), typeof(BuiltinsTestModule));
        var result = expr.Evaluate(new BuiltinsTestModule());
        Assert.IsTrue(double.IsPositiveInfinity(result.Value));
    }

    [TestMethod]
    public void Infinity_InArithmetic()
    {
        var expr = FieldExpression<double>.Compile(
            "$Infinity + 1",
            new(),
            typeof(BuiltinsTestModule)
        );
        var result = expr.Evaluate(new BuiltinsTestModule());
        Assert.IsTrue(double.IsPositiveInfinity(result.Value));
    }

    [TestMethod]
    public void Infinity_InComparison()
    {
        var expr = FieldExpression<bool>.Compile(
            "1000000 < $Infinity",
            new(),
            typeof(BuiltinsTestModule)
        );
        Assert.IsTrue(expr.Evaluate(new BuiltinsTestModule()));
    }

    [TestMethod]
    public void Infinity_DivideByInfinity_ReturnsNaN()
    {
        var expr = FieldExpression<double>.Compile(
            "$Infinity / $Infinity",
            new(),
            typeof(BuiltinsTestModule)
        );
        var result = expr.Evaluate(new BuiltinsTestModule());
        Assert.IsTrue(double.IsNaN(result.Value));
    }

    [TestMethod]
    public void Infinity_MinusInfinity_ReturnsNaN()
    {
        var expr = FieldExpression<double>.Compile(
            "$Infinity - $Infinity",
            new(),
            typeof(BuiltinsTestModule)
        );
        var result = expr.Evaluate(new BuiltinsTestModule());
        Assert.IsTrue(double.IsNaN(result.Value));
    }

    #endregion

    #region $Math Access

    [TestMethod]
    public void Math_PIAccess()
    {
        var expr = FieldExpression<double>.Compile("$Math.PI", new(), typeof(BuiltinsTestModule));
        Assert.AreEqual(Math.PI, expr.Evaluate(new BuiltinsTestModule()).Value, 1e-10);
    }

    [TestMethod]
    public void Math_EAccess()
    {
        var expr = FieldExpression<double>.Compile("$Math.E", new(), typeof(BuiltinsTestModule));
        Assert.AreEqual(Math.E, expr.Evaluate(new BuiltinsTestModule()).Value, 1e-10);
    }

    [TestMethod]
    public void Math_FunctionCall()
    {
        var expr = FieldExpression<double>.Compile(
            "$Math.Sqrt(16)",
            new(),
            typeof(BuiltinsTestModule)
        );
        Assert.AreEqual(4.0, expr.Evaluate(new BuiltinsTestModule()).Value, 1e-10);
    }

    [TestMethod]
    public void Math_ChainedAccess()
    {
        var expr = FieldExpression<double>.Compile(
            "$Math.Max($Math.Abs(-5), $Math.Abs(3))",
            new(),
            typeof(BuiltinsTestModule)
        );
        Assert.AreEqual(5.0, expr.Evaluate(new BuiltinsTestModule()).Value, 1e-10);
    }

    #endregion

    #region Combined Builtins

    [TestMethod]
    public void Combined_MathWithInfinity()
    {
        var expr = FieldExpression<bool>.Compile(
            "$Math.Log($Infinity) == $Infinity",
            new(),
            typeof(BuiltinsTestModule)
        );
        Assert.IsTrue(expr.Evaluate(new BuiltinsTestModule()));
    }

    [TestMethod]
    public void Combined_InfinityInMathFunction()
    {
        var expr = FieldExpression<double>.Compile(
            "$Math.Min($Infinity, 100)",
            new(),
            typeof(BuiltinsTestModule)
        );
        Assert.AreEqual(100.0, expr.Evaluate(new BuiltinsTestModule()).Value, 1e-10);
    }

    [TestMethod]
    public void Combined_FieldsWithBuiltins()
    {
        var module = new BuiltinsTestModule { value = 5.0 };
        var expr = FieldExpression<double>.Compile(
            "$Math.Pow(%value, 2) * $Math.PI",
            new(),
            typeof(BuiltinsTestModule)
        );
        Assert.AreEqual(25.0 * Math.PI, expr.Evaluate(module).Value, 1e-10);
    }

    #endregion

    #region Null Coalescing with Builtins

    [TestMethod]
    public void NullCoalesce_WithInfinity()
    {
        var expr = FieldExpression<double>.Compile(
            "null ?? $Infinity",
            new(),
            typeof(BuiltinsTestModule)
        );
        var result = expr.Evaluate(new BuiltinsTestModule());
        Assert.IsTrue(double.IsPositiveInfinity(result.Value));
    }

    [TestMethod]
    public void NullCoalesce_WithMathConstant()
    {
        var expr = FieldExpression<double>.Compile(
            "null ?? $Math.E",
            new(),
            typeof(BuiltinsTestModule)
        );
        Assert.AreEqual(Math.E, expr.Evaluate(new BuiltinsTestModule()).Value, 1e-10);
    }

    #endregion

    #region Edge Cases

    [TestMethod]
    public void EdgeCase_InfinityComparison()
    {
        var expr = FieldExpression<bool>.Compile(
            "$Infinity == $Infinity",
            new(),
            typeof(BuiltinsTestModule)
        );
        Assert.IsTrue(expr.Evaluate(new BuiltinsTestModule()));
    }

    [TestMethod]
    public void EdgeCase_InfinityGreaterThanAll()
    {
        var expr = FieldExpression<bool>.Compile(
            "$Infinity > 1e308",
            new(),
            typeof(BuiltinsTestModule)
        );
        Assert.IsTrue(expr.Evaluate(new BuiltinsTestModule()));
    }

    [TestMethod]
    public void EdgeCase_MathConstantsInExpression()
    {
        var expr = FieldExpression<double>.Compile(
            "($Math.PI + $Math.E) / 2",
            new(),
            typeof(BuiltinsTestModule)
        );
        Assert.AreEqual(
            (Math.PI + Math.E) / 2,
            expr.Evaluate(new BuiltinsTestModule()).Value,
            1e-10
        );
    }

    #endregion
}
