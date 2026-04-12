using BackgroundResourceProcessing.Utils;
using KSP.Testing;

namespace BackgroundResourceProcessing.Test.Utils;

/// <summary>
/// Tests for type coercion and conversion in field expressions.
/// Tests CoerceToEnum, CoerceToTarget, GetCompatibleValue, and IsCompatibleType.
/// </summary>
public sealed class FieldExpressionTypeCoercionTests : BRPTestBase
{
    #region Test Helper Classes

    class TypeCoercionModule : PartModule
    {
        public byte byteValue = 10;
        public sbyte sbyteValue = -5;
        public short shortValue = 100;
        public ushort ushortValue = 200;
        public int intValue = 1000;
        public uint uintValue = 2000;
        public long longValue = 10000L;
        public ulong ulongValue = 20000UL;
        public float floatValue = 3.14f;
        public double doubleValue = 2.718;
        public string stringValue = "42";
        public bool boolValue = true;
        public ResourceFlowMode enumValue = ResourceFlowMode.STACK_PRIORITY_SEARCH;
        public string enumString = "ALL_VESSEL";
        public object objectValue = 123;
        public object nullObject = null;
    }

    #endregion

    #region Numeric Type Coercion to Double

    [TestInfo("FieldExpressionTypeCoercionTests_NumericCoercion_ByteToDouble")]
    public void NumericCoercion_ByteToDouble()
    {
        var module = new TypeCoercionModule { byteValue = 255 };
        var expr = FieldExpression<double>.Compile("%byteValue", new(), typeof(TypeCoercionModule));
        Assert.AreEqual(255.0, expr.Evaluate(module));
    }

    [TestInfo("FieldExpressionTypeCoercionTests_NumericCoercion_SByteToDouble")]
    public void NumericCoercion_SByteToDouble()
    {
        var module = new TypeCoercionModule { sbyteValue = -128 };
        var expr = FieldExpression<double>.Compile(
            "%sbyteValue",
            new(),
            typeof(TypeCoercionModule)
        );
        Assert.AreEqual(-128.0, expr.Evaluate(module));
    }

    [TestInfo("FieldExpressionTypeCoercionTests_NumericCoercion_ShortToDouble")]
    public void NumericCoercion_ShortToDouble()
    {
        var module = new TypeCoercionModule { shortValue = 32767 };
        var expr = FieldExpression<double>.Compile(
            "%shortValue",
            new(),
            typeof(TypeCoercionModule)
        );
        Assert.AreEqual(32767.0, expr.Evaluate(module));
    }

    [TestInfo("FieldExpressionTypeCoercionTests_NumericCoercion_UShortToDouble")]
    public void NumericCoercion_UShortToDouble()
    {
        var module = new TypeCoercionModule { ushortValue = 65535 };
        var expr = FieldExpression<double>.Compile(
            "%ushortValue",
            new(),
            typeof(TypeCoercionModule)
        );
        Assert.AreEqual(65535.0, expr.Evaluate(module));
    }

    [TestInfo("FieldExpressionTypeCoercionTests_NumericCoercion_IntToDouble")]
    public void NumericCoercion_IntToDouble()
    {
        var module = new TypeCoercionModule { intValue = 123456 };
        var expr = FieldExpression<double>.Compile("%intValue", new(), typeof(TypeCoercionModule));
        Assert.AreEqual(123456.0, expr.Evaluate(module));
    }

    [TestInfo("FieldExpressionTypeCoercionTests_NumericCoercion_UIntToDouble")]
    public void NumericCoercion_UIntToDouble()
    {
        var module = new TypeCoercionModule { uintValue = 4000000000 };
        var expr = FieldExpression<double>.Compile("%uintValue", new(), typeof(TypeCoercionModule));
        Assert.AreEqual(4000000000.0, expr.Evaluate(module));
    }

    [TestInfo("FieldExpressionTypeCoercionTests_NumericCoercion_LongToDouble")]
    public void NumericCoercion_LongToDouble()
    {
        var module = new TypeCoercionModule { longValue = 9876543210L };
        var expr = FieldExpression<double>.Compile("%longValue", new(), typeof(TypeCoercionModule));
        Assert.AreEqual(9876543210.0, expr.Evaluate(module));
    }

