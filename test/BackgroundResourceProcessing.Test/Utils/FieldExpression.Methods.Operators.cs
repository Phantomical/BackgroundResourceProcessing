using BackgroundResourceProcessing.Utils;

namespace BackgroundResourceProcessing.Test.Utils;

/// <summary>
/// Tests for runtime operator implementations in the Methods class.
/// These test the DoMultiply, DoDivide, DoAdd, DoSub, DoMod, DoUnaryMinus, DoUnaryPlus,
/// DoBoolNot, DoBitNot, and DoXor methods which handle operator execution at runtime.
/// </summary>
[TestClass]
public sealed class FieldExpressionMethodsOperatorsTests
{
    class OperatorTestModule : PartModule
    {
        public int intValue = 10;
        public double doubleValue = 5.5;
        public float floatValue = 3.5f;
        public long longValue = 100L;
        public byte byteValue = 8;
        public bool boolValue = true;
        public string stringValue = "hello";
    }

    #region Arithmetic Operators (DoMultiply, DoDivide, DoAdd, DoSub, DoMod)

    [TestMethod]
    public void DoMultiply_IntInt()
    {
        var module = new OperatorTestModule { intValue = 6 };
        var expr = FieldExpression<double>.Compile(
            "%intValue * 7",
            new(),
            typeof(OperatorTestModule)
        );
        Assert.AreEqual(42.0, expr.Evaluate(module));
    }

    [TestMethod]
    public void DoMultiply_DoubleDouble()
    {
        var module = new OperatorTestModule { doubleValue = 2.5 };
        var expr = FieldExpression<double>.Compile(
            "%doubleValue * 4.0",
            new(),
            typeof(OperatorTestModule)
        );
        Assert.AreEqual(10.0, expr.Evaluate(module));
    }

    [TestMethod]
    public void DoMultiply_MixedTypes()
    {
        var module = new OperatorTestModule { intValue = 5, floatValue = 2.5f };
        var expr = FieldExpression<double>.Compile(
            "%intValue * %floatValue",
            new(),
            typeof(OperatorTestModule)
        );
        Assert.AreEqual(12.5, expr.Evaluate(module));
    }

    [TestMethod]
    public void DoMultiply_ByZero()
    {
        var module = new OperatorTestModule { intValue = 42 };
        var expr = FieldExpression<double>.Compile(
            "%intValue * 0",
            new(),
            typeof(OperatorTestModule)
        );
        Assert.AreEqual(0.0, expr.Evaluate(module));
    }

    [TestMethod]
    public void DoDivide_IntInt()
    {
        var module = new OperatorTestModule { intValue = 20 };
        var expr = FieldExpression<double>.Compile(
            "%intValue / 4",
            new(),
            typeof(OperatorTestModule)
        );
        Assert.AreEqual(5.0, expr.Evaluate(module));
    }

    [TestMethod]
    public void DoDivide_IntInt_Fractional()
    {
        var module = new OperatorTestModule { intValue = 5 };
        var expr = FieldExpression<double>.Compile(
            "%intValue / 2",
            new(),
            typeof(OperatorTestModule)
        );
        Assert.AreEqual(2.5, expr.Evaluate(module));
    }

    [TestMethod]
    public void DoDivide_DoubleDouble()
    {
        var module = new OperatorTestModule { doubleValue = 10.0 };
        var expr = FieldExpression<double>.Compile(
            "%doubleValue / 2.5",
            new(),
            typeof(OperatorTestModule)
        );
        Assert.AreEqual(4.0, expr.Evaluate(module));
    }

    [TestMethod]
    public void DoDivide_ByZero_Infinity()
    {
        var module = new OperatorTestModule { doubleValue = 10.0 };
        var expr = FieldExpression<double>.Compile(
            "%doubleValue / 0",
            new(),
            typeof(OperatorTestModule)
        );
        Assert.IsTrue(double.IsPositiveInfinity(expr.Evaluate(module).Value));
    }

    [TestMethod]
    public void DoDivide_NegativeByZero_NegativeInfinity()
    {
        var module = new OperatorTestModule { doubleValue = -10.0 };
        var expr = FieldExpression<double>.Compile(
            "%doubleValue / 0",
            new(),
            typeof(OperatorTestModule)
        );
        Assert.IsTrue(double.IsNegativeInfinity(expr.Evaluate(module).Value));
    }

