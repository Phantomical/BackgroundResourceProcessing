using BackgroundResourceProcessing.Utils;

namespace BackgroundResourceProcessing.Test.Utils;

[TestClass]
public class FieldExpressionIndexAccessTests
{
    #region Test Helper Classes

    class ArrayModule : PartModule
    {
        public int[] intArray = [1, 2, 3, 4, 5];
        public double[] doubleArray = [1.1, 2.2, 3.3];
        public string[] stringArray = ["first", "second", "third"];
        public int[] emptyArray = [];
        public int[] nullArray = null;
        public object[] objectArray = [42, "text", 3.14];
    }

    class ListModule : PartModule
    {
        public List<int> intList = new() { 10, 20, 30, 40 };
        public List<string> stringList = new() { "alpha", "beta", "gamma" };
        public List<double> doubleList = new() { 1.5, 2.5, 3.5 };
        public List<int> emptyList = new();
        public List<int> nullList = null;
    }

    class DictionaryModule : PartModule
    {
        public Dictionary<string, int> stringDict = new()
        {
            ["one"] = 1,
            ["two"] = 2,
            ["three"] = 3,
        };
        public Dictionary<int, string> intDict = new()
        {
            [1] = "first",
            [2] = "second",
            [3] = "third",
        };
        public Dictionary<string, double> doubleValueDict = new()
        {
            ["pi"] = 3.14159,
            ["e"] = 2.71828,
        };
        public Dictionary<string, int> emptyDict = new();
        public Dictionary<string, int> nullDict = null;
    }

    class CustomIndexerModule : PartModule
    {
        private readonly string[] data = ["zero", "one", "two"];

        public string this[int index]
        {
            get => index >= 0 && index < data.Length ? data[index] : null;
        }

        public int this[string key]
        {
            get =>
                key switch
                {
                    "first" => 100,
                    "second" => 200,
                    _ => -1,
                };
        }
    }

    class MultiIndexerModule : PartModule
    {
        public int this[int index] => index * 10;
        public double this[double index] => index * 1.5;
        public string this[string key] => $"key:{key}";

        public int IntIndex = 5;
    }

    class ThrowingIndexerModule : PartModule
    {
        public string this[int index]
        {
            get =>
                index switch
                {
                    0 => "valid",
                    1 => throw new IndexOutOfRangeException("Test exception"),
                    2 => throw new KeyNotFoundException("Test key not found"),
                    _ => throw new InvalidOperationException("Other exception"),
                };
        }
    }

    class FloatCurveModule : PartModule
    {
        public FloatCurve curve = new();

        public FloatCurveModule()
        {
            curve.Add(0, 0);
            curve.Add(1, 10);
            curve.Add(2, 20);
            curve.Add(3, 30);
        }
    }

    class NestedIndexModule : PartModule
    {
        public List<int[]> nestedArrays = new()
        {
            new[] { 1, 2, 3 },
            new[] { 4, 5, 6 },
            new[] { 7, 8, 9 },
        };

        public Dictionary<string, List<int>> dictOfLists = new()
        {
            ["first"] = new List<int> { 10, 20, 30 },
            ["second"] = new List<int> { 40, 50, 60 },
        };

        public int[][] jaggedArray = { new[] { 1, 2 }, new[] { 3, 4, 5 }, new[] { 6, 7, 8, 9 } };
    }

    class NumericConversionModule : PartModule
    {
        public int[] array = [100, 200, 300];
        public List<string> list = new() { "a", "b", "c" };
        public Dictionary<int, double> dict = new()
        {
            [1] = 1.1,
            [2] = 2.2,
            [3] = 3.3,
        };

        public float FloatZero = 0.0f;
        public uint IntTwo = 2u;
    }

#pragma warning disable CS0414
    class StringIndexFallbackModule : PartModule
    {
        public string PublicField = "FieldValue";
        public int NumericField = 42;
        private string PrivateField = "Private";