    [TestInfo("FieldExpressionTypeCoercionTests_NumericCoercion_ULongToDouble")]
    public void NumericCoercion_ULongToDouble()
    {
        var module = new TypeCoercionModule { ulongValue = 18000000000000000000UL };
        var expr = FieldExpression<double>.Compile(
            "%ulongValue",
            new(),
            typeof(TypeCoercionModule)
        );
        Assert.AreEqual(18000000000000000000.0, expr.Evaluate(module));
    }

    [TestInfo("FieldExpressionTypeCoercionTests_NumericCoercion_FloatToDouble")]
    public void NumericCoercion_FloatToDouble()
    {
        var module = new TypeCoercionModule { floatValue = 3.14159f };
        var expr = FieldExpression<double>.Compile(
            "%floatValue",
            new(),
            typeof(TypeCoercionModule)
        );
        var result = expr.Evaluate(module);
        Assert.AreEqual(3.14159, result.Value, 0.00001);
    }

    #endregion

    // Note: The expression system only supports double, not float

    #region Enum Coercion

    [TestInfo("FieldExpressionTypeCoercionTests_EnumCoercion_StringToEnum")]
    public void EnumCoercion_StringToEnum()
    {
        var module = new TypeCoercionModule { enumString = "NO_FLOW" };
        var expr = FieldExpression<ResourceFlowMode>.Compile(
            "%enumString",
            new(),
            typeof(TypeCoercionModule)
        );
        Assert.AreEqual(ResourceFlowMode.NO_FLOW, expr.Evaluate(module));
    }

    [TestInfo("FieldExpressionTypeCoercionTests_EnumCoercion_EnumFieldDirect")]
    public void EnumCoercion_EnumFieldDirect()
    {
        var module = new TypeCoercionModule { enumValue = ResourceFlowMode.ALL_VESSEL };
        var expr = FieldExpression<ResourceFlowMode>.Compile(
            "%enumValue",
            new(),
            typeof(TypeCoercionModule)
        );
        Assert.AreEqual(ResourceFlowMode.ALL_VESSEL, expr.Evaluate(module));
    }

    [TestInfo("FieldExpressionTypeCoercionTests_EnumCoercion_LiteralEnum")]
    public void EnumCoercion_LiteralEnum()
    {
        var expr = FieldExpression<ResourceFlowMode>.Compile(
            "STAGE_PRIORITY_FLOW",
            new(),
            typeof(TypeCoercionModule)
        );
        Assert.AreEqual(
            ResourceFlowMode.STAGE_PRIORITY_FLOW,
            expr.Evaluate(new TypeCoercionModule())
        );
    }

    #endregion

    #region Boolean Coercion

    [TestInfo("FieldExpressionTypeCoercionTests_BooleanCoercion_TrueValue")]
    public void BooleanCoercion_TrueValue()
    {
        var module = new TypeCoercionModule { boolValue = true };
        var expr = FieldExpression<bool>.Compile("%boolValue", new(), typeof(TypeCoercionModule));
        Assert.IsTrue(expr.Evaluate(module));
    }

    [TestInfo("FieldExpressionTypeCoercionTests_BooleanCoercion_StringToTrue")]
    public void BooleanCoercion_StringToTrue()
    {
        var module = new TypeCoercionModule { stringValue = "true" };
        var expr = FieldExpression<bool>.Compile("%stringValue", new(), typeof(TypeCoercionModule));
        Assert.IsTrue(expr.Evaluate(module));
    }

    [TestInfo("FieldExpressionTypeCoercionTests_BooleanCoercion_StringToFalse")]
    public void BooleanCoercion_StringToFalse()
    {
        var module = new TypeCoercionModule { stringValue = "false" };
        var expr = FieldExpression<bool>.Compile("%stringValue", new(), typeof(TypeCoercionModule));
        Assert.IsFalse(expr.Evaluate(module));
    }

