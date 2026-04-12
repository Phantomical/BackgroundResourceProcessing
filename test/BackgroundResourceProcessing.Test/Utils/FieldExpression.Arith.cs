using BackgroundResourceProcessing.Utils;
using KSP.Testing;

namespace BackgroundResourceProcessing.Test.Utils;

/// <summary>
/// Tests for arithmetic expressions in field expressions.
/// </summary>
public sealed class FieldExpressionArithTests : BRPTestBase
{
    class ArithTestModule : PartModule
    {
        public double value = 10.0;
        public double zero = 0.0;
        public double? nullValue = null;
        public float floatValue = 5.5f;
        public int intValue = 42;
        public string stringNumber = "3.14";
        public string invalidString = "not a number";
        public double[] array = [1.0, 2.0, 3.0];
    }

    #region Basic Arithmetic Operations

    [TestInfo("FieldExpressionArithTests_Addition_Constants")]
    public void Addition_Constants()
    {
        var expr = FieldExpression<double>.Compile("1 + 2", new(), typeof(ArithTestModule));
        Assert.AreEqual(3.0, expr.Evaluate(new ArithTestModule()));
    }

    [TestInfo("FieldExpressionArithTests_Addition_Fields")]
    public void Addition_Fields()
    {
        var module = new ArithTestModule { value = 10.0 };
        var expr = FieldExpression<double>.Compile("%value + 5", new(), typeof(ArithTestModule));
        Assert.AreEqual(15.0, expr.Evaluate(module));
    }

    [TestInfo("FieldExpressionArithTests_Subtraction_Constants")]
    public void Subtraction_Constants()
    {
        var expr = FieldExpression<double>.Compile("10 - 3", new(), typeof(ArithTestModule));
        Assert.AreEqual(7.0, expr.Evaluate(new ArithTestModule()));
    }

    [TestInfo("FieldExpressionArithTests_Subtraction_Fields")]
    public void Subtraction_Fields()
    {
        var module = new ArithTestModule { value = 20.0 };
        var expr = FieldExpression<double>.Compile("%value - 8", new(), typeof(ArithTestModule));
        Assert.AreEqual(12.0, expr.Evaluate(module));
    }

    [TestInfo("FieldExpressionArithTests_Multiplication_Constants")]
    public void Multiplication_Constants()
    {
        var expr = FieldExpression<double>.Compile("3 * 4", new(), typeof(ArithTestModule));
        Assert.AreEqual(12.0, expr.Evaluate(new ArithTestModule()));
    }

    [TestInfo("FieldExpressionArithTests_Multiplication_Fields")]
    public void Multiplication_Fields()
    {
        var module = new ArithTestModule { value = 7.0 };
        var expr = FieldExpression<double>.Compile("%value * 3", new(), typeof(ArithTestModule));
        Assert.AreEqual(21.0, expr.Evaluate(module));
    }

    [TestInfo("FieldExpressionArithTests_Division_Constants")]
    public void Division_Constants()
    {
        var expr = FieldExpression<double>.Compile("15 / 3", new(), typeof(ArithTestModule));
        Assert.AreEqual(5.0, expr.Evaluate(new ArithTestModule()));
    }

    [TestInfo("FieldExpressionArithTests_Division_Fields")]
    public void Division_Fields()
    {
        var module = new ArithTestModule { value = 20.0 };
        var expr = FieldExpression<double>.Compile("%value / 4", new(), typeof(ArithTestModule));
        Assert.AreEqual(5.0, expr.Evaluate(module));
    }

    [TestInfo("FieldExpressionArithTests_Division_ByZero_ReturnsInfinity")]
    public void Division_ByZero_ReturnsInfinity()
    {
        var module = new ArithTestModule { zero = 0.0 };
        var expr = FieldExpression<double>.Compile("10 / %zero", new(), typeof(ArithTestModule));
        Assert.AreEqual(double.PositiveInfinity, expr.Evaluate(module));
    }

    [TestInfo("FieldExpressionArithTests_Modulo_Constants")]
    public void Modulo_Constants()
    {
        var expr = FieldExpression<double>.Compile("17 % 5", new(), typeof(ArithTestModule));
        Assert.AreEqual(2.0, expr.Evaluate(new ArithTestModule()));
    }

    [TestInfo("FieldExpressionArithTests_Modulo_Fields")]
    public void Modulo_Fields()
    {
        var module = new ArithTestModule { value = 23.0 };
        var expr = FieldExpression<double>.Compile("%value % 7", new(), typeof(ArithTestModule));
        Assert.AreEqual(2.0, expr.Evaluate(module));
    }