        public Dictionary<string, int> dict = new() { ["key1"] = 10, ["key2"] = 20 };
    }
#pragma warning restore CS0414

    class TypeCoercionModule : PartModule
    {
        public object[] mixedArray = [42, 3.14, "text", true];
        public List<object> mixedList = new() { 100, 2.5, "string" };
    }

    #endregion

    #region Array Indexing Tests

    [TestMethod]
    public void TestIntArrayIndexing()
    {
        var module = new ArrayModule();
        var expr = FieldExpression<int>.Compile("%intArray[0]", new(), typeof(ArrayModule));
        Assert.AreEqual(1, expr.Evaluate(module));
    }

    [TestMethod]
    public void TestIntArrayMiddleIndex()
    {
        var module = new ArrayModule();
        var expr = FieldExpression<int>.Compile("%intArray[2]", new(), typeof(ArrayModule));
        Assert.AreEqual(3, expr.Evaluate(module));
    }

    [TestMethod]
    public void TestIntArrayLastIndex()
    {
        var module = new ArrayModule();
        var expr = FieldExpression<int>.Compile("%intArray[4]", new(), typeof(ArrayModule));
        Assert.AreEqual(5, expr.Evaluate(module));
    }

    [TestMethod]
    public void TestDoubleArrayIndexing()
    {
        var module = new ArrayModule();
        var expr = FieldExpression<double>.Compile("%doubleArray[1]", new(), typeof(ArrayModule));
        Assert.AreEqual(2.2, expr.Evaluate(module));
    }

    [TestMethod]
    public void TestStringArrayIndexing()
    {
        var module = new ArrayModule();
        var expr = FieldExpression<string>.Compile("%stringArray[2]", new(), typeof(ArrayModule));
        Assert.AreEqual("third", expr.Evaluate(module));
    }

    [TestMethod]
    public void TestArrayIndexOutOfBounds()
    {
        var module = new ArrayModule();
        var expr = FieldExpression<int>.Compile("%intArray[10] ?? -1", new(), typeof(ArrayModule));
        Assert.AreEqual(-1, expr.Evaluate(module));
    }

    [TestMethod]
    public void TestArrayNegativeIndex()
    {
        var module = new ArrayModule();
        var expr = FieldExpression<int>.Compile(
            "%intArray[-1] ?? -999",
            new(),
            typeof(ArrayModule)
        );
        Assert.AreEqual(-999, expr.Evaluate(module));
    }

    [TestMethod]
    public void TestEmptyArray()
    {
        var module = new ArrayModule();
        var expr = FieldExpression<int>.Compile("%emptyArray[0] ?? 0", new(), typeof(ArrayModule));
        Assert.AreEqual(0, expr.Evaluate(module));
    }

    [TestMethod]
    public void TestNullArray()
    {
        var module = new ArrayModule();
        var expr = FieldExpression<int>.Compile("%nullArray[0] ?? 5", new(), typeof(ArrayModule));
        Assert.AreEqual(5, expr.Evaluate(module));
    }

    [TestMethod]
    public void TestObjectArray()
    {
        var module = new ArrayModule();
        var expr = FieldExpression<int>.Compile("%objectArray[0]", new(), typeof(ArrayModule));
        Assert.AreEqual(42, expr.Evaluate(module));
    }

    #endregion

    #region List Indexing Tests

    [TestMethod]
    public void TestListIndexing()
    {
        var module = new ListModule();
        var expr = FieldExpression<int>.Compile("%intList[0]", new(), typeof(ListModule));
        Assert.AreEqual(10, expr.Evaluate(module));
    }

    [TestMethod]
    public void TestListMiddleIndex()
    {
        var module = new ListModule();
        var expr = FieldExpression<int>.Compile("%intList[2]", new(), typeof(ListModule));
        Assert.AreEqual(30, expr.Evaluate(module));
    }

