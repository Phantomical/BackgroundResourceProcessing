using BackgroundResourceProcessing.Utils;

namespace BackgroundResourceProcessing.Test.Utils;

/// <summary>
/// Tests for field and property resolution, including dynamic field access via DoFieldAccess.
/// Tests the Parser.ParseFieldAccess and Methods.DoFieldAccess functionality.
/// </summary>
[TestClass]
public sealed class FieldExpressionFieldResolutionTests
{
    #region Test Helper Classes

#pragma warning disable CS0414
    class FieldResolutionModule : PartModule
    {
        public string publicField = "public";
        private string privateField = "private";
        protected string protectedField = "protected";
        internal string internalField = "internal";

        public string PublicProperty { get; set; } = "property";
        public string ReadOnlyProperty => "readonly";
        public string ComputedProperty => publicField + "_computed";

        public NestedClass nested = new();

        public class NestedClass
        {
            public string nestedField = "nested";
            public DeepNestedClass deep = new();

            public class DeepNestedClass
            {
                public string deepField = "deep";
            }
        }
    }
#pragma warning restore CS0414

    class DynamicTypeModule : PartModule
    {
        public object dynamicField;

        public DynamicTypeModule()
        {
            dynamicField = new DynamicObject();
        }

        public class DynamicObject
        {
            public string runtimeField = "runtime";
            public int runtimeNumber = 42;
        }
    }

    class CollisionModule : PartModule
    {
        // Field and property with same name (different casing)
        public string value = "field_value";
        public string Value => "property_value";
    }

    #endregion

    #region Basic Field Access

    [TestMethod]
    public void FieldAccess_PublicField()
    {
        var module = new FieldResolutionModule();
        var expr = FieldExpression<string>.Compile(
            "%publicField",
            new(),
            typeof(FieldResolutionModule)
        );
        Assert.AreEqual("public", expr.Evaluate(module));
    }

    [TestMethod]
    public void FieldAccess_PrivateField()
    {
        var module = new FieldResolutionModule();
        var expr = FieldExpression<string>.Compile(
            "%privateField",
            new(),
            typeof(FieldResolutionModule)
        );
        Assert.AreEqual("private", expr.Evaluate(module));
    }

    [TestMethod]
    public void FieldAccess_ProtectedField()
    {
        var module = new FieldResolutionModule();
        var expr = FieldExpression<string>.Compile(
            "%protectedField",
            new(),
            typeof(FieldResolutionModule)
        );
        Assert.AreEqual("protected", expr.Evaluate(module));
    }

    [TestMethod]
    public void FieldAccess_InternalField()
    {
        var module = new FieldResolutionModule();
        var expr = FieldExpression<string>.Compile(
            "%internalField",
            new(),
            typeof(FieldResolutionModule)
        );
        Assert.AreEqual("internal", expr.Evaluate(module));
    }

    #endregion

    #region Property Access

    [TestMethod]
    public void PropertyAccess_ReadWrite()
    {
        var module = new FieldResolutionModule();
        var expr = FieldExpression<string>.Compile(
            "%PublicProperty",
            new(),
            typeof(FieldResolutionModule)
        );
        Assert.AreEqual("property", expr.Evaluate(module));
    }

    [TestMethod]
    public void PropertyAccess_ReadOnly()
    {
        var module = new FieldResolutionModule();
        var expr = FieldExpression<string>.Compile(
            "%ReadOnlyProperty",
            new(),
            typeof(FieldResolutionModule)
        );
        Assert.AreEqual("readonly", expr.Evaluate(module));
    }

    [TestMethod]
    public void PropertyAccess_Computed()
    {
        var module = new FieldResolutionModule();
        var expr = FieldExpression<string>.Compile(
            "%ComputedProperty",
            new(),
            typeof(FieldResolutionModule)
        );
        Assert.AreEqual("public_computed", expr.Evaluate(module));
    }

    #endregion

    #region Nested Field Access

    [TestMethod]
    public void NestedFieldAccess_SingleLevel()
    {
        var module = new FieldResolutionModule();
        var expr = FieldExpression<string>.Compile(
            "%nested.nestedField",
            new(),
            typeof(FieldResolutionModule)
        );
        Assert.AreEqual("nested", expr.Evaluate(module));
    }

