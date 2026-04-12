using BackgroundResourceProcessing.Utils;
using KSP.Testing;

namespace BackgroundResourceProcessing.Test.Utils;

/// <summary>
/// Tests for boolean/logical expressions in field expressions.
/// </summary>
public sealed class FieldExpressionLogicTests : BRPTestBase
{
    class LogicTestModule : PartModule
    {
        public bool trueValue = true;
        public bool falseValue = false;
        public bool? nullBool = null;
        public string trueString = "true";
        public string falseString = "false";
        public string emptyString = "";
        public string nullString = null;
        public int intValue = 42;
        public int zero = 0;
        public double doubleValue = 3.14;
        public object nullObject = null;
        public object nonNullObject = new();
    }

    #region Basic Boolean Operators

    [TestInfo("FieldExpressionLogicTests_LogicalNot_True")]
    public void LogicalNot_True()
    {
        var expr = FieldExpression<bool>.Compile("!true", new(), typeof(LogicTestModule));
        Assert.AreEqual(false, expr.Evaluate(new LogicTestModule()));
    }

    [TestInfo("FieldExpressionLogicTests_LogicalNot_False")]
    public void LogicalNot_False()
    {
        var expr = FieldExpression<bool>.Compile("!false", new(), typeof(LogicTestModule));
        Assert.AreEqual(true, expr.Evaluate(new LogicTestModule()));
    }

    [TestInfo("FieldExpressionLogicTests_LogicalNot_Field_True")]
    public void LogicalNot_Field_True()
    {
        var module = new LogicTestModule { trueValue = true };
        var expr = FieldExpression<bool>.Compile("!%trueValue", new(), typeof(LogicTestModule));
        Assert.AreEqual(false, expr.Evaluate(module));
    }

    [TestInfo("FieldExpressionLogicTests_LogicalNot_Field_False")]
    public void LogicalNot_Field_False()
    {
        var module = new LogicTestModule { falseValue = false };
        var expr = FieldExpression<bool>.Compile("!%falseValue", new(), typeof(LogicTestModule));
        Assert.AreEqual(true, expr.Evaluate(module));
    }

    [TestInfo("FieldExpressionLogicTests_LogicalAnd_TrueTrue")]
    public void LogicalAnd_TrueTrue()
    {
        var expr = FieldExpression<bool>.Compile("true && true", new(), typeof(LogicTestModule));
        Assert.AreEqual(true, expr.Evaluate(new LogicTestModule()));
    }

    [TestInfo("FieldExpressionLogicTests_LogicalAnd_TrueFalse")]
    public void LogicalAnd_TrueFalse()
    {
        var expr = FieldExpression<bool>.Compile("true && false", new(), typeof(LogicTestModule));
        Assert.AreEqual(false, expr.Evaluate(new LogicTestModule()));
    }

    [TestInfo("FieldExpressionLogicTests_LogicalAnd_FalseTrue")]
    public void LogicalAnd_FalseTrue()
    {
        var expr = FieldExpression<bool>.Compile("false && true", new(), typeof(LogicTestModule));
        Assert.AreEqual(false, expr.Evaluate(new LogicTestModule()));
    }

    [TestInfo("FieldExpressionLogicTests_LogicalAnd_FalseFalse")]
    public void LogicalAnd_FalseFalse()
    {
        var expr = FieldExpression<bool>.Compile("false && false", new(), typeof(LogicTestModule));
        Assert.AreEqual(false, expr.Evaluate(new LogicTestModule()));
    }

    [TestInfo("FieldExpressionLogicTests_LogicalOr_TrueTrue")]
    public void LogicalOr_TrueTrue()
    {
        var expr = FieldExpression<bool>.Compile("true || true", new(), typeof(LogicTestModule));
        Assert.AreEqual(true, expr.Evaluate(new LogicTestModule()));
    }

    [TestInfo("FieldExpressionLogicTests_LogicalOr_TrueFalse")]
    public void LogicalOr_TrueFalse()
    {
        var expr = FieldExpression<bool>.Compile("true || false", new(), typeof(LogicTestModule));
        Assert.AreEqual(true, expr.Evaluate(new LogicTestModule()));
    }