    [TestMethod]
    public void DoAdd_IntInt()
    {
        var module = new OperatorTestModule { intValue = 15 };
        var expr = FieldExpression<double>.Compile(
            "%intValue + 27",
            new(),
            typeof(OperatorTestModule)
        );
        Assert.AreEqual(42.0, expr.Evaluate(module));
    }

    [TestMethod]
    public void DoAdd_DoubleDouble()
    {
        var module = new OperatorTestModule { doubleValue = 3.14 };
        var expr = FieldExpression<double>.Compile(
            "%doubleValue + 2.86",
            new(),
            typeof(OperatorTestModule)
        );
        Assert.AreEqual(6.0, expr.Evaluate(module).Value, 1e-10);
    }

    [TestMethod]
    public void DoAdd_MixedTypes()
    {
        var module = new OperatorTestModule { intValue = 10, floatValue = 2.5f };
        var expr = FieldExpression<double>.Compile(
            "%intValue + %floatValue",
            new(),
            typeof(OperatorTestModule)
        );
        Assert.AreEqual(12.5, expr.Evaluate(module));
    }

    [TestMethod]
    public void DoSub_IntInt()
    {
        var module = new OperatorTestModule { intValue = 50 };
        var expr = FieldExpression<double>.Compile(
            "%intValue - 8",
            new(),
            typeof(OperatorTestModule)
        );
        Assert.AreEqual(42.0, expr.Evaluate(module));
    }

    [TestMethod]
    public void DoSub_DoubleDouble()
    {
        var module = new OperatorTestModule { doubleValue = 10.5 };
        var expr = FieldExpression<double>.Compile(
            "%doubleValue - 5.5",
            new(),
            typeof(OperatorTestModule)
        );
        Assert.AreEqual(5.0, expr.Evaluate(module));
    }

    [TestMethod]
    public void DoSub_ResultNegative()
    {
        var module = new OperatorTestModule { intValue = 10 };
        var expr = FieldExpression<double>.Compile(
            "%intValue - 20",
            new(),
            typeof(OperatorTestModule)
        );
        Assert.AreEqual(-10.0, expr.Evaluate(module));
    }

    [TestMethod]
    public void DoMod_IntInt()
    {
        var module = new OperatorTestModule { intValue = 17 };
        var expr = FieldExpression<double>.Compile(
            "%intValue % 5",
            new(),
            typeof(OperatorTestModule)
        );
        Assert.AreEqual(2.0, expr.Evaluate(module));
    }

    [TestMethod]
    public void DoMod_DoubleDouble()
    {
        var module = new OperatorTestModule { doubleValue = 7.5 };
        var expr = FieldExpression<double>.Compile(
            "%doubleValue % 2.5",
            new(),
            typeof(OperatorTestModule)
        );
        Assert.AreEqual(0.0, expr.Evaluate(module).Value, 1e-10);
    }

    [TestMethod]
    public void DoMod_ZeroRemainder()
    {
        var module = new OperatorTestModule { intValue = 20 };
        var expr = FieldExpression<double>.Compile(
            "%intValue % 4",
            new(),
            typeof(OperatorTestModule)
        );
        Assert.AreEqual(0.0, expr.Evaluate(module));
    }

    #endregion

    #region Unary Operators (DoUnaryMinus, DoUnaryPlus)

    [TestMethod]
    public void DoUnaryMinus_PositiveInt()
    {
        var module = new OperatorTestModule { intValue = 42 };
        var expr = FieldExpression<double>.Compile("-%intValue", new(), typeof(OperatorTestModule));
        Assert.AreEqual(-42.0, expr.Evaluate(module));
    }

    [TestMethod]
    public void DoUnaryMinus_NegativeInt()
    {
        var module = new OperatorTestModule { intValue = -42 };
        var expr = FieldExpression<double>.Compile("-%intValue", new(), typeof(OperatorTestModule));
        Assert.AreEqual(42.0, expr.Evaluate(module));
    }