    [TestInfo("FieldExpressionArithTests_Modulo_FloatingPoint")]
    public void Modulo_FloatingPoint()
    {
        var expr = FieldExpression<double>.Compile("7.5 % 2.5", new(), typeof(ArithTestModule));
        Assert.AreEqual(0.0, expr.Evaluate(new ArithTestModule()));
    }

    #endregion

    #region Unary Operations

    [TestInfo("FieldExpressionArithTests_UnaryMinus_Constant")]
    public void UnaryMinus_Constant()
    {
        var expr = FieldExpression<double>.Compile("-5", new(), typeof(ArithTestModule));
        Assert.AreEqual(-5.0, expr.Evaluate(new ArithTestModule()));
    }

    [TestInfo("FieldExpressionArithTests_UnaryMinus_Field")]
    public void UnaryMinus_Field()
    {
        var module = new ArithTestModule { value = 10.0 };
        var expr = FieldExpression<double>.Compile("-%value", new(), typeof(ArithTestModule));
        Assert.AreEqual(-10.0, expr.Evaluate(module));
    }

    [TestInfo("FieldExpressionArithTests_UnaryMinus_NegativeValue")]
    public void UnaryMinus_NegativeValue()
    {
        var module = new ArithTestModule { value = -7.5 };
        var expr = FieldExpression<double>.Compile("-%value", new(), typeof(ArithTestModule));
        Assert.AreEqual(7.5, expr.Evaluate(module));
    }

    [TestInfo("FieldExpressionArithTests_UnaryPlus_Constant")]
    public void UnaryPlus_Constant()
    {
        var expr = FieldExpression<double>.Compile("+5", new(), typeof(ArithTestModule));
        Assert.AreEqual(5.0, expr.Evaluate(new ArithTestModule()));
    }

    [TestInfo("FieldExpressionArithTests_UnaryPlus_Field")]
    public void UnaryPlus_Field()
    {
        var module = new ArithTestModule { value = 10.0 };
        var expr = FieldExpression<double>.Compile("+%value", new(), typeof(ArithTestModule));
        Assert.AreEqual(10.0, expr.Evaluate(module));
    }

    [TestInfo("FieldExpressionArithTests_UnaryMinus_Expression")]
    public void UnaryMinus_Expression()
    {
        var expr = FieldExpression<double>.Compile("-(3 + 4)", new(), typeof(ArithTestModule));
        Assert.AreEqual(-7.0, expr.Evaluate(new ArithTestModule()));
    }

    #endregion

    #region Operator Precedence

    [TestInfo("FieldExpressionArithTests_Precedence_MultiplicationBeforeAddition")]
    public void Precedence_MultiplicationBeforeAddition()
    {
        var expr = FieldExpression<double>.Compile("2 + 3 * 4", new(), typeof(ArithTestModule));
        Assert.AreEqual(14.0, expr.Evaluate(new ArithTestModule()));
    }

    [TestInfo("FieldExpressionArithTests_Precedence_DivisionBeforeSubtraction")]
    public void Precedence_DivisionBeforeSubtraction()
    {
        var expr = FieldExpression<double>.Compile("20 - 10 / 2", new(), typeof(ArithTestModule));
        Assert.AreEqual(15.0, expr.Evaluate(new ArithTestModule()));
    }

    [TestInfo("FieldExpressionArithTests_Precedence_ModuloSameAsMultiplication")]
    public void Precedence_ModuloSameAsMultiplication()
    {
        var expr = FieldExpression<double>.Compile("10 + 7 % 3", new(), typeof(ArithTestModule));
        Assert.AreEqual(11.0, expr.Evaluate(new ArithTestModule()));
    }

    [TestInfo("FieldExpressionArithTests_Precedence_UnaryBeforeBinary")]
    public void Precedence_UnaryBeforeBinary()
    {
        var expr = FieldExpression<double>.Compile("-2 * 3", new(), typeof(ArithTestModule));
        Assert.AreEqual(-6.0, expr.Evaluate(new ArithTestModule()));
    }

    [TestInfo("FieldExpressionArithTests_Precedence_Parentheses")]
    public void Precedence_Parentheses()
    {
        var expr = FieldExpression<double>.Compile("(2 + 3) * 4", new(), typeof(ArithTestModule));
        Assert.AreEqual(20.0, expr.Evaluate(new ArithTestModule()));
    }