    [TestInfo("FieldExpressionLogicTests_LogicalOr_FalseTrue")]
    public void LogicalOr_FalseTrue()
    {
        var expr = FieldExpression<bool>.Compile("false || true", new(), typeof(LogicTestModule));
        Assert.AreEqual(true, expr.Evaluate(new LogicTestModule()));
    }

    [TestInfo("FieldExpressionLogicTests_LogicalOr_FalseFalse")]
    public void LogicalOr_FalseFalse()
    {
        var expr = FieldExpression<bool>.Compile("false || false", new(), typeof(LogicTestModule));
        Assert.AreEqual(false, expr.Evaluate(new LogicTestModule()));
    }

    [TestInfo("FieldExpressionLogicTests_LogicalXor_TrueTrue")]
    public void LogicalXor_TrueTrue()
    {
        var expr = FieldExpression<bool>.Compile("true ^ true", new(), typeof(LogicTestModule));
        Assert.AreEqual(false, expr.Evaluate(new LogicTestModule()));
    }

    [TestInfo("FieldExpressionLogicTests_LogicalXor_TrueFalse")]
    public void LogicalXor_TrueFalse()
    {
        var expr = FieldExpression<bool>.Compile("true ^ false", new(), typeof(LogicTestModule));
        Assert.AreEqual(true, expr.Evaluate(new LogicTestModule()));
    }

    [TestInfo("FieldExpressionLogicTests_LogicalXor_FalseTrue")]
    public void LogicalXor_FalseTrue()
    {
        var expr = FieldExpression<bool>.Compile("false ^ true", new(), typeof(LogicTestModule));
        Assert.AreEqual(true, expr.Evaluate(new LogicTestModule()));
    }

    [TestInfo("FieldExpressionLogicTests_LogicalXor_FalseFalse")]
    public void LogicalXor_FalseFalse()
    {
        var expr = FieldExpression<bool>.Compile("false ^ false", new(), typeof(LogicTestModule));
        Assert.AreEqual(false, expr.Evaluate(new LogicTestModule()));
    }

    #endregion

    #region Boolean Coercion - Null Values

    [TestInfo("FieldExpressionLogicTests_Coercion_Null_CoercesToFalse")]
    public void Coercion_Null_CoercesToFalse()
    {
        var module = new LogicTestModule { nullObject = null };
        var expr = FieldExpression<bool>.Compile("%nullObject", new(), typeof(LogicTestModule));
        Assert.AreEqual(false, expr.Evaluate(module));
    }

    [TestInfo("FieldExpressionLogicTests_Coercion_NullString_CoercesToFalse")]
    public void Coercion_NullString_CoercesToFalse()
    {
        var module = new LogicTestModule { nullString = null };
        var expr = FieldExpression<bool>.Compile("%nullString", new(), typeof(LogicTestModule));
        Assert.AreEqual(false, expr.Evaluate(module));
    }

    [TestInfo("FieldExpressionLogicTests_Coercion_NullBool_CoercesToFalse")]
    public void Coercion_NullBool_CoercesToFalse()
    {
        var module = new LogicTestModule { nullBool = null };
        var expr = FieldExpression<bool>.Compile("%nullBool", new(), typeof(LogicTestModule));
        Assert.AreEqual(false, expr.Evaluate(module));
    }

    [TestInfo("FieldExpressionLogicTests_LogicalNot_Null_CoercesToTrue")]
    public void LogicalNot_Null_CoercesToTrue()
    {
        var module = new LogicTestModule { nullObject = null };
        var expr = FieldExpression<bool>.Compile("!%nullObject", new(), typeof(LogicTestModule));
        Assert.AreEqual(true, expr.Evaluate(module));
    }

    [TestInfo("FieldExpressionLogicTests_LogicalAnd_NullAndTrue_ReturnsFalse")]
    public void LogicalAnd_NullAndTrue_ReturnsFalse()
    {
        var module = new LogicTestModule { nullObject = null };
        var expr = FieldExpression<bool>.Compile(
            "%nullObject && true",
            new(),
            typeof(LogicTestModule)
        );
        Assert.AreEqual(false, expr.Evaluate(module));
    }

