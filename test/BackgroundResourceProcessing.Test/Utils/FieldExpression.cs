using BackgroundResourceProcessing.Utils;
using KSP.Testing;

namespace BackgroundResourceProcessing.Test.Utils;

public sealed class FieldExpressionTests : BRPTestBase
{
    [TestInfo("FieldExpressionTests_TestNumbers")]
    public void TestNumbers()
    {
        ConfigNode node = new();

        FieldExpression<double>.Compile("1e-6", node);
        FieldExpression<double>.Compile("1e+10", node);
        FieldExpression<double>.Compile("1", node);
        FieldExpression<double>.Compile("-88", node);
        FieldExpression<double>.Compile("1e6", node);
    }

    class IndexNullTest : PartModule
    {
        public double[] array = null;
    }

    [TestInfo("FieldExpressionTests_IndexNull")]
    public void IndexNull()
    {
        ConfigNode node = new();
        IndexNullTest module = new();

        var expr = FieldExpression<double>.Compile("%array[3] ?? 5", node, typeof(IndexNullTest));
        Assert.AreEqual(expr.Evaluate(module), 5.0);
    }

    [TestInfo("FieldExpressionTests_ParseEnum")]
    public void ParseEnum()
    {
        ConfigNode node = new();
        IndexNullTest module = new();

        var expr = FieldExpression<ResourceFlowMode>.Compile(
            "NO_FLOW",
            node,
            typeof(IndexNullTest)
        );
        Assert.AreEqual(expr.Evaluate(module), ResourceFlowMode.NO_FLOW);
    }

    class FloatCurveTest : PartModule
    {
        public double Evaluate(double value) => value;

        public float EvaluateF(float value) => value;

        public double NoParams() => 1.0;
    }

    [TestInfo("FieldExpressionTests_EvaluateFloatCurve")]
    public void EvaluateFloatCurve()
    {
        FloatCurveTest module = new();

        var expr = FieldExpression<double>.Compile("%.Evaluate(75)", new(), typeof(FloatCurveTest));
        Assert.AreEqual(75.0, expr.Evaluate(module));
    }

    [TestInfo("FieldExpressionTests_EvaluateFloatMethodCurve")]
    public void EvaluateFloatMethodCurve()
    {
        FloatCurveTest module = new();

        var expr = FieldExpression<double>.Compile(
            "%.EvaluateF(75)",
            new(),
            typeof(FloatCurveTest)
        );
        Assert.AreEqual(75.0, expr.Evaluate(module));
    }

    [TestInfo("FieldExpressionTests_EvaluateNoParams")]
    public void EvaluateNoParams()
    {
        FloatCurveTest module = new();
        var expr = FieldExpression<double>.Compile("%.NoParams()", new(), typeof(FloatCurveTest));
        Assert.AreEqual(1.0, expr.Evaluate(module));
    }
}
