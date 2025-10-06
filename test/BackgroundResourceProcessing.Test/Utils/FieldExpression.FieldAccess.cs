using BackgroundResourceProcessing.Utils;

namespace BackgroundResourceProcessing.Test.Utils;

[TestClass]
public sealed class FieldExpressionFieldAccessTests
{
    #region Test Helper Classes

    class BasicFieldsModule : PartModule
    {
        public string PublicField = "PublicValue";
#pragma warning disable CS0414
        private string PrivateField = "PrivateValue";
#pragma warning restore CS0414
        protected string ProtectedField = "ProtectedValue";
        internal string InternalField = "InternalValue";

        public string PublicProperty { get; set; } = "PublicPropertyValue";
        public string ReadOnlyProperty => "ReadOnlyValue";
        public string WriteOnlyProperty
        {
            set { }
        }

        public string ComputedProperty => PublicField + "_Computed";
    }

    class NestedFieldsModule : PartModule
    {
        public NestedObject Nested = new();
        public NestedObject NullNested = null;

        public class NestedObject
        {
            public string Value = "NestedValue";
            public DeepNestedObject Deep = new();

            public class DeepNestedObject
            {
                public string DeeperValue = "DeeperValue";
                public int Number = 42;
            }
        }
    }

    class NullableFieldsModule : PartModule
    {
        public int? NullableInt = 100;
        public int? NullableNull = null;
        public double? NullableDouble = 3.14;

        public NestedWithNullable Nested = new();

        public class NestedWithNullable
        {
            public string Value = "Value";
            public int? Number = 50;
        }
    }

    class TypeCoercionModule : PartModule
    {
        public int IntField = 42;
        public float FloatField = 3.14f;
        public double DoubleField = 2.718;
        public byte ByteField = 255;
        public long LongField = 9999999999L;

        public ResourceFlowMode EnumField = ResourceFlowMode.STACK_PRIORITY_SEARCH;
        public string EnumString = "ALL_VESSEL";
    }

    class DerivedFieldsBase : PartModule
    {
        public string BaseField = "BaseValue";
        public object DynamicField;
    }

    class DerivedFieldsModule : DerivedFieldsBase
    {
        public string DerivedField = "DerivedValue";

        public DerivedFieldsModule()
        {
            DynamicField = new DynamicNestedObject();
        }

        public class DynamicNestedObject
        {
            public string RuntimeField = "RuntimeValue";
            public int RuntimeNumber = 123;
        }
    }

    class ExceptionThrowingModule : PartModule
    {
        public string NormalField = "Normal";

        public string ThrowingProperty
        {
            get => throw new InvalidOperationException("Property access failed");
        }
    }

    class CollectionFieldsModule : PartModule
    {
        public string[] StringArray = ["First", "Second", "Third"];
        public List<string> StringList = ["ListFirst", "ListSecond"];
        public Dictionary<string, int> IntDict = new() { ["key1"] = 10, ["key2"] = 20 };
    }

    #endregion

    #region Basic Field Access Tests

    [TestMethod]
    public void TestPublicFieldAccess()
    {
        var module = new BasicFieldsModule();
        var expr = FieldExpression<string>.Compile(
            "%PublicField",
            new(),
            typeof(BasicFieldsModule)
        );

        Assert.AreEqual("PublicValue", expr.Evaluate(module));
    }

    [TestMethod]
    public void TestPrivateFieldAccess()
    {
        var module = new BasicFieldsModule();
        var expr = FieldExpression<string>.Compile(
            "%PrivateField",
            new(),
            typeof(BasicFieldsModule)
        );

        Assert.AreEqual("PrivateValue", expr.Evaluate(module));
    }

    [TestMethod]
    public void TestProtectedFieldAccess()
    {
        var module = new BasicFieldsModule();
        var expr = FieldExpression<string>.Compile(
            "%ProtectedField",
            new(),
            typeof(BasicFieldsModule)
        );

        Assert.AreEqual("ProtectedValue", expr.Evaluate(module));
    }

    [TestMethod]
    public void TestInternalFieldAccess()
    {
        var module = new BasicFieldsModule();
        var expr = FieldExpression<string>.Compile(
            "%InternalField",
            new(),
            typeof(BasicFieldsModule)
        );

        Assert.AreEqual("InternalValue", expr.Evaluate(module));
    }

    #endregion

    #region Property Access Tests

    [TestMethod]
    public void TestReadablePropertyAccess()
    {
        var module = new BasicFieldsModule();
        var expr = FieldExpression<string>.Compile(
            "%PublicProperty",
            new(),
            typeof(BasicFieldsModule)
        );

        Assert.AreEqual("PublicPropertyValue", expr.Evaluate(module));
    }