    [TestInfo("FieldExpressionLogicTests_LogicalOr_NullOrTrue_ReturnsTrue")]
    public void LogicalOr_NullOrTrue_ReturnsTrue()
    {
        var module = new LogicTestModule { nullObject = null };
        var expr = FieldExpression<bool>.Compile(
            "%nullObject || true",
            new(),
            typeof(LogicTestModule)
        );
        Assert.AreEqual(true, expr.Evaluate(module));
    }

    [TestInfo("FieldExpressionLogicTests_LogicalOr_NullOrFalse_ReturnsFalse")]
    public void LogicalOr_NullOrFalse_ReturnsFalse()
    {
        var module = new LogicTestModule { nullObject = null };
        var expr = FieldExpression<bool>.Compile(
            "%nullObject || false",
            new(),
            typeof(LogicTestModule)
        );
        Assert.AreEqual(false, expr.Evaluate(module));
    }

    [TestInfo("FieldExpressionLogicTests_LogicalXor_NullXorTrue_ReturnsTrue")]
    public void LogicalXor_NullXorTrue_ReturnsTrue()
    {
        var module = new LogicTestModule { nullObject = null };
        var expr = FieldExpression<bool>.Compile(
            "%nullObject ^ true",
            new(),
            typeof(LogicTestModule)
        );
        Assert.AreEqual(true, expr.Evaluate(module));
    }

    [TestInfo("FieldExpressionLogicTests_LogicalXor_NullXorFalse_ReturnsFalse")]
    public void LogicalXor_NullXorFalse_ReturnsFalse()
    {
        var module = new LogicTestModule { nullObject = null };
        var expr = FieldExpression<bool>.Compile(
            "%nullObject ^ false",
            new(),
            typeof(LogicTestModule)
        );
        Assert.AreEqual(false, expr.Evaluate(module));
    }

    #endregion

    #region Boolean Coercion - String Values

    [TestInfo("FieldExpressionLogicTests_Coercion_StringTrue_CaseInsensitive")]
    public void Coercion_StringTrue_CaseInsensitive()
    {
        var module = new LogicTestModule { trueString = "TrUe" };
        var expr = FieldExpression<bool>.Compile("%trueString", new(), typeof(LogicTestModule));
        Assert.AreEqual(true, expr.Evaluate(module));
    }

    [TestInfo("FieldExpressionLogicTests_Coercion_StringFalse_CaseInsensitive")]
    public void Coercion_StringFalse_CaseInsensitive()
    {
        var module = new LogicTestModule { falseString = "FaLsE" };
        var expr = FieldExpression<bool>.Compile("%falseString", new(), typeof(LogicTestModule));
        Assert.AreEqual(false, expr.Evaluate(module));
    }

    [TestInfo("FieldExpressionLogicTests_Coercion_StringTrue_Lowercase")]
    public void Coercion_StringTrue_Lowercase()
    {
        var module = new LogicTestModule { trueString = "true" };
        var expr = FieldExpression<bool>.Compile("%trueString", new(), typeof(LogicTestModule));
        Assert.AreEqual(true, expr.Evaluate(module));
    }

    [TestInfo("FieldExpressionLogicTests_Coercion_StringFalse_Lowercase")]
    public void Coercion_StringFalse_Lowercase()
    {
        var module = new LogicTestModule { falseString = "false" };
        var expr = FieldExpression<bool>.Compile("%falseString", new(), typeof(LogicTestModule));
        Assert.AreEqual(false, expr.Evaluate(module));
    }

    [TestInfo("FieldExpressionLogicTests_Coercion_StringTrue_Uppercase")]
    public void Coercion_StringTrue_Uppercase()
    {
        var module = new LogicTestModule { trueString = "TRUE" };
        var expr = FieldExpression<bool>.Compile("%trueString", new(), typeof(LogicTestModule));
        Assert.AreEqual(true, expr.Evaluate(module));
    }

