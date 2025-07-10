using BackgroundResourceProcessing.Utils;

namespace BackgroundResourceProcessing.Test.Utils
{
    [TestClass]
    public sealed class ModuleFilterTests
    {
        [TestMethod]
        public void TestConstants()
        {
            ModuleFilter filter;
            ConfigNode node = new();
            node.AddValue("a", "True");
            node.AddValue("b", "False");
            node.AddValue("c", "null");

            filter = ModuleFilter.Compile("@a == true", node);
            Assert.IsTrue(filter.Invoke(null));

            filter = ModuleFilter.Compile("@b == false", node);
            Assert.IsTrue(filter.Invoke(null));

            filter = ModuleFilter.Compile("@c == null", node);
            Assert.IsFalse(filter.Invoke(null));

            filter = ModuleFilter.Compile("?d == null", node);
            Assert.IsTrue(filter.Invoke(null));
        }

        [TestMethod]
        public void TestConfigReadNonNull()
        {
            ModuleFilter filter;
            ConfigNode node = new();
            node.AddValue("a", "test1");
            node.AddValue("b", "test2");

            filter = ModuleFilter.Compile("@a == @a", node);
            Assert.IsTrue(filter.Invoke(null));

            filter = ModuleFilter.Compile("@a != @b", node);
            Assert.IsTrue(filter.Invoke(null));

            try
            {
                ModuleFilter.Compile("@c == @a", node);
                Assert.Fail();
            }
            catch (ModuleFilterException) { }
        }

        [TestMethod]
        public void TestConfigReadNullable()
        {
            ModuleFilter filter;
            ConfigNode node = new();
            node.AddValue("a", "test1");
            node.AddValue("b", "test1");
            node.AddValue("c", "test2");

            filter = ModuleFilter.Compile("?a == @a", node);
            Assert.IsTrue(filter.Invoke(null));

            filter = ModuleFilter.Compile("?a == @b", node);
            Assert.IsTrue(filter.Invoke(null));

            filter = ModuleFilter.Compile("?a == @c", node);
            Assert.IsFalse(filter.Invoke(null));

            filter = ModuleFilter.Compile("?a == ?z", node);
            Assert.IsFalse(filter.Invoke(null));

            filter = ModuleFilter.Compile("?a != ?z", node);
            Assert.IsTrue(filter.Invoke(null));

            filter = ModuleFilter.Compile("?x == ?y", node);
            Assert.IsTrue(filter.Invoke(null));
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

            public ResourceFlowMode FlowMode = ResourceFlowMode.ALL_VESSEL;
        }

        [TestMethod]
        public void TestFieldAccess()
        {
            ModuleFilter filter;
            ConfigNode node = new();
            node.AddValue("Value1", "TestValue1");
            node.AddValue("Value2", "Enabled");
            node.AddValue("Value3", "Blargl");
            node.AddValue("Value4", "ALL_VESSEL");
            node.AddValue("name", "DummyPartModule");

            DummyPartModule module = new();

            filter = ModuleFilter.Compile("%Field1 == @Value1", node);
            Assert.IsTrue(filter.Invoke(module));

            filter = ModuleFilter.Compile("%Field1 != @Value3", node);
            Assert.IsTrue(filter.Invoke(module));

            filter = ModuleFilter.Compile("%Field2 != null", node);
            Assert.IsTrue(filter.Invoke(module));

            filter = ModuleFilter.Compile("%Field3 == null", node);
            Assert.IsTrue(filter.Invoke(module));

            filter = ModuleFilter.Compile("%True == true", node);
            Assert.IsTrue(filter.Invoke(module));

            filter = ModuleFilter.Compile("%False == false", node);
            Assert.IsTrue(filter.Invoke(module));

            filter = ModuleFilter.Compile("%FlowMode == @Value4", node);
            Assert.IsTrue(filter.Invoke(module));

            filter = ModuleFilter.Compile("%FlowMode != %Field1", node);
            Assert.IsTrue(filter.Invoke(module));

            filter = ModuleFilter.Compile("%True != %False", node);
            Assert.IsTrue(filter.Invoke(module));

            filter = ModuleFilter.Compile("%BoolPropertyTrue", node);
            Assert.IsTrue(filter.Invoke(module));

            filter = ModuleFilter.Compile("%BoolPropertyFalse", node);
            Assert.IsFalse(filter.Invoke(module));
        }

        [TestMethod]
        public void TestFilterCompilation()
        {
            ConfigNode node = new();
            node.AddValue("ConverterName", "LF+Ox");
            node.AddValue("moduleID", "converter1");

            try
            {
                ModuleFilter.Compile("%ConverterName = ?ConverterName", node);
                Assert.Fail();
            }
            catch (ModuleFilterException) { }

            ModuleFilter.Compile("%moduleID == @moduleID", node);
        }

        [TestMethod]
        public void TestUnaryExpressions()
        {
            ConfigNode node = new();
            node.AddValue("ConverterName", "LF+Ox");
            node.AddValue("moduleID", "converter1");

            ModuleFilter filter;
            DummyPartModule module = new();

            filter = ModuleFilter.Compile("%True || %False", node);
            Assert.IsTrue(filter.Invoke(module));

            filter = ModuleFilter.Compile("%True && !%False", node);
            Assert.IsTrue(filter.Invoke(module));

            filter = ModuleFilter.Compile("!%True", node);
            Assert.IsFalse(filter.Invoke(module));
        }
    }
}