    [TestMethod]
    public void TestListLastIndex()
    {
        var module = new ListModule();
        var expr = FieldExpression<int>.Compile("%intList[3]", new(), typeof(ListModule));
        Assert.AreEqual(40, expr.Evaluate(module));
    }

    [TestMethod]
    public void TestStringListIndexing()
    {
        var module = new ListModule();
        var expr = FieldExpression<string>.Compile("%stringList[1]", new(), typeof(ListModule));
        Assert.AreEqual("beta", expr.Evaluate(module));
    }

    [TestMethod]
    public void TestDoubleListIndexing()
    {
        var module = new ListModule();
        var expr = FieldExpression<double>.Compile("%doubleList[2]", new(), typeof(ListModule));
        Assert.AreEqual(3.5, expr.Evaluate(module));
    }

    [TestMethod]
    public void TestListIndexOutOfBounds()
    {
        var module = new ListModule();
        var expr = FieldExpression<int>.Compile("%intList[10] ?? -1", new(), typeof(ListModule));
        Assert.AreEqual(-1, expr.Evaluate(module));
    }

    [TestMethod]
    public void TestListNegativeIndex()
    {
        var module = new ListModule();
        var expr = FieldExpression<int>.Compile("%intList[-1] ?? -999", new(), typeof(ListModule));
        Assert.AreEqual(-999, expr.Evaluate(module));
    }

    [TestMethod]
    public void TestEmptyList()
    {
        var module = new ListModule();
        var expr = FieldExpression<int>.Compile("%emptyList[0] ?? 0", new(), typeof(ListModule));
        Assert.AreEqual(0, expr.Evaluate(module));
    }

    [TestMethod]
    public void TestNullList()
    {
        var module = new ListModule();
        var expr = FieldExpression<int>.Compile("%nullList[0] ?? 5", new(), typeof(ListModule));
        Assert.AreEqual(5, expr.Evaluate(module));
    }

    #endregion

    #region Dictionary Indexing Tests

    [TestMethod]
    public void TestStringDictionaryIndexing()
    {
        var module = new DictionaryModule();
        var expr = FieldExpression<int>.Compile(
            "%stringDict[\"one\"]",
            new(),
            typeof(DictionaryModule)
        );
        Assert.AreEqual(1, expr.Evaluate(module));
    }

    [TestMethod]
    public void TestStringDictionaryMultipleKeys()
    {
        var module = new DictionaryModule();
        var expr = FieldExpression<int>.Compile(
            "%stringDict[\"three\"]",
            new(),
            typeof(DictionaryModule)
        );
        Assert.AreEqual(3, expr.Evaluate(module));
    }

    [TestMethod]
    public void TestIntDictionaryIndexing()
    {
        var module = new DictionaryModule();
        var expr = FieldExpression<string>.Compile("%intDict[2]", new(), typeof(DictionaryModule));
        Assert.AreEqual("second", expr.Evaluate(module));
    }

    [TestMethod]
    public void TestDoubleDictionaryIndexing()
    {
        var module = new DictionaryModule();
        var expr = FieldExpression<double>.Compile(
            "%doubleValueDict[\"pi\"]",
            new(),
            typeof(DictionaryModule)
        );
        Assert.AreEqual(3.14159, expr.Evaluate(module));
    }

    [TestMethod]
    public void TestDictionaryMissingKey()
    {
        var module = new DictionaryModule();
        var expr = FieldExpression<int>.Compile(
            "%stringDict[\"missing\"] ?? -1",
            new(),
            typeof(DictionaryModule)
        );
        Assert.AreEqual(-1, expr.Evaluate(module));
    }

    [TestMethod]
    public void TestEmptyDictionary()
    {
        var module = new DictionaryModule();
        var expr = FieldExpression<int>.Compile(
            "%emptyDict[\"key\"] ?? 0",
            new(),
            typeof(DictionaryModule)
        );
        Assert.AreEqual(0, expr.Evaluate(module));
    }