    [TestInfo("FieldExpressionArithTests_Precedence_NestedParentheses")]
    public void Precedence_NestedParentheses()
    {
        var expr = FieldExpression<double>.Compile(
            "((2 + 3) * 4) - 5",
            new(),
            typeof(ArithTestModule)
        );
        Assert.AreEqual(15.0, expr.Evaluate(new ArithTestModule()));
    }

    [TestInfo("FieldExpressionArithTests_Precedence_ComplexExpression")]
    public void Precedence_ComplexExpression()
    {
        var expr = FieldExpression<double>.Compile(
            "2 + 3 * 4 - 10 / 2",
            new(),
            typeof(ArithTestModule)
        );
        Assert.AreEqual(9.0, expr.Evaluate(new ArithTestModule()));
    }

    #endregion

    #region Null Propagation

    [TestInfo("FieldExpressionArithTests_NullPropagation_Addition")]
    public void NullPropagation_Addition()
    {
        var module = new ArithTestModule { nullValue = null };
        var expr = FieldExpression<double?>.Compile(
            "%nullValue + 5",
            new(),
            typeof(ArithTestModule)
        );
        Assert.IsNull(expr.Evaluate(module));
    }

    [TestInfo("FieldExpressionArithTests_NullPropagation_Subtraction")]
    public void NullPropagation_Subtraction()
    {
        var module = new ArithTestModule { nullValue = null };
        var expr = FieldExpression<double?>.Compile(
            "10 - %nullValue",
            new(),
            typeof(ArithTestModule)
        );
        Assert.IsNull(expr.Evaluate(module));
    }

    [TestInfo("FieldExpressionArithTests_NullPropagation_Multiplication")]
    public void NullPropagation_Multiplication()
    {
        var module = new ArithTestModule { nullValue = null };
        var expr = FieldExpression<double?>.Compile(
            "%nullValue * 3",
            new(),
            typeof(ArithTestModule)
        );
        Assert.IsNull(expr.Evaluate(module));
    }

    [TestInfo("FieldExpressionArithTests_NullPropagation_Division")]
    public void NullPropagation_Division()
    {
        var module = new ArithTestModule { nullValue = null };
        var expr = FieldExpression<double?>.Compile(
            "%nullValue / 2",
            new(),
            typeof(ArithTestModule)
        );
        Assert.IsNull(expr.Evaluate(module));
    }

    [TestInfo("FieldExpressionArithTests_NullPropagation_Modulo")]
    public void NullPropagation_Modulo()
    {
        var module = new ArithTestModule { nullValue = null };
        var expr = FieldExpression<double?>.Compile(
            "%nullValue % 5",
            new(),
            typeof(ArithTestModule)
        );
        Assert.IsNull(expr.Evaluate(module));
    }

    [TestInfo("FieldExpressionArithTests_NullPropagation_UnaryMinus")]
    public void NullPropagation_UnaryMinus()
    {
        var module = new ArithTestModule { nullValue = null };
        var expr = FieldExpression<double?>.Compile("-%nullValue", new(), typeof(ArithTestModule));
        Assert.IsNull(expr.Evaluate(module));
    }

    [TestInfo("FieldExpressionArithTests_NullPropagation_BothOperands")]
    public void NullPropagation_BothOperands()
    {
        var module = new ArithTestModule { nullValue = null };
        var expr = FieldExpression<double?>.Compile(
            "%nullValue + %nullValue",
            new(),
            typeof(ArithTestModule)
        );
        Assert.IsNull(expr.Evaluate(module));
    }

    [TestInfo("FieldExpressionArithTests_NullPropagation_ComplexExpression")]
    public void NullPropagation_ComplexExpression()
    {
        var module = new ArithTestModule { nullValue = null, value = 10.0 };
        var expr = FieldExpression<double?>.Compile(
            "(%value + %nullValue) * 2",
            new(),
            typeof(ArithTestModule)
        );
        Assert.IsNull(expr.Evaluate(module));
    }

    [TestInfo("FieldExpressionArithTests_NullCoalesce_WithArithmetic")]
    public void NullCoalesce_WithArithmetic()
    {
        var module = new ArithTestModule { nullValue = null };
        var expr = FieldExpression<double>.Compile(
            "(%nullValue ?? 5) + 3",
            new(),
            typeof(ArithTestModule)
        );
        Assert.AreEqual(8.0, expr.Evaluate(module));
    }