    [TestMethod]
    public void TestReadOnlyPropertyAccess()
    {
        var module = new BasicFieldsModule();
        var expr = FieldExpression<string>.Compile(
            "%ReadOnlyProperty",
            new(),
            typeof(BasicFieldsModule)
        );

        Assert.AreEqual("ReadOnlyValue", expr.Evaluate(module));
    }

    [TestMethod]
    public void TestComputedPropertyAccess()
    {
        var module = new BasicFieldsModule();
        var expr = FieldExpression<string>.Compile(
            "%ComputedProperty",
            new(),
            typeof(BasicFieldsModule)
        );

        Assert.AreEqual("PublicValue_Computed", expr.Evaluate(module));
    }

    [TestMethod]
    [ExpectedException(typeof(BackgroundResourceProcessing.Expr.CompilationException))]
    public void TestWriteOnlyPropertyAccessThrows()
    {
        var module = new BasicFieldsModule();
        // This should throw CompilationException because write-only properties are not readable
        var expr = FieldExpression<string>.Compile(
            "%WriteOnlyProperty",
            new(),
            typeof(BasicFieldsModule)
        );
    }

    #endregion

    #region Nested Field Access Tests

    [TestMethod]
    public void TestSingleLevelNestedFieldAccess()
    {
        var module = new NestedFieldsModule();
        var expr = FieldExpression<string>.Compile(
            "%Nested.Value",
            new(),
            typeof(NestedFieldsModule)
        );

        Assert.AreEqual("NestedValue", expr.Evaluate(module));
    }

    [TestMethod]
    public void TestMultiLevelNestedFieldAccess()
    {
        var module = new NestedFieldsModule();
        var expr = FieldExpression<string>.Compile(
            "%Nested.Deep.DeeperValue",
            new(),
            typeof(NestedFieldsModule)
        );

        Assert.AreEqual("DeeperValue", expr.Evaluate(module));
    }

    [TestMethod]
    public void TestMultiLevelNestedNumericAccess()
    {
        var module = new NestedFieldsModule();
        var expr = FieldExpression<int>.Compile(
            "%Nested.Deep.Number",
            new(),
            typeof(NestedFieldsModule)
        );

        Assert.AreEqual(42, expr.Evaluate(module));
    }

    [TestMethod]
    public void TestNestedFieldAccessOnNull()
    {
        var module = new NestedFieldsModule();
        var expr = FieldExpression<string>.Compile(
            "%NullNested.Value",
            new(),
            typeof(NestedFieldsModule)
        );

        Assert.IsNull(expr.Evaluate(module));
    }

    [TestMethod]
    public void TestDeepNestedFieldAccessOnNull()
    {
        var module = new NestedFieldsModule();
        var expr = FieldExpression<string>.Compile(
            "%NullNested.Deep.DeeperValue",
            new(),
            typeof(NestedFieldsModule)
        );

        Assert.IsNull(expr.Evaluate(module));
    }

    #endregion

    #region Non-Existent Field Tests

    [TestMethod]
    public void TestNonExistentField()
    {
        var module = new BasicFieldsModule();
        var expr = FieldExpression<string>.Compile(
            "%NonExistentField",
            new(),
            typeof(BasicFieldsModule)
        );

        Assert.IsNull(expr.Evaluate(module));
    }

    [TestMethod]
    public void TestNonExistentNestedField()
    {
        var module = new NestedFieldsModule();
        var expr = FieldExpression<string>.Compile(
            "%Nested.NonExistent",
            new(),
            typeof(NestedFieldsModule)
        );

        Assert.IsNull(expr.Evaluate(module));
    }

    [TestMethod]
    public void TestCaseSensitiveFieldAccess()
    {
        var module = new BasicFieldsModule();
        // Field is "PublicField" but we access "publicfield" - should return null
        var expr = FieldExpression<string>.Compile(
            "%publicfield",
            new(),
            typeof(BasicFieldsModule)
        );

        Assert.IsNull(expr.Evaluate(module));
    }

    #endregion

    #region Module Self-Reference Tests

    [TestMethod]
    public void TestModuleSelfReference()
    {
        var module = new BasicFieldsModule();
        var expr = FieldExpression<PartModule>.Compile("%", new(), typeof(BasicFieldsModule));

        Assert.IsTrue(expr.TryEvaluate(module, out var result));
        Assert.IsNotNull(result);
        Assert.AreSame(module, result);
    }

    [TestMethod]
    public void TestModuleSelfReferenceWithDotAccess()
    {
        var module = new BasicFieldsModule();
        var expr = FieldExpression<string>.Compile(
            "%.PublicField",
            new(),
            typeof(BasicFieldsModule)
        );

        Assert.AreEqual("PublicValue", expr.Evaluate(module));
    }

    #endregion

    #region Index-Based Field Access Tests

