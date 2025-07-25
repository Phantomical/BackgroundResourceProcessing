using BackgroundResourceProcessing.Utils;

namespace BackgroundResourceProcessing.Test.Utils;

[TestClass]
public sealed class FieldExpressionTests
{
    [TestMethod]
    public void TestNumbers()
    {
        ConfigNode node = new();

        FieldExpression<double>.Compile("1e-6", node);
        FieldExpression<double>.Compile("1e+10", node);
        FieldExpression<double>.Compile("1", node);
        FieldExpression<double>.Compile("-88", node);
    }

    class IndexNullTest : PartModule
    {
        public double[] array = null;
    }

    [TestMethod]
    public void IndexNull()
    {
        ConfigNode node = new();
        IndexNullTest module = new();

        var expr = FieldExpression<double>.Compile("%array[3] ?? 5", node, typeof(IndexNullTest));
        Assert.AreEqual(expr.Evaluate(module), 5.0);
    }
}