    [TestInfo("FieldExpressionTypeCoercionTests_BooleanCoercion_NullToFalse")]
    public void BooleanCoercion_NullToFalse()
    {
        var module = new TypeCoercionModule { nullObject = null };
        var expr = FieldExpression<bool>.Compile("%nullObject", new(), typeof(TypeCoercionModule));
        Assert.IsFalse(expr.Evaluate(module));
    }

    [TestInfo("FieldExpressionTypeCoercionTests_BooleanCoercion_NonNullObjectToTrue")]
    public void BooleanCoercion_NonNullObjectToTrue()
    {
        var module = new TypeCoercionModule { objectValue = new object() };
        var expr = FieldExpression<bool>.Compile("%objectValue", new(), typeof(TypeCoercionModule));
        Assert.IsTrue(expr.Evaluate(module));
    }

    [TestInfo("FieldExpressionTypeCoercionTests_BooleanCoercion_NumberToTrue")]
    public void BooleanCoercion_NumberToTrue()
    {
        var module = new TypeCoercionModule { intValue = 42 };
        var expr = FieldExpression<bool>.Compile("%intValue", new(), typeof(TypeCoercionModule));
        Assert.IsTrue(expr.Evaluate(module));
    }

    [TestInfo("FieldExpressionTypeCoercionTests_BooleanCoercion_ZeroToTrue")]
    public void BooleanCoercion_ZeroToTrue()
    {
        var module = new TypeCoercionModule { intValue = 0 };
        var expr = FieldExpression<bool>.Compile("%intValue", new(), typeof(TypeCoercionModule));
        // Numbers coerce to true regardless of value
        Assert.IsTrue(expr.Evaluate(module));
    }

    #endregion

    #region Nullable Type Coercion

    [TestInfo("FieldExpressionTypeCoercionTests_NullableCoercion_NullableIntWithValue")]
    public void NullableCoercion_NullableIntWithValue()
    {
        var module = new NullableModule { nullableInt = 42 };
        var expr = FieldExpression<double>.Compile("%nullableInt", new(), typeof(NullableModule));
        Assert.AreEqual(42.0, expr.Evaluate(module));
    }

    [TestInfo("FieldExpressionTypeCoercionTests_NullableCoercion_NullableIntNull")]
    public void NullableCoercion_NullableIntNull()
    {
        var module = new NullableModule { nullableInt = null };
        var expr = FieldExpression<double>.Compile(
            "%nullableInt ?? 99",
            new(),
            typeof(NullableModule)
        );
        Assert.AreEqual(99.0, expr.Evaluate(module));
    }

    [TestInfo("FieldExpressionTypeCoercionTests_NullableCoercion_NullableDoubleWithValue")]
    public void NullableCoercion_NullableDoubleWithValue()
    {
        var module = new NullableModule { nullableDouble = 3.14 };
        var expr = FieldExpression<double>.Compile(
            "%nullableDouble",
            new(),
            typeof(NullableModule)
        );
        Assert.AreEqual(3.14, expr.Evaluate(module));
    }

#pragma warning disable CS0649
    class NullableModule : PartModule
    {
        public int? nullableInt;
        public double? nullableDouble;
        public bool? nullableBool;
    }
#pragma warning restore CS0649

    #endregion

    #region String Coercion

    [TestInfo("FieldExpressionTypeCoercionTests_StringCoercion_NumberStringToDouble")]
    public void StringCoercion_NumberStringToDouble()
    {
        var module = new TypeCoercionModule { stringValue = "123.45" };
        var expr = FieldExpression<double>.Compile(
            "%stringValue",
            new(),
            typeof(TypeCoercionModule)
        );
        Assert.AreEqual(123.45, expr.Evaluate(module));
    }

    [TestInfo("FieldExpressionTypeCoercionTests_StringCoercion_ScientificNotation")]
    public void StringCoercion_ScientificNotation()
    {
        var module = new TypeCoercionModule { stringValue = "1.5e3" };
        var expr = FieldExpression<double>.Compile(
            "%stringValue",
            new(),
            typeof(TypeCoercionModule)
        );
        Assert.AreEqual(1500.0, expr.Evaluate(module));
    }

    #endregion

    #region Object Type Coercion