    [TestMethod]
    public void TestStringIndexFieldAccess()
    {
        var module = new BasicFieldsModule();
        var expr = FieldExpression<string>.Compile(
            "%[\"PublicField\"]",
            new(),
            typeof(BasicFieldsModule)
        );

        Assert.AreEqual("PublicValue", expr.Evaluate(module));
    }

    [TestMethod]
    public void TestStringIndexPropertyAccess()
    {
        var module = new BasicFieldsModule();
        var expr = FieldExpression<string>.Compile(
            "%[\"PublicProperty\"]",
            new(),
            typeof(BasicFieldsModule)
        );

        Assert.AreEqual("PublicPropertyValue", expr.Evaluate(module));
    }

    [TestMethod]
    public void TestStringIndexNonExistentField()
    {
        var module = new BasicFieldsModule();
        var expr = FieldExpression<bool>.Compile(
            "%[\"NonExistent\"] == null",
            new(),
            typeof(BasicFieldsModule)
        );

        Assert.IsTrue(expr.Evaluate(module));
    }

    #endregion

    #region Dynamic Type Tests (Runtime Type Resolution)

    [TestMethod]
    public void TestDerivedClassFieldAccess()
    {
        var module = new DerivedFieldsModule();
        var expr = FieldExpression<string>.Compile(
            "%DerivedField",
            new(),
            typeof(DerivedFieldsModule)
        );

        Assert.AreEqual("DerivedValue", expr.Evaluate(module));
    }

    [TestMethod]
    public void TestBaseClassFieldFromDerived()
    {
        var module = new DerivedFieldsModule();
        var expr = FieldExpression<string>.Compile(
            "%BaseField",
            new(),
            typeof(DerivedFieldsModule)
        );

        Assert.AreEqual("BaseValue", expr.Evaluate(module));
    }

    [TestMethod]
    public void TestRuntimeTypeFieldAccess()
    {
        var module = new DerivedFieldsModule();
        // DynamicField is typed as 'object' but contains DynamicNestedObject at runtime
        // The DoFieldAccess method should use reflection to access RuntimeField
        var expr = FieldExpression<string>.Compile(
            "%DynamicField.RuntimeField",
            new(),
            typeof(DerivedFieldsModule)
        );

        // This actually works! The DoFieldAccess method uses reflection at runtime
        Assert.AreEqual("RuntimeValue", expr.Evaluate(module));
    }

    [TestMethod]
    public void TestRuntimeTypeNestedNumericAccess()
    {
        var module = new DerivedFieldsModule();
        var expr = FieldExpression<double>.Compile(
            "%DynamicField.RuntimeNumber",
            new(),
            typeof(DerivedFieldsModule)
        );

        // Runtime reflection also handles numeric type conversion
        Assert.AreEqual(123.0, expr.Evaluate(module));
    }

    #endregion

    #region Type Coercion Tests

    [TestMethod]
    public void TestIntToDoubleCoercion()
    {
        var module = new TypeCoercionModule();
        var expr = FieldExpression<double>.Compile("%IntField", new(), typeof(TypeCoercionModule));

        Assert.AreEqual(42.0, expr.Evaluate(module));
    }

    [TestMethod]
    public void TestFloatToDoubleCoercion()
    {
        var module = new TypeCoercionModule();
        var expr = FieldExpression<double>.Compile(
            "%FloatField",
            new(),
            typeof(TypeCoercionModule)
        );

        var result = expr.Evaluate(module);
        Assert.IsNotNull(result);
        Assert.AreEqual(3.14, result.Value, 0.001);
    }

    [TestMethod]
    public void TestByteToDoubleCoercion()
    {
        var module = new TypeCoercionModule();
        var expr = FieldExpression<double>.Compile("%ByteField", new(), typeof(TypeCoercionModule));

        Assert.AreEqual(255.0, expr.Evaluate(module));
    }

    [TestMethod]
    public void TestLongToDoubleCoercion()
    {
        var module = new TypeCoercionModule();
        var expr = FieldExpression<double>.Compile("%LongField", new(), typeof(TypeCoercionModule));

        Assert.AreEqual(9999999999.0, expr.Evaluate(module));
    }

    [TestMethod]
    public void TestEnumFieldAccess()
    {
        var module = new TypeCoercionModule();
        var expr = FieldExpression<ResourceFlowMode>.Compile(
            "%EnumField",
            new(),
            typeof(TypeCoercionModule)
        );

        Assert.IsTrue(expr.TryEvaluate(module, out var result));
        Assert.AreEqual(ResourceFlowMode.STACK_PRIORITY_SEARCH, result);
    }

    #endregion

    #region Nullable Type Tests

    [TestMethod]
    public void TestNullableIntAccess()
    {
        var module = new NullableFieldsModule();
        var expr = FieldExpression<double>.Compile(
            "%NullableInt",
            new(),
            typeof(NullableFieldsModule)
        );

        Assert.AreEqual(100.0, expr.Evaluate(module));
    }