    [TestMethod]
    public void TestNullDictionary()
    {
        var module = new DictionaryModule();
        var expr = FieldExpression<int>.Compile(
            "%nullDict[\"key\"] ?? 5",
            new(),
            typeof(DictionaryModule)
        );
        Assert.AreEqual(5, expr.Evaluate(module));
    }

    #endregion

    #region Custom Indexer Tests

    [TestMethod]
    public void TestCustomIntIndexer()
    {
        var module = new CustomIndexerModule();
        var expr = FieldExpression<string>.Compile("%[0]", new(), typeof(CustomIndexerModule));
        Assert.AreEqual("zero", expr.Evaluate(module));
    }

    [TestMethod]
    public void TestCustomIntIndexerMiddle()
    {
        var module = new CustomIndexerModule();
        var expr = FieldExpression<string>.Compile("%[1]", new(), typeof(CustomIndexerModule));
        Assert.AreEqual("one", expr.Evaluate(module));
    }

    [TestMethod]
    public void TestCustomStringIndexer()
    {
        var module = new CustomIndexerModule();
        var expr = FieldExpression<int>.Compile("%[\"first\"]", new(), typeof(CustomIndexerModule));
        Assert.AreEqual(100, expr.Evaluate(module));
    }

    [TestMethod]
    public void TestCustomStringIndexerSecond()
    {
        var module = new CustomIndexerModule();
        var expr = FieldExpression<int>.Compile(
            "%[\"second\"]",
            new(),
            typeof(CustomIndexerModule)
        );
        Assert.AreEqual(200, expr.Evaluate(module));
    }

    [TestMethod]
    public void TestCustomIndexerInvalidKey()
    {
        var module = new CustomIndexerModule();
        var expr = FieldExpression<int>.Compile(
            "%[\"invalid\"]",
            new(),
            typeof(CustomIndexerModule)
        );
        Assert.AreEqual(-1, expr.Evaluate(module));
    }

    [TestMethod]
    public void TestCustomIndexerOutOfBounds()
    {
        var module = new CustomIndexerModule();
        var expr = FieldExpression<string>.Compile(
            "%[10] ?? \"default\"",
            new(),
            typeof(CustomIndexerModule)
        );
        Assert.AreEqual("default", expr.Evaluate(module));
    }

    [TestMethod]
    public void TestMultiIndexerInt()
    {
        var module = new MultiIndexerModule();
        var expr = FieldExpression<int>.Compile("%[%IntIndex]", new(), typeof(MultiIndexerModule));
        Assert.AreEqual(50, expr.Evaluate(module));
    }

    [TestMethod]
    public void TestMultiIndexerDouble()
    {
        var module = new MultiIndexerModule();
        var expr = FieldExpression<double>.Compile("%[4.0]", new(), typeof(MultiIndexerModule));
        Assert.AreEqual(6.0, expr.Evaluate(module));
    }

    [TestMethod]
    public void TestMultiIndexerString()
    {
        var module = new MultiIndexerModule();
        var expr = FieldExpression<string>.Compile(
            "%[\"test\"]",
            new(),
            typeof(MultiIndexerModule)
        );
        Assert.AreEqual("key:test", expr.Evaluate(module));
    }

    #endregion

    #region Numeric Type Conversion Tests

    [TestMethod]
    public void TestDoubleToIntConversion()
    {
        var module = new NumericConversionModule();
        var expr = FieldExpression<int>.Compile(
            "%array[1.0]",
            new(),
            typeof(NumericConversionModule)
        );
        Assert.AreEqual(200, expr.Evaluate(module));
    }

    [TestMethod]
    public void TestDoubleToIntConversionTruncation()
    {
        var module = new NumericConversionModule();
        var expr = FieldExpression<int>.Compile(
            "%array[1.7]",
            new(),
            typeof(NumericConversionModule)
        );
        Assert.AreEqual(200, expr.Evaluate(module));
    }