    [TestInfo("FieldExpressionLogicTests_Coercion_StringFalse_Uppercase")]
    public void Coercion_StringFalse_Uppercase()
    {
        var module = new LogicTestModule { falseString = "FALSE" };
        var expr = FieldExpression<bool>.Compile("%falseString", new(), typeof(LogicTestModule));
        Assert.AreEqual(false, expr.Evaluate(module));
    }

    [TestInfo("FieldExpressionLogicTests_Coercion_EmptyString_CoercesToTrue")]
    public void Coercion_EmptyString_CoercesToTrue()
    {
        var module = new LogicTestModule { emptyString = "" };
        var expr = FieldExpression<bool>.Compile("%emptyString", new(), typeof(LogicTestModule));
        Assert.AreEqual(true, expr.Evaluate(module));
    }

    [TestInfo("FieldExpressionLogicTests_Coercion_NonBooleanString_CoercesToTrue")]
    public void Coercion_NonBooleanString_CoercesToTrue()
    {
        var module = new LogicTestModule { emptyString = "hello" };
        var expr = FieldExpression<bool>.Compile("%emptyString", new(), typeof(LogicTestModule));
        Assert.AreEqual(true, expr.Evaluate(module));
    }

    #endregion

    #region Boolean Coercion - Numeric Values

    [TestInfo("FieldExpressionLogicTests_Coercion_NonZeroInt_CoercesToTrue")]
    public void Coercion_NonZeroInt_CoercesToTrue()
    {
        var module = new LogicTestModule { intValue = 42 };
        var expr = FieldExpression<bool>.Compile("%intValue", new(), typeof(LogicTestModule));
        Assert.AreEqual(true, expr.Evaluate(module));
    }

    [TestInfo("FieldExpressionLogicTests_Coercion_ZeroInt_CoercesToTrue")]
    public void Coercion_ZeroInt_CoercesToTrue()
    {
        var module = new LogicTestModule { zero = 0 };
        var expr = FieldExpression<bool>.Compile("%zero", new(), typeof(LogicTestModule));
        Assert.AreEqual(true, expr.Evaluate(module));
    }

    [TestInfo("FieldExpressionLogicTests_Coercion_NonZeroDouble_CoercesToTrue")]
    public void Coercion_NonZeroDouble_CoercesToTrue()
    {
        var module = new LogicTestModule { doubleValue = 3.14 };
        var expr = FieldExpression<bool>.Compile("%doubleValue", new(), typeof(LogicTestModule));
        Assert.AreEqual(true, expr.Evaluate(module));
    }

    [TestInfo("FieldExpressionLogicTests_Coercion_ZeroDouble_CoercesToTrue")]
    public void Coercion_ZeroDouble_CoercesToTrue()
    {
        var module = new LogicTestModule { doubleValue = 0.0 };
        var expr = FieldExpression<bool>.Compile("%doubleValue", new(), typeof(LogicTestModule));
        Assert.AreEqual(true, expr.Evaluate(module));
    }

    [TestInfo("FieldExpressionLogicTests_Coercion_NegativeInt_CoercesToTrue")]
    public void Coercion_NegativeInt_CoercesToTrue()
    {
        var module = new LogicTestModule { intValue = -5 };
        var expr = FieldExpression<bool>.Compile("%intValue", new(), typeof(LogicTestModule));
        Assert.AreEqual(true, expr.Evaluate(module));
    }

    #endregion

    #region Boolean Coercion - Object Values

    [TestInfo("FieldExpressionLogicTests_Coercion_NonNullObject_CoercesToTrue")]
    public void Coercion_NonNullObject_CoercesToTrue()
    {
        var module = new LogicTestModule { nonNullObject = new object() };
        var expr = FieldExpression<bool>.Compile("%nonNullObject", new(), typeof(LogicTestModule));
        Assert.AreEqual(true, expr.Evaluate(module));
    }

    #endregion

    #region Operator Precedence

    [TestInfo("FieldExpressionLogicTests_Precedence_NotBeforeAnd")]
    public void Precedence_NotBeforeAnd()
    {
        var expr = FieldExpression<bool>.Compile("!false && true", new(), typeof(LogicTestModule));
        Assert.AreEqual(true, expr.Evaluate(new LogicTestModule()));
    }