    #endregion

    #region Type Coercion

    [TestInfo("FieldExpressionArithTests_Coercion_FloatToDouble")]
    public void Coercion_FloatToDouble()
    {
        var module = new ArithTestModule { floatValue = 5.5f };
        var expr = FieldExpression<double>.Compile(
            "%floatValue + 2",
            new(),
            typeof(ArithTestModule)
        );
        Assert.AreEqual(7.5, expr.Evaluate(module));
    }

    [TestInfo("FieldExpressionArithTests_Coercion_IntToDouble")]
    public void Coercion_IntToDouble()
    {
        var module = new ArithTestModule { intValue = 42 };
        var expr = FieldExpression<double>.Compile("%intValue * 2", new(), typeof(ArithTestModule));
        Assert.AreEqual(84.0, expr.Evaluate(module));
    }

    [TestInfo("FieldExpressionArithTests_Coercion_IntDivision_ReturnsDouble")]
    public void Coercion_IntDivision_ReturnsDouble()
    {
        var module = new ArithTestModule { intValue = 5 };
        var expr = FieldExpression<double>.Compile("%intValue / 2", new(), typeof(ArithTestModule));
        Assert.AreEqual(2.5, expr.Evaluate(module));
    }

    [TestInfo("FieldExpressionArithTests_Coercion_StringToDouble")]
    public void Coercion_StringToDouble()
    {
        var module = new ArithTestModule { stringNumber = "3.14" };
        var expr = FieldExpression<double>.Compile(
            "%stringNumber * 2",
            new(),
            typeof(ArithTestModule)
        );
        Assert.AreEqual(6.28, expr.Evaluate(module));
    }

    [TestInfo("FieldExpressionArithTests_Coercion_MixedTypes")]
    public void Coercion_MixedTypes()
    {
        var module = new ArithTestModule { floatValue = 3.0f, intValue = 2 };
        var expr = FieldExpression<double>.Compile(
            "%floatValue + %intValue",
            new(),
            typeof(ArithTestModule)
        );
        Assert.AreEqual(5.0, expr.Evaluate(module));
    }

    #endregion

    #region Complex Expressions

    [TestInfo("FieldExpressionArithTests_Complex_NestedArithmetic")]
    public void Complex_NestedArithmetic()
    {
        var module = new ArithTestModule { value = 10.0 };
        var expr = FieldExpression<double>.Compile(
            "((%value + 5) * 2 - 10) / 4",
            new(),
            typeof(ArithTestModule)
        );
        Assert.AreEqual(5.0, expr.Evaluate(module));
    }

    [TestInfo("FieldExpressionArithTests_Complex_WithMultipleFields")]
    public void Complex_WithMultipleFields()
    {
        var module = new ArithTestModule
        {
            value = 10.0,
            floatValue = 5.0f,
            intValue = 2,
        };
        var expr = FieldExpression<double>.Compile(
            "%value + %floatValue * %intValue",
            new(),
            typeof(ArithTestModule)
        );
        Assert.AreEqual(20.0, expr.Evaluate(module));
    }

    [TestInfo("FieldExpressionArithTests_Complex_WithUnaryOperators")]
    public void Complex_WithUnaryOperators()
    {
        var module = new ArithTestModule { value = 5.0 };
        var expr = FieldExpression<double>.Compile(
            "-%value + -(-10)",
            new(),
            typeof(ArithTestModule)
        );
        Assert.AreEqual(5.0, expr.Evaluate(module));
    }

    [TestInfo("FieldExpressionArithTests_Complex_ChainedOperations")]
    public void Complex_ChainedOperations()
    {
        var expr = FieldExpression<double>.Compile(
            "1 + 2 + 3 + 4 + 5",
            new(),
            typeof(ArithTestModule)
        );
        Assert.AreEqual(15.0, expr.Evaluate(new ArithTestModule()));
    }

    [TestInfo("FieldExpressionArithTests_Complex_ChainedMultiplication")]
    public void Complex_ChainedMultiplication()
    {
        var expr = FieldExpression<double>.Compile("2 * 3 * 4", new(), typeof(ArithTestModule));
        Assert.AreEqual(24.0, expr.Evaluate(new ArithTestModule()));
    }

    [TestInfo("FieldExpressionArithTests_Complex_WithArrayAccess")]
    public void Complex_WithArrayAccess()
    {
        var module = new ArithTestModule { array = [10.0, 20.0, 30.0] };
        var expr = FieldExpression<double>.Compile(
            "%array[0] + %array[1] * 2",
            new(),
            typeof(ArithTestModule)
        );
        Assert.AreEqual(50.0, expr.Evaluate(module));
    }