    [TestMethod]
    public void TestDoubleToIntListIndex()
    {
        var module = new NumericConversionModule();
        var expr = FieldExpression<string>.Compile(
            "%list[2.0]",
            new(),
            typeof(NumericConversionModule)
        );
        Assert.AreEqual("c", expr.Evaluate(module));
    }

    [TestMethod]
    public void TestDoubleToIntDictKey()
    {
        var module = new NumericConversionModule();
        var expr = FieldExpression<double>.Compile(
            "%dict[2.0]",
            new(),
            typeof(NumericConversionModule)
        );
        Assert.AreEqual(2.2, expr.Evaluate(module));
    }

    [TestMethod]
    public void TestFloatToIntConversion()
    {
        var module = new NumericConversionModule();
        var expr = FieldExpression<int>.Compile(
            "%array[%FloatZero]",
            new(),
            typeof(NumericConversionModule)
        );
        Assert.AreEqual(100, expr.Evaluate(module));
    }

    [TestMethod]
    public void TestUIntToIntConversion()
    {
        var module = new NumericConversionModule();
        var expr = FieldExpression<int>.Compile(
            "%array[%IntTwo]",
            new(),
            typeof(NumericConversionModule)
        );
        Assert.AreEqual(300, expr.Evaluate(module));
    }

    #endregion

    #region String Index Fallback Tests

    [TestMethod]
    public void TestStringIndexFallbackToField()
    {
        var module = new StringIndexFallbackModule();
        var expr = FieldExpression<string>.Compile(
            "%[\"PublicField\"]",
            new(),
            typeof(StringIndexFallbackModule)
        );
        Assert.AreEqual("FieldValue", expr.Evaluate(module));
    }

    [TestMethod]
    public void TestStringIndexFallbackNumericField()
    {
        var module = new StringIndexFallbackModule();
        var expr = FieldExpression<int>.Compile(
            "%[\"NumericField\"]",
            new(),
            typeof(StringIndexFallbackModule)
        );
        Assert.AreEqual(42, expr.Evaluate(module));
    }

    [TestMethod]
    public void TestStringIndexPrefersIndexer()
    {
        var module = new StringIndexFallbackModule();
        var expr = FieldExpression<int>.Compile(
            "%dict[\"key1\"]",
            new(),
            typeof(StringIndexFallbackModule)
        );
        Assert.AreEqual(10, expr.Evaluate(module));
    }

    [TestMethod]
    public void TestStringIndexFallbackNonExistent()
    {
        var module = new StringIndexFallbackModule();
        var expr = FieldExpression<string>.Compile(
            "%[\"NonExistent\"] ?? \"default\"",
            new(),
            typeof(StringIndexFallbackModule)
        );
        Assert.AreEqual("default", expr.Evaluate(module));
    }

    #endregion

    #region Null Propagation Tests

    [TestMethod]
    public void TestNullObjectIndex()
    {
        var module = new ArrayModule();
        var expr = FieldExpression<int>.Compile("%nullArray[0] ?? 99", new(), typeof(ArrayModule));
        Assert.AreEqual(99, expr.Evaluate(module));
    }

    [TestMethod]
    public void TestNullIndexOnValidObject()
    {
        var module = new ArrayModule();
        // This tests what happens when the index expression evaluates to null
        var expr = FieldExpression<int>.Compile(
            "%intArray[nullArray[0]] ?? 88",
            new(),
            typeof(ArrayModule)
        );
        Assert.AreEqual(88, expr.Evaluate(module));
    }

    [TestMethod]
    public void TestNullDictionaryAccess()
    {
        var module = new DictionaryModule();
        var expr = FieldExpression<int>.Compile(
            "%nullDict[\"key\"] ?? 77",
            new(),
            typeof(DictionaryModule)
        );
        Assert.AreEqual(77, expr.Evaluate(module));
    }