    [TestInfo("FieldExpressionLogicTests_Precedence_NotBeforeOr")]
    public void Precedence_NotBeforeOr()
    {
        var expr = FieldExpression<bool>.Compile("!false || false", new(), typeof(LogicTestModule));
        Assert.AreEqual(true, expr.Evaluate(new LogicTestModule()));
    }

    [TestInfo("FieldExpressionLogicTests_Precedence_AndBeforeOr")]
    public void Precedence_AndBeforeOr()
    {
        var expr = FieldExpression<bool>.Compile(
            "false || true && false",
            new(),
            typeof(LogicTestModule)
        );
        Assert.AreEqual(false, expr.Evaluate(new LogicTestModule()));
    }

    [TestInfo("FieldExpressionLogicTests_Precedence_AndBeforeOr_Alternative")]
    public void Precedence_AndBeforeOr_Alternative()
    {
        var expr = FieldExpression<bool>.Compile(
            "true || false && false",
            new(),
            typeof(LogicTestModule)
        );
        Assert.AreEqual(true, expr.Evaluate(new LogicTestModule()));
    }

    [TestInfo("FieldExpressionLogicTests_Precedence_XorBeforeOr")]
    public void Precedence_XorBeforeOr()
    {
        var expr = FieldExpression<bool>.Compile(
            "false || true ^ true",
            new(),
            typeof(LogicTestModule)
        );
        Assert.AreEqual(false, expr.Evaluate(new LogicTestModule()));
    }

    [TestInfo("FieldExpressionLogicTests_Precedence_Parentheses_OverrideAnd")]
    public void Precedence_Parentheses_OverrideAnd()
    {
        var expr = FieldExpression<bool>.Compile(
            "(false || true) && false",
            new(),
            typeof(LogicTestModule)
        );
        Assert.AreEqual(false, expr.Evaluate(new LogicTestModule()));
    }

    [TestInfo("FieldExpressionLogicTests_Precedence_Parentheses_OverrideOr")]
    public void Precedence_Parentheses_OverrideOr()
    {
        var expr = FieldExpression<bool>.Compile(
            "false || (true && true)",
            new(),
            typeof(LogicTestModule)
        );
        Assert.AreEqual(true, expr.Evaluate(new LogicTestModule()));
    }

    [TestInfo("FieldExpressionLogicTests_Precedence_MultipleNot")]
    public void Precedence_MultipleNot()
    {
        var expr = FieldExpression<bool>.Compile("!!true", new(), typeof(LogicTestModule));
        Assert.AreEqual(true, expr.Evaluate(new LogicTestModule()));
    }

    [TestInfo("FieldExpressionLogicTests_Precedence_TripleNot")]
    public void Precedence_TripleNot()
    {
        var expr = FieldExpression<bool>.Compile("!!!false", new(), typeof(LogicTestModule));
        Assert.AreEqual(true, expr.Evaluate(new LogicTestModule()));
    }

    #endregion

    #region Comparison Operators

    [TestInfo("FieldExpressionLogicTests_Comparison_Equal_Integers")]
    public void Comparison_Equal_Integers()
    {
        var expr = FieldExpression<bool>.Compile("5 == 5", new(), typeof(LogicTestModule));
        Assert.AreEqual(true, expr.Evaluate(new LogicTestModule()));
    }

    [TestInfo("FieldExpressionLogicTests_Comparison_NotEqual_Integers")]
    public void Comparison_NotEqual_Integers()
    {
        var expr = FieldExpression<bool>.Compile("5 != 3", new(), typeof(LogicTestModule));
        Assert.AreEqual(true, expr.Evaluate(new LogicTestModule()));
    }

    [TestInfo("FieldExpressionLogicTests_Comparison_LessThan")]
    public void Comparison_LessThan()
    {
        var expr = FieldExpression<bool>.Compile("3 < 5", new(), typeof(LogicTestModule));
        Assert.AreEqual(true, expr.Evaluate(new LogicTestModule()));
    }

