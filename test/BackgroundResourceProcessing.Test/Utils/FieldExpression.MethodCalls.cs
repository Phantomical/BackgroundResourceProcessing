using BackgroundResourceProcessing.Utils;

namespace BackgroundResourceProcessing.Test.Utils;

/// <summary>
/// Tests for method invocation in field expressions.
/// Tests the Methods.DoMethodInvoke functionality and parameter type conversion.
/// </summary>
[TestClass]
public sealed class FieldExpressionMethodCallsTests
{
    #region Test Helper Classes

    class MethodCallModule : PartModule
    {
        public double Add(double a, double b) => a + b;

        public double Multiply(int a, int b) => a * b;

        public string Concat(string a, string b) => a + b;

        public double NoParams() => 42.0;

        public double SingleParam(double x) => x * 2;

        public double ThreeParams(double a, double b, double c) => a + b + c;

        // Overloaded methods
        public double Overloaded(int x) => x;

        public double Overloaded(double x) => x * 2;

        public double Overloaded(string x) => double.Parse(x);

        // Method with type conversion
        public double ConvertParam(int x) => x + 0.5;

        // Method returning different types
        public int ReturnInt() => 10;

        public string ReturnString() => "test";

        public bool ReturnBool() => true;
    }

    #endregion

    #region Basic Method Calls

    [TestMethod]
    public void MethodCall_NoParams()
    {
        var module = new MethodCallModule();
        var expr = FieldExpression<double>.Compile("%.NoParams()", new(), typeof(MethodCallModule));
        Assert.AreEqual(42.0, expr.Evaluate(module));
    }

    [TestMethod]
    public void MethodCall_SingleParam()
    {
        var module = new MethodCallModule();
        var expr = FieldExpression<double>.Compile(
            "%.SingleParam(10)",
            new(),
            typeof(MethodCallModule)
        );
        Assert.AreEqual(20.0, expr.Evaluate(module));
    }

    [TestMethod]
    public void MethodCall_TwoParams()
    {
        var module = new MethodCallModule();
        var expr = FieldExpression<double>.Compile("%.Add(5, 7)", new(), typeof(MethodCallModule));
        Assert.AreEqual(12.0, expr.Evaluate(module));
    }

    [TestMethod]
    public void MethodCall_ThreeParams()
    {
        var module = new MethodCallModule();
        var expr = FieldExpression<double>.Compile(
            "%.ThreeParams(1, 2, 3)",
            new(),
            typeof(MethodCallModule)
        );
        Assert.AreEqual(6.0, expr.Evaluate(module));
    }

    #endregion

    #region Method Calls with Type Conversion

    [TestMethod]
    public void MethodCall_TypeConversion_IntToDouble()
    {
        var module = new MethodCallModule();
        var expr = FieldExpression<double>.Compile(
            "%.Multiply(5, 3)",
            new(),
            typeof(MethodCallModule)
        );
        Assert.AreEqual(15.0, expr.Evaluate(module));
    }

    [TestMethod]
    public void MethodCall_TypeConversion_DoubleToInt()
    {
        var module = new MethodCallModule();
        var expr = FieldExpression<double>.Compile(
            "%.ConvertParam(10)",
            new(),
            typeof(MethodCallModule)
        );
        Assert.AreEqual(10.5, expr.Evaluate(module));
    }

    #endregion

    #region Method Calls with String Parameters

    [TestMethod]
    public void MethodCall_StringParams()
    {
        var module = new MethodCallModule();
        var expr = FieldExpression<string>.Compile(
            "%.Concat(\"hello\", \" world\")",
            new(),
            typeof(MethodCallModule)
        );
        Assert.AreEqual("hello world", expr.Evaluate(module));
    }

    #endregion

    #region Method Overload Resolution

    [TestMethod]
    public void MethodOverload_IntParam()
    {
        var module = new MethodCallModule();
        var expr = FieldExpression<double>.Compile(
            "%.Overloaded(%.ReturnInt())",
            new(),
            typeof(MethodCallModule)
        );
        // Should call int overload
        Assert.AreEqual(10.0, expr.Evaluate(module));
    }