    [TestMethod]
    public void TestNullListAccess()
    {
        var module = new ListModule();
        var expr = FieldExpression<int>.Compile("%nullList[0] ?? 66", new(), typeof(ListModule));
        Assert.AreEqual(66, expr.Evaluate(module));
    }

    #endregion

    #region Nested Index Access Tests

    [TestMethod]
    public void TestNestedArrayAccess()
    {
        var module = new NestedIndexModule();
        var expr = FieldExpression<int>.Compile(
            "%nestedArrays[0][1]",
            new(),
            typeof(NestedIndexModule)
        );
        Assert.AreEqual(2, expr.Evaluate(module));
    }

    [TestMethod]
    public void TestNestedArrayAccessDeep()
    {
        var module = new NestedIndexModule();
        var expr = FieldExpression<int>.Compile(
            "%nestedArrays[2][2]",
            new(),
            typeof(NestedIndexModule)
        );
        Assert.AreEqual(9, expr.Evaluate(module));
    }

    [TestMethod]
    public void TestDictOfListsAccess()
    {
        var module = new NestedIndexModule();
        var expr = FieldExpression<int>.Compile(
            "%dictOfLists[\"first\"][1]",
            new(),
            typeof(NestedIndexModule)
        );
        Assert.AreEqual(20, expr.Evaluate(module));
    }

    [TestMethod]
    public void TestDictOfListsSecondKey()
    {
        var module = new NestedIndexModule();
        var expr = FieldExpression<int>.Compile(
            "%dictOfLists[\"second\"][2]",
            new(),
            typeof(NestedIndexModule)
        );
        Assert.AreEqual(60, expr.Evaluate(module));
    }

    [TestMethod]
    public void TestJaggedArrayAccess()
    {
        var module = new NestedIndexModule();
        var expr = FieldExpression<int>.Compile(
            "%jaggedArray[1][2]",
            new(),
            typeof(NestedIndexModule)
        );
        Assert.AreEqual(5, expr.Evaluate(module));
    }

    [TestMethod]
    public void TestNestedAccessOutOfBounds()
    {
        var module = new NestedIndexModule();
        var expr = FieldExpression<int>.Compile(
            "%nestedArrays[10][0] ?? -1",
            new(),
            typeof(NestedIndexModule)
        );
        Assert.AreEqual(-1, expr.Evaluate(module));
    }

    [TestMethod]
    public void TestNestedAccessInnerOutOfBounds()
    {
        var module = new NestedIndexModule();
        var expr = FieldExpression<int>.Compile(
            "%nestedArrays[0][10] ?? -1",
            new(),
            typeof(NestedIndexModule)
        );
        Assert.AreEqual(-1, expr.Evaluate(module));
    }

    #endregion

    #region Exception Handling Tests

    [TestMethod]
    public void TestIndexOutOfRangeException()
    {
        var module = new ThrowingIndexerModule();
        var expr = FieldExpression<string>.Compile(
            "%[1] ?? \"caught\"",
            new(),
            typeof(ThrowingIndexerModule)
        );
        Assert.AreEqual("caught", expr.Evaluate(module));
    }

    [TestMethod]
    public void TestKeyNotFoundException()
    {
        var module = new ThrowingIndexerModule();
        var expr = FieldExpression<string>.Compile(
            "%[2] ?? \"caught\"",
            new(),
            typeof(ThrowingIndexerModule)
        );
        Assert.AreEqual("caught", expr.Evaluate(module));
    }

    [TestMethod]
    public void TestOtherExceptionLogged()
    {
        var module = new ThrowingIndexerModule();
        var expr = FieldExpression<string>.Compile(
            "%[3] ?? \"caught\"",
            new(),
            typeof(ThrowingIndexerModule)
        );
        // Other exceptions are logged but still return null
        Assert.AreEqual("caught", expr.Evaluate(module));
    }