    [TestInfo("FieldExpressionLogicTests_Comparison_LessThanOrEqual")]
    public void Comparison_LessThanOrEqual()
    {
        var expr = FieldExpression<bool>.Compile("5 <= 5", new(), typeof(LogicTestModule));
        Assert.AreEqual(true, expr.Evaluate(new LogicTestModule()));
    }

    [TestInfo("FieldExpressionLogicTests_Comparison_GreaterThan")]
    public void Comparison_GreaterThan()
    {
        var expr = FieldExpression<bool>.Compile("5 > 3", new(), typeof(LogicTestModule));
        Assert.AreEqual(true, expr.Evaluate(new LogicTestModule()));
    }

    [TestInfo("FieldExpressionLogicTests_Comparison_GreaterThanOrEqual")]
    public void Comparison_GreaterThanOrEqual()
    {
        var expr = FieldExpression<bool>.Compile("5 >= 5", new(), typeof(LogicTestModule));
        Assert.AreEqual(true, expr.Evaluate(new LogicTestModule()));
    }

    [TestInfo("FieldExpressionLogicTests_Comparison_Equal_Booleans")]
    public void Comparison_Equal_Booleans()
    {
        var expr = FieldExpression<bool>.Compile("true == true", new(), typeof(LogicTestModule));
        Assert.AreEqual(true, expr.Evaluate(new LogicTestModule()));
    }

    [TestInfo("FieldExpressionLogicTests_Comparison_NotEqual_Booleans")]
    public void Comparison_NotEqual_Booleans()
    {
        var expr = FieldExpression<bool>.Compile("true != false", new(), typeof(LogicTestModule));
        Assert.AreEqual(true, expr.Evaluate(new LogicTestModule()));
    }

    [TestInfo("FieldExpressionLogicTests_Comparison_WithFields")]
    public void Comparison_WithFields()
    {
        var module = new LogicTestModule { intValue = 42 };
        var expr = FieldExpression<bool>.Compile("%intValue > 40", new(), typeof(LogicTestModule));
        Assert.AreEqual(true, expr.Evaluate(module));
    }

    #endregion

    #region Complex Expressions

    [TestInfo("FieldExpressionLogicTests_Complex_AndOrCombination")]
    public void Complex_AndOrCombination()
    {
        var expr = FieldExpression<bool>.Compile(
            "true && false || true",
            new(),
            typeof(LogicTestModule)
        );
        Assert.AreEqual(true, expr.Evaluate(new LogicTestModule()));
    }

    [TestInfo("FieldExpressionLogicTests_Complex_NestedParentheses")]
    public void Complex_NestedParentheses()
    {
        var expr = FieldExpression<bool>.Compile(
            "((true && false) || (false && true)) || true",
            new(),
            typeof(LogicTestModule)
        );
        Assert.AreEqual(true, expr.Evaluate(new LogicTestModule()));
    }

    [TestInfo("FieldExpressionLogicTests_Complex_WithComparisons")]
    public void Complex_WithComparisons()
    {
        var module = new LogicTestModule { intValue = 10 };
        var expr = FieldExpression<bool>.Compile(
            "%intValue > 5 && %intValue < 15",
            new(),
            typeof(LogicTestModule)
        );
        Assert.AreEqual(true, expr.Evaluate(module));
    }

    [TestInfo("FieldExpressionLogicTests_Complex_WithNot")]
    public void Complex_WithNot()
    {
        var module = new LogicTestModule { trueValue = true, falseValue = false };
        var expr = FieldExpression<bool>.Compile(
            "!%falseValue && %trueValue",
            new(),
            typeof(LogicTestModule)
        );
        Assert.AreEqual(true, expr.Evaluate(module));
    }

    [TestInfo("FieldExpressionLogicTests_Complex_AllOperators")]
    public void Complex_AllOperators()
    {
        var expr = FieldExpression<bool>.Compile(
            "!false && (true || false) ^ false",
            new(),
            typeof(LogicTestModule)
        );
        Assert.AreEqual(true, expr.Evaluate(new LogicTestModule()));
    }