    [TestMethod]
    public void NestedFieldAccess_MultiLevel()
    {
        var module = new FieldResolutionModule();
        var expr = FieldExpression<string>.Compile(
            "%nested.deep.deepField",
            new(),
            typeof(FieldResolutionModule)
        );
        Assert.AreEqual("deep", expr.Evaluate(module));
    }

    [TestMethod]
    public void NestedFieldAccess_NullIntermediate()
    {
        var module = new FieldResolutionModule { nested = null };
        var expr = FieldExpression<string>.Compile(
            "%nested.nestedField ?? \"fallback\"",
            new(),
            typeof(FieldResolutionModule)
        );
        Assert.AreEqual("fallback", expr.Evaluate(module));
    }

    [TestMethod]
    public void NestedFieldAccess_NullDeep()
    {
        var module = new FieldResolutionModule();
        module.nested.deep = null;
        var expr = FieldExpression<string>.Compile(
            "%nested.deep.deepField ?? \"fallback\"",
            new(),
            typeof(FieldResolutionModule)
        );
        Assert.AreEqual("fallback", expr.Evaluate(module));
    }

    #endregion

    #region Dynamic Type Resolution

    [TestMethod]
    public void DynamicFieldAccess_RuntimeType()
    {
        var module = new DynamicTypeModule();
        // dynamicField is typed as object but contains DynamicObject at runtime
        var expr = FieldExpression<string>.Compile(
            "%dynamicField.runtimeField",
            new(),
            typeof(DynamicTypeModule)
        );
        Assert.AreEqual("runtime", expr.Evaluate(module));
    }

    [TestMethod]
    public void DynamicFieldAccess_RuntimeNumeric()
    {
        var module = new DynamicTypeModule();
        var expr = FieldExpression<double>.Compile(
            "%dynamicField.runtimeNumber",
            new(),
            typeof(DynamicTypeModule)
        );
        Assert.AreEqual(42.0, expr.Evaluate(module));
    }

    [TestMethod]
    public void DynamicFieldAccess_NullDynamic()
    {
        var module = new DynamicTypeModule { dynamicField = null };
        var expr = FieldExpression<string>.Compile(
            "%dynamicField.runtimeField ?? \"fallback\"",
            new(),
            typeof(DynamicTypeModule)
        );
        Assert.AreEqual("fallback", expr.Evaluate(module));
    }

    #endregion

    #region String Index Access for Fields

    [TestMethod]
    public void StringIndexAccess_Field()
    {
        var module = new FieldResolutionModule();
        var expr = FieldExpression<string>.Compile(
            "%[\"publicField\"]",
            new(),
            typeof(FieldResolutionModule)
        );
        Assert.AreEqual("public", expr.Evaluate(module));
    }

    [TestMethod]
    public void StringIndexAccess_Property()
    {
        var module = new FieldResolutionModule();
        var expr = FieldExpression<string>.Compile(
            "%[\"PublicProperty\"]",
            new(),
            typeof(FieldResolutionModule)
        );
        Assert.AreEqual("property", expr.Evaluate(module));
    }

    [TestMethod]
    public void StringIndexAccess_NonExistent()
    {
        var module = new FieldResolutionModule();
        var expr = FieldExpression<string>.Compile(
            "%[\"doesNotExist\"] ?? \"fallback\"",
            new(),
            typeof(FieldResolutionModule)
        );
        Assert.AreEqual("fallback", expr.Evaluate(module));
    }

    #endregion

    #region Case Sensitivity

    [TestMethod]
    public void CaseSensitivity_ExactMatch()
    {
        var module = new FieldResolutionModule();
        var expr = FieldExpression<string>.Compile(
            "%publicField",
            new(),
            typeof(FieldResolutionModule)
        );
        Assert.AreEqual("public", expr.Evaluate(module));
    }

    [TestMethod]
    public void CaseSensitivity_WrongCase_ReturnsNull()
    {
        var module = new FieldResolutionModule();
        var expr = FieldExpression<string>.Compile(
            "%publicfield ?? \"fallback\"",
            new(),
            typeof(FieldResolutionModule)
        );
        // Field names are case-sensitive
        Assert.AreEqual("fallback", expr.Evaluate(module));
    }