    [TestMethod]
    public void TestValidIndexAfterException()
    {
        var module = new ThrowingIndexerModule();
        var expr = FieldExpression<string>.Compile("%[0]", new(), typeof(ThrowingIndexerModule));
        Assert.AreEqual("valid", expr.Evaluate(module));
    }

    #endregion

    #region Type Coercion Tests

    [TestMethod]
    public void TestIntFromObjectArray()
    {
        var module = new TypeCoercionModule();
        var expr = FieldExpression<int>.Compile(
            "%mixedArray[0]",
            new(),
            typeof(TypeCoercionModule)
        );
        Assert.AreEqual(42, expr.Evaluate(module));
    }

    [TestMethod]
    public void TestDoubleFromObjectArray()
    {
        var module = new TypeCoercionModule();
        var expr = FieldExpression<double>.Compile(
            "%mixedArray[1]",
            new(),
            typeof(TypeCoercionModule)
        );
        Assert.AreEqual(3.14, expr.Evaluate(module));
    }

    [TestMethod]
    public void TestStringFromObjectArray()
    {
        var module = new TypeCoercionModule();
        var expr = FieldExpression<string>.Compile(
            "%mixedArray[2]",
            new(),
            typeof(TypeCoercionModule)
        );
        Assert.AreEqual("text", expr.Evaluate(module));
    }

    [TestMethod]
    public void TestBoolFromObjectArray()
    {
        var module = new TypeCoercionModule();
        var expr = FieldExpression<bool>.Compile(
            "%mixedArray[3]",
            new(),
            typeof(TypeCoercionModule)
        );
        Assert.AreEqual(true, expr.Evaluate(module));
    }

    [TestMethod]
    public void TestObjectFromMixedList()
    {
        var module = new TypeCoercionModule();
        var expr = FieldExpression<double>.Compile(
            "%mixedList[1]",
            new(),
            typeof(TypeCoercionModule)
        );
        Assert.AreEqual(2.5, expr.Evaluate(module));
    }

    [TestMethod]
    public void TestIntFromMixedList()
    {
        var module = new TypeCoercionModule();
        var expr = FieldExpression<int>.Compile("%mixedList[0]", new(), typeof(TypeCoercionModule));
        Assert.AreEqual(100, expr.Evaluate(module));
    }

    #endregion

    #region Edge Cases

    [TestMethod]
    public void TestZeroIndex()
    {
        var module = new ArrayModule();
        var expr = FieldExpression<int>.Compile("%intArray[0]", new(), typeof(ArrayModule));
        Assert.AreEqual(1, expr.Evaluate(module));
    }

    [TestMethod]
    public void TestEmptyStringKey()
    {
        var module = new DictionaryModule();
        var expr = FieldExpression<int>.Compile(
            "%stringDict[\"\"] ?? -1",
            new(),
            typeof(DictionaryModule)
        );
        Assert.AreEqual(-1, expr.Evaluate(module));
    }

    [TestMethod]
    public void TestComplexExpression()
    {
        var module = new ArrayModule();
        var expr = FieldExpression<int>.Compile(
            "%intArray[1] + %intArray[2]",
            new(),
            typeof(ArrayModule)
        );
        Assert.AreEqual(5, expr.Evaluate(module));
    }

    [TestMethod]
    public void TestIndexWithCoalescing()
    {
        var module = new ArrayModule();
        var expr = FieldExpression<int>.Compile(
            "(%nullArray[0] ?? 10) + %intArray[0]",
            new(),
            typeof(ArrayModule)
        );
        Assert.AreEqual(11, expr.Evaluate(module));
    }

    [TestMethod]
    public void TestChainedCoalescing()
    {
        var module = new ArrayModule();
        var expr = FieldExpression<int>.Compile(
            "%nullArray[0] ?? %emptyArray[0] ?? %intArray[0]",
            new(),
            typeof(ArrayModule)
        );
        Assert.AreEqual(1, expr.Evaluate(module));
    }

    #endregion
}