    [TestMethod]
    public void DoUnaryMinus_Double()
    {
        var module = new OperatorTestModule { doubleValue = 3.14 };
        var expr = FieldExpression<double>.Compile(
            "-%doubleValue",
            new(),
            typeof(OperatorTestModule)
        );
        Assert.AreEqual(-3.14, expr.Evaluate(module));
    }

    [TestMethod]
    public void DoUnaryMinus_Zero()
    {
        var module = new OperatorTestModule { intValue = 0 };
        var expr = FieldExpression<double>.Compile("-%intValue", new(), typeof(OperatorTestModule));
        Assert.AreEqual(0.0, expr.Evaluate(module));
    }

    [TestMethod]
    public void DoUnaryPlus_PositiveInt()
    {
        var module = new OperatorTestModule { intValue = 42 };
        var expr = FieldExpression<double>.Compile("+%intValue", new(), typeof(OperatorTestModule));
        Assert.AreEqual(42.0, expr.Evaluate(module));
    }

    [TestMethod]
    public void DoUnaryPlus_NegativeInt()
    {
        var module = new OperatorTestModule { intValue = -42 };
        var expr = FieldExpression<double>.Compile("+%intValue", new(), typeof(OperatorTestModule));
        Assert.AreEqual(-42.0, expr.Evaluate(module));
    }

    [TestMethod]
    public void DoUnaryPlus_Double()
    {
        var module = new OperatorTestModule { doubleValue = 3.14 };
        var expr = FieldExpression<double>.Compile(
            "+%doubleValue",
            new(),
            typeof(OperatorTestModule)
        );
        Assert.AreEqual(3.14, expr.Evaluate(module));
    }

    #endregion

    #region Boolean Operators (DoBoolNot)

    [TestMethod]
    public void DoBoolNot_True()
    {
        var module = new OperatorTestModule { boolValue = true };
        var expr = FieldExpression<bool>.Compile("!%boolValue", new(), typeof(OperatorTestModule));
        Assert.AreEqual(false, expr.Evaluate(module));
    }

    [TestMethod]
    public void DoBoolNot_False()
    {
        var module = new OperatorTestModule { boolValue = false };
        var expr = FieldExpression<bool>.Compile("!%boolValue", new(), typeof(OperatorTestModule));
        Assert.AreEqual(true, expr.Evaluate(module));
    }

    [TestMethod]
    public void DoBoolNot_Expression()
    {
        var module = new OperatorTestModule { intValue = 10 };
        var expr = FieldExpression<bool>.Compile(
            "!(%intValue > 5)",
            new(),
            typeof(OperatorTestModule)
        );
        Assert.AreEqual(false, expr.Evaluate(module));
    }

    [TestMethod]
    public void DoBoolNot_DoubleNegation()
    {
        var module = new OperatorTestModule { boolValue = true };
        var expr = FieldExpression<bool>.Compile("!!%boolValue", new(), typeof(OperatorTestModule));
        Assert.AreEqual(true, expr.Evaluate(module));
    }

    #endregion

    #region Bitwise Operators (DoBitNot, DoXor)

    [TestMethod]
    public void DoXor_BooleanValues()
    {
        var expr = FieldExpression<bool>.Compile("true ^ false", new(), typeof(OperatorTestModule));
        Assert.AreEqual(true, expr.Evaluate(new OperatorTestModule()));
    }

    [TestMethod]
    public void DoXor_BothTrue()
    {
        var expr = FieldExpression<bool>.Compile("true ^ true", new(), typeof(OperatorTestModule));
        Assert.AreEqual(false, expr.Evaluate(new OperatorTestModule()));
    }

    [TestMethod]
    public void DoXor_BothFalse()
    {
        var expr = FieldExpression<bool>.Compile(
            "false ^ false",
            new(),
            typeof(OperatorTestModule)
        );
        Assert.AreEqual(false, expr.Evaluate(new OperatorTestModule()));
    }

    #endregion

    #region Type Promotion and Mixed Operations

    [TestMethod]
    public void TypePromotion_ByteToDouble()
    {
        var module = new OperatorTestModule { byteValue = 10 };
        var expr = FieldExpression<double>.Compile(
            "%byteValue * 2.5",
            new(),
            typeof(OperatorTestModule)
        );
        Assert.AreEqual(25.0, expr.Evaluate(module));
    }