    [TestMethod]
    public void CaseSensitivity_FieldVsProperty()
    {
        var module = new CollisionModule();

        // Access lowercase field
        var expr1 = FieldExpression<string>.Compile("%value", new(), typeof(CollisionModule));
        Assert.AreEqual("field_value", expr1.Evaluate(module));

        // Access uppercase property
        var expr2 = FieldExpression<string>.Compile("%Value", new(), typeof(CollisionModule));
        Assert.AreEqual("property_value", expr2.Evaluate(module));
    }

    #endregion

    #region Non-Existent Fields

    [TestMethod]
    public void NonExistentField_ReturnsNull()
    {
        var module = new FieldResolutionModule();
        var expr = FieldExpression<string>.Compile(
            "%doesNotExist ?? \"fallback\"",
            new(),
            typeof(FieldResolutionModule)
        );
        Assert.AreEqual("fallback", expr.Evaluate(module));
    }

    [TestMethod]
    public void NonExistentNestedField()
    {
        var module = new FieldResolutionModule();
        var expr = FieldExpression<string>.Compile(
            "%nested.doesNotExist ?? \"fallback\"",
            new(),
            typeof(FieldResolutionModule)
        );
        Assert.AreEqual("fallback", expr.Evaluate(module));
    }

    #endregion

    #region Module Self-Reference

    [TestMethod]
    public void SelfReference_ModuleObject()
    {
        var module = new FieldResolutionModule();
        var expr = FieldExpression<PartModule>.Compile("%", new(), typeof(FieldResolutionModule));

        var result = expr.Evaluate(module);
        Assert.IsNotNull(result);
        Assert.AreSame(module, result);
    }

    [TestMethod]
    public void SelfReference_WithDotAccess()
    {
        var module = new FieldResolutionModule();
        var expr = FieldExpression<string>.Compile(
            "%.publicField",
            new(),
            typeof(FieldResolutionModule)
        );
        Assert.AreEqual("public", expr.Evaluate(module));
    }

    #endregion

    #region Complex Field Expressions

    [TestMethod]
    public void ComplexExpression_FieldInArithmetic()
    {
        var module = new DynamicTypeModule();
        var expr = FieldExpression<double>.Compile(
            "%dynamicField.runtimeNumber * 2",
            new(),
            typeof(DynamicTypeModule)
        );
        Assert.AreEqual(84.0, expr.Evaluate(module));
    }

    [TestMethod]
    public void ComplexExpression_NestedFieldComparison()
    {
        var module = new FieldResolutionModule();
        var expr = FieldExpression<bool>.Compile(
            "%nested.nestedField == \"nested\"",
            new(),
            typeof(FieldResolutionModule)
        );
        Assert.IsTrue(expr.Evaluate(module));
    }

    [TestMethod]
    public void ComplexExpression_MultipleFieldAccess()
    {
        var module = new FieldResolutionModule();
        var expr = FieldExpression<string>.Compile(
            "%publicField + \"-\" + %PublicProperty",
            new(),
            typeof(FieldResolutionModule)
        );
        Assert.AreEqual("public-property", expr.Evaluate(module));
    }

    #endregion

    #region Edge Cases

    [TestMethod]
    public void EdgeCase_FieldReturningNull()
    {
        var module = new FieldResolutionModule { nested = null };
        var expr = FieldExpression<FieldResolutionModule.NestedClass>.Compile(
            "%nested",
            new(),
            typeof(FieldResolutionModule)
        );
        Assert.IsNull(expr.Evaluate(module));
    }

    [TestMethod]
    public void EdgeCase_ChainedNullPropagation()
    {
        var module = new FieldResolutionModule { nested = null };
        var expr = FieldExpression<string>.Compile(
            "%nested.deep.deepField ?? \"fallback\"",
            new(),
            typeof(FieldResolutionModule)
        );
        Assert.AreEqual("fallback", expr.Evaluate(module));
    }

    [TestMethod]
    public void EdgeCase_PropertyWithSideEffects()
    {
        var module = new CountingPropertyModule();
        var expr = FieldExpression<int>.Compile("%Counter", new(), typeof(CountingPropertyModule));

        // Evaluate multiple times
        Assert.AreEqual(1, expr.Evaluate(module));
        Assert.AreEqual(2, expr.Evaluate(module));
        Assert.AreEqual(3, expr.Evaluate(module));
    }

    class CountingPropertyModule : PartModule
    {
        private int count = 0;
        public int Counter => ++count;
    }

    #endregion
}