    [TestInfo("FieldExpressionLogicTests_Complex_WithNullCoalescing")]
    public void Complex_WithNullCoalescing()
    {
        var module = new LogicTestModule { nullBool = null };
        var expr = FieldExpression<bool>.Compile(
            "(%nullBool ?? false) || true",
            new(),
            typeof(LogicTestModule)
        );
        Assert.AreEqual(true, expr.Evaluate(module));
    }

    [TestInfo("FieldExpressionLogicTests_Complex_ChainedAnd")]
    public void Complex_ChainedAnd()
    {
        var expr = FieldExpression<bool>.Compile(
            "true && true && true && true",
            new(),
            typeof(LogicTestModule)
        );
        Assert.AreEqual(true, expr.Evaluate(new LogicTestModule()));
    }

    [TestInfo("FieldExpressionLogicTests_Complex_ChainedOr")]
    public void Complex_ChainedOr()
    {
        var expr = FieldExpression<bool>.Compile(
            "false || false || false || true",
            new(),
            typeof(LogicTestModule)
        );
        Assert.AreEqual(true, expr.Evaluate(new LogicTestModule()));
    }

    [TestInfo("FieldExpressionLogicTests_Complex_MixedFields")]
    public void Complex_MixedFields()
    {
        var module = new LogicTestModule
        {
            trueValue = true,
            falseValue = false,
            intValue = 10,
        };
        var expr = FieldExpression<bool>.Compile(
            "%trueValue && !%falseValue && %intValue > 5",
            new(),
            typeof(LogicTestModule)
        );
        Assert.AreEqual(true, expr.Evaluate(module));
    }

    #endregion

    #region Short-Circuit Evaluation

    [TestInfo("FieldExpressionLogicTests_ShortCircuit_And_FalseFirst")]
    public void ShortCircuit_And_FalseFirst()
    {
        // If AND short-circuits properly, the second operand won't be evaluated
        var module = new LogicTestModule { falseValue = false, trueValue = true };
        var expr = FieldExpression<bool>.Compile(
            "%falseValue && %trueValue",
            new(),
            typeof(LogicTestModule)
        );
        Assert.AreEqual(false, expr.Evaluate(module));
    }

    [TestInfo("FieldExpressionLogicTests_ShortCircuit_Or_TrueFirst")]
    public void ShortCircuit_Or_TrueFirst()
    {
        // If OR short-circuits properly, the second operand won't be evaluated
        var module = new LogicTestModule { trueValue = true, falseValue = false };
        var expr = FieldExpression<bool>.Compile(
            "%trueValue || %falseValue",
            new(),
            typeof(LogicTestModule)
        );
        Assert.AreEqual(true, expr.Evaluate(module));
    }

    #endregion

    #region Case Sensitivity

    [TestInfo("FieldExpressionLogicTests_CaseSensitivity_True_Lowercase")]
    public void CaseSensitivity_True_Lowercase()
    {
        var expr = FieldExpression<bool>.Compile("true", new(), typeof(LogicTestModule));
        Assert.AreEqual(true, expr.Evaluate(new LogicTestModule()));
    }

    [TestInfo("FieldExpressionLogicTests_CaseSensitivity_True_Uppercase")]
    public void CaseSensitivity_True_Uppercase()
    {
        var expr = FieldExpression<bool>.Compile("TRUE", new(), typeof(LogicTestModule));
        Assert.AreEqual(true, expr.Evaluate(new LogicTestModule()));
    }

    [TestInfo("FieldExpressionLogicTests_CaseSensitivity_True_MixedCase")]
    public void CaseSensitivity_True_MixedCase()
    {
        var expr = FieldExpression<bool>.Compile("TrUe", new(), typeof(LogicTestModule));
        Assert.AreEqual(true, expr.Evaluate(new LogicTestModule()));
    }

    [TestInfo("FieldExpressionLogicTests_CaseSensitivity_False_Lowercase")]
    public void CaseSensitivity_False_Lowercase()
    {
        var expr = FieldExpression<bool>.Compile("false", new(), typeof(LogicTestModule));
        Assert.AreEqual(false, expr.Evaluate(new LogicTestModule()));
    }