    #endregion

    #region Edge Cases

    [TestInfo("FieldExpressionArithTests_EdgeCase_NegativeNumbers")]
    public void EdgeCase_NegativeNumbers()
    {
        var expr = FieldExpression<double>.Compile("-5 + -3", new(), typeof(ArithTestModule));
        Assert.AreEqual(-8.0, expr.Evaluate(new ArithTestModule()));
    }

    [TestInfo("FieldExpressionArithTests_EdgeCase_NegativeMultiplication")]
    public void EdgeCase_NegativeMultiplication()
    {
        var expr = FieldExpression<double>.Compile("-5 * -3", new(), typeof(ArithTestModule));
        Assert.AreEqual(15.0, expr.Evaluate(new ArithTestModule()));
    }

    [TestInfo("FieldExpressionArithTests_EdgeCase_ZeroMultiplication")]
    public void EdgeCase_ZeroMultiplication()
    {
        var module = new ArithTestModule { value = 100.0, zero = 0.0 };
        var expr = FieldExpression<double>.Compile(
            "%value * %zero",
            new(),
            typeof(ArithTestModule)
        );
        Assert.AreEqual(0.0, expr.Evaluate(module));
    }

    [TestInfo("FieldExpressionArithTests_EdgeCase_ZeroDivision")]
    public void EdgeCase_ZeroDivision()
    {
        var module = new ArithTestModule { zero = 0.0 };
        var expr = FieldExpression<double>.Compile("%zero / 5", new(), typeof(ArithTestModule));
        Assert.AreEqual(0.0, expr.Evaluate(module));
    }

    [TestInfo("FieldExpressionArithTests_EdgeCase_VeryLargeNumbers")]
    public void EdgeCase_VeryLargeNumbers()
    {
        var expr = FieldExpression<double>.Compile("1e308 + 1e308", new(), typeof(ArithTestModule));
        Assert.AreEqual(double.PositiveInfinity, expr.Evaluate(new ArithTestModule()));
    }

    [TestInfo("FieldExpressionArithTests_EdgeCase_VerySmallNumbers")]
    public void EdgeCase_VerySmallNumbers()
    {
        var expr = FieldExpression<double>.Compile(
            "1e-300 * 1e-300",
            new(),
            typeof(ArithTestModule)
        );
        Assert.AreEqual(0.0, expr.Evaluate(new ArithTestModule()));
    }

    [TestInfo("FieldExpressionArithTests_EdgeCase_ScientificNotation")]
    public void EdgeCase_ScientificNotation()
    {
        var expr = FieldExpression<double>.Compile("1e3 + 2e2", new(), typeof(ArithTestModule));
        Assert.AreEqual(1200.0, expr.Evaluate(new ArithTestModule()));
    }

    [TestInfo("FieldExpressionArithTests_EdgeCase_NegativeScientificNotation")]
    public void EdgeCase_NegativeScientificNotation()
    {
        var expr = FieldExpression<double>.Compile("1e-3 * 1e3", new(), typeof(ArithTestModule));
        Assert.AreEqual(1.0, expr.Evaluate(new ArithTestModule()));
    }

    #endregion

    #region Associativity

    [TestInfo("FieldExpressionArithTests_Associativity_LeftToRight_Addition")]
    public void Associativity_LeftToRight_Addition()
    {
        var expr = FieldExpression<double>.Compile("10 - 5 - 2", new(), typeof(ArithTestModule));
        Assert.AreEqual(3.0, expr.Evaluate(new ArithTestModule()));
    }

    [TestInfo("FieldExpressionArithTests_Associativity_LeftToRight_Division")]
    public void Associativity_LeftToRight_Division()
    {
        var expr = FieldExpression<double>.Compile("20 / 4 / 2", new(), typeof(ArithTestModule));
        Assert.AreEqual(2.5, expr.Evaluate(new ArithTestModule()));
    }

    [TestInfo("FieldExpressionArithTests_Associativity_WithParentheses")]
    public void Associativity_WithParentheses()
    {
        var expr = FieldExpression<double>.Compile("20 / (4 / 2)", new(), typeof(ArithTestModule));
        Assert.AreEqual(10.0, expr.Evaluate(new ArithTestModule()));
    }

    #endregion
}
