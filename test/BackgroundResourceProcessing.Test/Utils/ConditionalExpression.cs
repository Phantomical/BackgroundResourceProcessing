using BackgroundResourceProcessing.Utils;

namespace BackgroundResourceProcessing.Test.Utils;

[TestClass]
public sealed class ConditionalExpressionTests
{
    public static ConditionalExpression Compile(string expression, ConfigNode node = null)
    {
        return ConditionalExpression.Compile(expression, node ?? new(), typeof(DummyPartModule));
    }

    [TestMethod]
    public void TestConstants()
    {
        ConditionalExpression expr;
        ConfigNode node = new();
        node.AddValue("a", "True");
        node.AddValue("b", "False");
        node.AddValue("c", "null");

        expr = Compile("@a == true", node);
        Assert.IsTrue(expr.Evaluate(null));

        expr = Compile("@b == false", node);
        Assert.IsTrue(expr.Evaluate(null));

        expr = Compile("@c == null", node);
        Assert.IsFalse(expr.Evaluate(null));

        expr = Compile("@c == \"null\"", node);
        Assert.IsTrue(expr.Evaluate(null));

        expr = Compile("@d == null", node);
        Assert.IsTrue(expr.Evaluate(null));
    }

    public class DummyPartModule() : PartModule
    {
        public string Field1 = "TestValue1";
        public string Field2 = "Enabled";
        public string Field3 = null;

        public bool True = true;
        public bool False = false;

        public bool BoolPropertyTrue => true;
        public bool BoolPropertyFalse => false;

        public string[] Array = ["a", "b", "c"];

        public ResourceFlowMode FlowMode = ResourceFlowMode.ALL_VESSEL;

        public ResourceRatio Input = new()
        {
            Ratio = 5,
            ResourceName = "ElectricCharge",
            DumpExcess = true,
            FlowMode = ResourceFlowMode.STACK_PRIORITY_SEARCH,
        };
    }

    [TestMethod]
    public void TestFieldAccess()
    {
        ConditionalExpression expr;
        ConfigNode node = new();
        node.AddValue("Value1", "TestValue1");
        node.AddValue("Value2", "Enabled");
        node.AddValue("Value3", "Blargl");
        node.AddValue("Value4", "ALL_VESSEL");
        node.AddValue("name", "DummyPartModule");

        DummyPartModule module = new();

        expr = Compile("%Field1 == \"TestValue1\"", node);
        Assert.IsTrue(expr.Evaluate(module));

        expr = Compile("%Field2 == \"Enabled\"", node);
        Assert.IsTrue(expr.Evaluate(module));

        expr = Compile("%True", node);
        Assert.IsTrue(expr.Evaluate(module));

        expr = Compile("!%False", node);
        Assert.IsTrue(expr.Evaluate(module));

        expr = Compile("%FlowMode == \"ALL_VESSEL\"");
        Assert.IsTrue(expr.Evaluate(module));
    }

    [TestMethod]
    public void TestNestedFieldAccess()
    {
        ConditionalExpression expr;
        DummyPartModule module = new();

        expr = Compile("%Input.ResourceName == \"ElectricCharge\"");
        Assert.IsTrue(expr.Evaluate(module));

        expr = Compile("%Input.ResourceName.Length == 14");
        Assert.IsTrue(expr.Evaluate(module));

        expr = Compile("%Input.DumpExcess");
        Assert.IsTrue(expr.Evaluate(module));

        expr = Compile("%Input.Ratio == 5");
        Assert.IsTrue(expr.Evaluate(module));

        expr = Compile("%.Input.FlowMode == \"STACK_PRIORITY_SEARCH\"");
        Assert.IsTrue(expr.Evaluate(module));
    }

    [TestMethod]
    public void TestTypeof()
    {
        ConditionalExpression expr;
        DummyPartModule module = new();

        expr = Compile("typeof(%) == \"DummyPartModule\"");
        Assert.IsTrue(expr.Evaluate(module));

        expr = Compile("typeof(%FlowMode) == \"ResourceFlowMode\"");
        Assert.IsTrue(expr.Evaluate(module));
    }

    [TestMethod]
    public void TestStringFieldAccess()
    {
        ConditionalExpression expr;
        DummyPartModule module = new();

        expr = Compile("%[\"Field1\"] == \"TestValue1\"");
        Assert.IsTrue(expr.Evaluate(module));
    }

    [TestMethod]
    public void TestParenthesizedExpression()
    {
        ConditionalExpression expr;
        DummyPartModule module = new();

        expr = Compile("!%BoolPropertyFalse && (%BoolPropertyFalse || %[\"True\"])");
        Assert.IsTrue(expr.Evaluate(module));
    }
}