    [TestInfo("FieldExpressionTypeCoercionTests_ObjectCoercion_IntObject")]
    public void ObjectCoercion_IntObject()
    {
        var module = new TypeCoercionModule { objectValue = 42 };
        var expr = FieldExpression<double>.Compile(
            "%objectValue",
            new(),
            typeof(TypeCoercionModule)
        );
        Assert.AreEqual(42.0, expr.Evaluate(module));
    }

    [TestInfo("FieldExpressionTypeCoercionTests_ObjectCoercion_DoubleObject")]
    public void ObjectCoercion_DoubleObject()
    {
        var module = new TypeCoercionModule { objectValue = 3.14 };
        var expr = FieldExpression<double>.Compile(
            "%objectValue",
            new(),
            typeof(TypeCoercionModule)
        );
        Assert.AreEqual(3.14, expr.Evaluate(module));
    }

    [TestInfo("FieldExpressionTypeCoercionTests_ObjectCoercion_StringObject")]
    public void ObjectCoercion_StringObject()
    {
        var module = new TypeCoercionModule { objectValue = "hello" };
        var expr = FieldExpression<string>.Compile(
            "%objectValue",
            new(),
            typeof(TypeCoercionModule)
        );
        Assert.AreEqual("hello", expr.Evaluate(module));
    }

    #endregion

    #region Type Compatibility in Arithmetic

    [TestInfo("FieldExpressionTypeCoercionTests_TypeCompatibility_MixedArithmetic")]
    public void TypeCompatibility_MixedArithmetic()
    {
        var module = new TypeCoercionModule { intValue = 10, floatValue = 2.5f };
        var expr = FieldExpression<double>.Compile(
            "%intValue + %floatValue",
            new(),
            typeof(TypeCoercionModule)
        );
        Assert.AreEqual(12.5, expr.Evaluate(module));
    }

    [TestInfo("FieldExpressionTypeCoercionTests_TypeCompatibility_AllNumericTypes")]
    public void TypeCompatibility_AllNumericTypes()
    {
        var module = new TypeCoercionModule
        {
            byteValue = 1,
            shortValue = 2,
            intValue = 3,
            longValue = 4,
            floatValue = 5.0f,
            doubleValue = 6.0,
        };
        var expr = FieldExpression<double>.Compile(
            "%byteValue + %shortValue + %intValue + %longValue + %floatValue + %doubleValue",
            new(),
            typeof(TypeCoercionModule)
        );
        Assert.AreEqual(21.0, expr.Evaluate(module));
    }

    #endregion

    #region Edge Cases

    [TestInfo("FieldExpressionTypeCoercionTests_EdgeCase_MaxValues")]
    public void EdgeCase_MaxValues()
    {
        var module = new TypeCoercionModule { byteValue = byte.MaxValue, intValue = int.MaxValue };
        var expr = FieldExpression<double>.Compile(
            "%byteValue + %intValue",
            new(),
            typeof(TypeCoercionModule)
        );
        Assert.AreEqual((double)byte.MaxValue + int.MaxValue, expr.Evaluate(module));
    }

    [TestInfo("FieldExpressionTypeCoercionTests_EdgeCase_MinValues")]
    public void EdgeCase_MinValues()
    {
        var module = new TypeCoercionModule
        {
            sbyteValue = sbyte.MinValue,
            shortValue = short.MinValue,
        };
        var expr = FieldExpression<double>.Compile(
            "%sbyteValue + %shortValue",
            new(),
            typeof(TypeCoercionModule)
        );
        Assert.AreEqual((double)sbyte.MinValue + short.MinValue, expr.Evaluate(module));
    }

    [TestInfo("FieldExpressionTypeCoercionTests_EdgeCase_PrecisionLoss")]
    public void EdgeCase_PrecisionLoss()
    {
        var module = new TypeCoercionModule { longValue = long.MaxValue };
        var expr = FieldExpression<double>.Compile("%longValue", new(), typeof(TypeCoercionModule));
        // Double can't represent all long values precisely
        var result = expr.Evaluate(module);
        Assert.IsTrue(result > 0);
    }

    #endregion
}