    [TestInfo("FieldExpressionLogicTests_CaseSensitivity_False_Uppercase")]
    public void CaseSensitivity_False_Uppercase()
    {
        var expr = FieldExpression<bool>.Compile("FALSE", new(), typeof(LogicTestModule));
        Assert.AreEqual(false, expr.Evaluate(new LogicTestModule()));
    }

    [TestInfo("FieldExpressionLogicTests_CaseSensitivity_False_MixedCase")]
    public void CaseSensitivity_False_MixedCase()
    {
        var expr = FieldExpression<bool>.Compile("FaLsE", new(), typeof(LogicTestModule));
        Assert.AreEqual(false, expr.Evaluate(new LogicTestModule()));
    }

    #endregion

    #region Real-World Use Cases

    [TestInfo("FieldExpressionLogicTests_RealWorld_ActivationCondition")]
    public void RealWorld_ActivationCondition()
    {
        var module = new LogicTestModule
        {
            trueValue = true, // isAlwaysActive
            falseValue = false, // isThrottleControlled
        };
        // Example from wiki: !%isThrottleControlled && %isAlwaysActive
        var expr = FieldExpression<bool>.Compile(
            "!%falseValue && %trueValue",
            new(),
            typeof(LogicTestModule)
        );
        Assert.AreEqual(true, expr.Evaluate(module));
    }

    [TestInfo("FieldExpressionLogicTests_RealWorld_RangeCheck")]
    public void RealWorld_RangeCheck()
    {
        var module = new LogicTestModule { doubleValue = 50.0 };
        var expr = FieldExpression<bool>.Compile(
            "%doubleValue >= 0 && %doubleValue <= 100",
            new(),
            typeof(LogicTestModule)
        );
        Assert.AreEqual(true, expr.Evaluate(module));
    }

    [TestInfo("FieldExpressionLogicTests_RealWorld_NullSafeCheck")]
    public void RealWorld_NullSafeCheck()
    {
        var module = new LogicTestModule { nullObject = null };
        var expr = FieldExpression<bool>.Compile(
            "(%nullObject ?? true) && true",
            new(),
            typeof(LogicTestModule)
        );
        Assert.AreEqual(true, expr.Evaluate(module));
    }

    #endregion

    #region Edge Cases

    [TestInfo("FieldExpressionLogicTests_EdgeCase_DoubleNegation")]
    public void EdgeCase_DoubleNegation()
    {
        var expr = FieldExpression<bool>.Compile("!!true", new(), typeof(LogicTestModule));
        Assert.AreEqual(true, expr.Evaluate(new LogicTestModule()));
    }

    [TestInfo("FieldExpressionLogicTests_EdgeCase_XorChaining")]
    public void EdgeCase_XorChaining()
    {
        var expr = FieldExpression<bool>.Compile(
            "true ^ true ^ true",
            new(),
            typeof(LogicTestModule)
        );
        Assert.AreEqual(true, expr.Evaluate(new LogicTestModule()));
    }

    [TestInfo("FieldExpressionLogicTests_EdgeCase_AllFalseAnd")]
    public void EdgeCase_AllFalseAnd()
    {
        var expr = FieldExpression<bool>.Compile(
            "false && false && false",
            new(),
            typeof(LogicTestModule)
        );
        Assert.AreEqual(false, expr.Evaluate(new LogicTestModule()));
    }

    [TestInfo("FieldExpressionLogicTests_EdgeCase_AllTrueOr")]
    public void EdgeCase_AllTrueOr()
    {
        var expr = FieldExpression<bool>.Compile(
            "true || true || true",
            new(),
            typeof(LogicTestModule)
        );
        Assert.AreEqual(true, expr.Evaluate(new LogicTestModule()));
    }

    [TestInfo("FieldExpressionLogicTests_EdgeCase_DeeplyNestedParentheses")]
    public void EdgeCase_DeeplyNestedParentheses()
    {
        var expr = FieldExpression<bool>.Compile(
            "(((true && true) && true) && true)",
            new(),
            typeof(LogicTestModule)
        );
        Assert.AreEqual(true, expr.Evaluate(new LogicTestModule()));
    }

    #endregion
}