    [TestMethod]
    public void MethodOverload_DoubleParam()
    {
        var module = new MethodCallModule();
        var expr = FieldExpression<double>.Compile(
            "%.Overloaded(5)",
            new(),
            typeof(MethodCallModule)
        );
        // Should call double overload
        Assert.AreEqual(10.0, expr.Evaluate(module));
    }

    [TestMethod]
    public void MethodOverload_StringParam()
    {
        var module = new MethodCallModule();
        var expr = FieldExpression<double>.Compile(
            "%.Overloaded(\"3.14\")",
            new(),
            typeof(MethodCallModule)
        );
        // Should call string overload
        Assert.AreEqual(3.14, expr.Evaluate(module));
    }

    #endregion

    #region Return Type Conversion

    [TestMethod]
    public void ReturnType_IntToDouble()
    {
        var module = new MethodCallModule();
        var expr = FieldExpression<double>.Compile(
            "%.ReturnInt()",
            new(),
            typeof(MethodCallModule)
        );
        Assert.AreEqual(10.0, expr.Evaluate(module));
    }

    [TestMethod]
    public void ReturnType_String()
    {
        var module = new MethodCallModule();
        var expr = FieldExpression<string>.Compile(
            "%.ReturnString()",
            new(),
            typeof(MethodCallModule)
        );
        Assert.AreEqual("test", expr.Evaluate(module));
    }

    [TestMethod]
    public void ReturnType_Bool()
    {
        var module = new MethodCallModule();
        var expr = FieldExpression<bool>.Compile("%.ReturnBool()", new(), typeof(MethodCallModule));
        Assert.IsTrue(expr.Evaluate(module));
    }

    #endregion

    #region Method Calls in Expressions

    [TestMethod]
    public void MethodInArithmetic()
    {
        var module = new MethodCallModule();
        var expr = FieldExpression<double>.Compile(
            "%.Add(5, 3) * 2",
            new(),
            typeof(MethodCallModule)
        );
        Assert.AreEqual(16.0, expr.Evaluate(module));
    }

    [TestMethod]
    public void NestedMethodCalls()
    {
        var module = new MethodCallModule();
        var expr = FieldExpression<double>.Compile(
            "%.Add(%.SingleParam(5), 10)",
            new(),
            typeof(MethodCallModule)
        );
        // SingleParam(5) = 10, Add(10, 10) = 20
        Assert.AreEqual(20.0, expr.Evaluate(module));
    }

    [TestMethod]
    public void MethodCallWithFieldParam()
    {
        var module = new MethodWithFieldsModule { value = 7 };
        var expr = FieldExpression<double>.Compile(
            "%.Double(%value)",
            new(),
            typeof(MethodWithFieldsModule)
        );
        Assert.AreEqual(14.0, expr.Evaluate(module));
    }

    class MethodWithFieldsModule : PartModule
    {
        public double value;

        public double Double(double x) => x * 2;
    }

    #endregion

    #region Edge Cases

    [TestMethod]
    public void EdgeCase_MethodReturningNull()
    {
        var module = new NullReturningModule();
        var expr = FieldExpression<string>.Compile(
            "%.ReturnNull() ?? \"fallback\"",
            new(),
            typeof(NullReturningModule)
        );
        Assert.AreEqual("fallback", expr.Evaluate(module));
    }

    class NullReturningModule : PartModule
    {
        public string ReturnNull() => null;
    }

    [TestMethod]
    public void EdgeCase_MethodWithManyParams()
    {
        var module = new ManyParamsModule();
        var expr = FieldExpression<double>.Compile(
            "%.Sum(1, 2, 3, 4, 5)",
            new(),
            typeof(ManyParamsModule)
        );
        Assert.AreEqual(15.0, expr.Evaluate(module));
    }

    class ManyParamsModule : PartModule
    {
        public double Sum(double a, double b, double c, double d, double e) => a + b + c + d + e;
    }

    #endregion
}