    [TestMethod]
    public void TestNullableNullAccess()
    {
        var module = new NullableFieldsModule();
        var expr = FieldExpression<bool>.Compile(
            "%NullableNull == null",
            new(),
            typeof(NullableFieldsModule)
        );

        // Nullable value type returns null when the value is null
        Assert.IsTrue(expr.Evaluate(module));
    }

    [TestMethod]
    public void TestNullableDoubleAccess()
    {
        var module = new NullableFieldsModule();
        var expr = FieldExpression<double>.Compile(
            "%NullableDouble",
            new(),
            typeof(NullableFieldsModule)
        );

        var result = expr.Evaluate(module);
        Assert.IsNotNull(result);
        Assert.AreEqual(3.14, result.Value, 0.001);
    }

    [TestMethod]
    public void TestNestedFieldWithNullable()
    {
        var module = new NullableFieldsModule();
        var expr = FieldExpression<double>.Compile(
            "%Nested.Number",
            new(),
            typeof(NullableFieldsModule)
        );

        var result = expr.Evaluate(module);
        Assert.IsNotNull(result);
        Assert.AreEqual(50.0, result.Value, 0.001);
    }

    #endregion

    #region Exception Handling Tests

    [TestMethod]
    public void TestExceptionInPropertyGetter()
    {
        var module = new ExceptionThrowingModule();
        // Property getter throws, should catch the exception and return null
        // The exception is logged as an error which throws TestLogErrorException in test environment
        var expr = FieldExpression<string>.Compile(
            "%ThrowingProperty",
            new(),
            typeof(ExceptionThrowingModule)
        );

        // This will throw because LogUtil.Error throws in test environment
        Assert.ThrowsException<BackgroundResourceProcessing.Test.Setup.TestLogErrorException>(() =>
            expr.Evaluate(module)
        );
    }

    [TestMethod]
    public void TestNormalFieldAfterException()
    {
        var module = new ExceptionThrowingModule();
        var expr = FieldExpression<string>.Compile(
            "%NormalField",
            new(),
            typeof(ExceptionThrowingModule)
        );

        Assert.AreEqual("Normal", expr.Evaluate(module));
    }

    #endregion

    #region Collection Field Tests

    [TestMethod]
    public void TestArrayFieldAccess()
    {
        var module = new CollectionFieldsModule();
        var expr = FieldExpression<string[]>.Compile(
            "%StringArray",
            new(),
            typeof(CollectionFieldsModule)
        );

        Assert.IsTrue(expr.TryEvaluate(module, out var result));
        Assert.IsNotNull(result);
        Assert.IsInstanceOfType(result, typeof(string[]));
    }

    [TestMethod]
    public void TestListFieldAccess()
    {
        var module = new CollectionFieldsModule();
        var expr = FieldExpression<List<string>>.Compile(
            "%StringList",
            new(),
            typeof(CollectionFieldsModule)
        );

        Assert.IsTrue(expr.TryEvaluate(module, out var result));
        Assert.IsNotNull(result);
        Assert.IsInstanceOfType(result, typeof(List<string>));
    }

    [TestMethod]
    public void TestDictionaryFieldAccess()
    {
        var module = new CollectionFieldsModule();
        var expr = FieldExpression<Dictionary<string, int>>.Compile(
            "%IntDict",
            new(),
            typeof(CollectionFieldsModule)
        );

        Assert.IsTrue(expr.TryEvaluate(module, out var result));
        Assert.IsNotNull(result);
        Assert.IsInstanceOfType(result, typeof(Dictionary<string, int>));
    }

    #endregion

    #region Edge Cases

    [TestMethod]
    public void TestFieldReturningNull()
    {
        var module = new NestedFieldsModule();
        var expr = FieldExpression<NestedFieldsModule.NestedObject>.Compile(
            "%NullNested",
            new(),
            typeof(NestedFieldsModule)
        );

        // Reference types return true even when null
        Assert.IsTrue(expr.TryEvaluate(module, out var result));
        Assert.IsNull(result);
    }

    [TestMethod]
    public void TestValueTypeFieldAccess()
    {
        var module = new TypeCoercionModule();
        var expr = FieldExpression<int>.Compile("%IntField", new(), typeof(TypeCoercionModule));

        Assert.AreEqual(42, expr.Evaluate(module));
    }

    [TestMethod]
    public void TestChainedNullPropagation()
    {
        var module = new NestedFieldsModule();
        module.Nested = null;

        var expr = FieldExpression<string>.Compile(
            "%Nested.Deep.DeeperValue",
            new(),
            typeof(NestedFieldsModule)
        );

        // Should propagate null through the chain
        Assert.IsNull(expr.Evaluate(module));
    }

    #endregion
}