    [TestMethod]
    public void TypePromotion_LongToDouble()
    {
        var module = new OperatorTestModule { longValue = 100 };
        var expr = FieldExpression<double>.Compile(
            "%longValue / 4",
            new(),
            typeof(OperatorTestModule)
        );
        Assert.AreEqual(25.0, expr.Evaluate(module));
    }

    [TestMethod]
    public void TypePromotion_FloatToDouble()
    {
        var module = new OperatorTestModule { floatValue = 2.5f };
        var expr = FieldExpression<double>.Compile(
            "%floatValue + 1.5",
            new(),
            typeof(OperatorTestModule)
        );
        Assert.AreEqual(4.0, expr.Evaluate(module));
    }

    [TestMethod]
    public void MixedOperation_AllNumericTypes()
    {
        var module = new OperatorTestModule
        {
            byteValue = 1,
            intValue = 2,
            floatValue = 3.0f,
            doubleValue = 4.0,
        };
        var expr = FieldExpression<double>.Compile(
            "%byteValue + %intValue + %floatValue + %doubleValue",
            new(),
            typeof(OperatorTestModule)
        );
        Assert.AreEqual(10.0, expr.Evaluate(module));
    }

    #endregion

    #region Complex Expressions with Operators

    [TestMethod]
    public void ComplexExpression_MultipleOperators()
    {
        var module = new OperatorTestModule { intValue = 5, doubleValue = 2.0 };
        var expr = FieldExpression<double>.Compile(
            "(%intValue + %doubleValue) * 2 - 3",
            new(),
            typeof(OperatorTestModule)
        );
        Assert.AreEqual(11.0, expr.Evaluate(module));
    }

    [TestMethod]
    public void ComplexExpression_NestedUnaryOperators()
    {
        var module = new OperatorTestModule { intValue = 5 };
        var expr = FieldExpression<double>.Compile(
            "-(-(%intValue))",
            new(),
            typeof(OperatorTestModule)
        );
        Assert.AreEqual(5.0, expr.Evaluate(module));
    }

    [TestMethod]
    public void ComplexExpression_MixedUnaryAndBinary()
    {
        var module = new OperatorTestModule { intValue = 10, doubleValue = 3.0 };
        var expr = FieldExpression<double>.Compile(
            "-%intValue + +%doubleValue",
            new(),
            typeof(OperatorTestModule)
        );
        Assert.AreEqual(-7.0, expr.Evaluate(module));
    }

    #endregion

    #region Edge Cases

    [TestMethod]
    public void EdgeCase_DivisionResultingInInfinity()
    {
        var module = new OperatorTestModule { doubleValue = 1e308 };
        var expr = FieldExpression<double>.Compile(
            "%doubleValue * 10",
            new(),
            typeof(OperatorTestModule)
        );
        var result = expr.Evaluate(module);
        Assert.IsTrue(double.IsPositiveInfinity(result.Value) || result.Value > 1e308);
    }

    [TestMethod]
    public void EdgeCase_VerySmallResult()
    {
        var module = new OperatorTestModule { doubleValue = 1e-300 };
        var expr = FieldExpression<double>.Compile(
            "%doubleValue / 1e10",
            new(),
            typeof(OperatorTestModule)
        );
        var result = expr.Evaluate(module);
        Assert.IsTrue(result >= 0);
    }

    [TestMethod]
    public void EdgeCase_ModuloWithNegative()
    {
        var module = new OperatorTestModule { intValue = -17 };
        var expr = FieldExpression<double>.Compile(
            "%intValue % 5",
            new(),
            typeof(OperatorTestModule)
        );
        var result = expr.Evaluate(module);
        // C# modulo with negative dividends
        Assert.AreEqual(-2.0, result);
    }

    [TestMethod]
    public void EdgeCase_UnaryMinusOnZero()
    {
        var module = new OperatorTestModule { intValue = 0 };
        var expr = FieldExpression<double>.Compile("-%intValue", new(), typeof(OperatorTestModule));
        Assert.AreEqual(0.0, expr.Evaluate(module));
    }

    #endregion
}
