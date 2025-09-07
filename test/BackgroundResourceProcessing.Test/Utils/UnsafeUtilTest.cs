using System;
using BackgroundResourceProcessing.Utils;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace BackgroundResourceProcessing.Test.Utils;

[TestClass]
public sealed class UnsafeUtilTest
{
    private struct PrimitiveStruct
    {
        public int IntValue { get; set; }
        public float FloatValue { get; set; }
        public bool BoolValue { get; set; }
    }

    private struct StructWithReference
    {
        public int IntValue { get; set; }
        public string StringValue { get; set; }
    }

    private struct NestedStructWithoutReferences
    {
        public PrimitiveStruct Nested { get; set; }
        public int Value { get; set; }
    }

    private struct NestedStructWithReferences
    {
        public StructWithReference Nested { get; set; }
        public int Value { get; set; }
    }

    private struct StructWithNullableValueType
    {
        public int? NullableInt { get; set; }
        public bool? NullableBool { get; set; }
    }

    private enum SimpleEnum
    {
        Value1,
        Value2,
    }

    private enum EnumWithValues : int
    {
        First = 1,
        Second = 2,
    }

    private class ReferenceClass
    {
        public int Value { get; set; }
    }

    [TestMethod]
    public void ContainsReferences_PrimitiveTypes_ReturnsFalse()
    {
        Assert.IsFalse(UnsafeUtil.ContainsReferences<int>());
        Assert.IsFalse(UnsafeUtil.ContainsReferences<float>());
        Assert.IsFalse(UnsafeUtil.ContainsReferences<double>());
        Assert.IsFalse(UnsafeUtil.ContainsReferences<byte>());
        Assert.IsFalse(UnsafeUtil.ContainsReferences<bool>());
        Assert.IsFalse(UnsafeUtil.ContainsReferences<char>());
        Assert.IsFalse(UnsafeUtil.ContainsReferences<long>());
        Assert.IsFalse(UnsafeUtil.ContainsReferences<short>());
        Assert.IsFalse(UnsafeUtil.ContainsReferences<decimal>());
    }

    [TestMethod]
    public void ContainsReferences_ReferenceTypes_ReturnsTrue()
    {
        Assert.IsTrue(UnsafeUtil.ContainsReferences<string>());
        Assert.IsTrue(UnsafeUtil.ContainsReferences<object>());
        Assert.IsTrue(UnsafeUtil.ContainsReferences<ReferenceClass>());
        Assert.IsTrue(UnsafeUtil.ContainsReferences<int[]>());
        Assert.IsTrue(UnsafeUtil.ContainsReferences<List<int>>());
    }

    [TestMethod]
    public void ContainsReferences_EnumTypes_ReturnsFalse()
    {
        Assert.IsFalse(UnsafeUtil.ContainsReferences<SimpleEnum>());
        Assert.IsFalse(UnsafeUtil.ContainsReferences<EnumWithValues>());
        Assert.IsFalse(UnsafeUtil.ContainsReferences<DayOfWeek>());
    }

    [TestMethod]
    public void ContainsReferences_PrimitiveOnlyStruct_ReturnsFalse()
    {
        Assert.IsFalse(UnsafeUtil.ContainsReferences<PrimitiveStruct>());
    }

    [TestMethod]
    public void ContainsReferences_StructWithReferenceField_ReturnsTrue()
    {
        Assert.IsTrue(UnsafeUtil.ContainsReferences<StructWithReference>());
    }

    [TestMethod]
    public void ContainsReferences_NestedStructWithoutReferences_ReturnsFalse()
    {
        Assert.IsFalse(UnsafeUtil.ContainsReferences<NestedStructWithoutReferences>());
    }

    [TestMethod]
    public void ContainsReferences_NestedStructWithReferences_ReturnsTrue()
    {
        Assert.IsTrue(UnsafeUtil.ContainsReferences<NestedStructWithReferences>());
    }

    [TestMethod]
    public void ContainsReferences_StructWithNullableValueTypes_ReturnsTrue()
    {
        Assert.IsFalse(UnsafeUtil.ContainsReferences<StructWithNullableValueType>());
    }

    [TestMethod]
    public void ContainsReferences_NullableValueTypes_ReturnsTrue()
    {
        Assert.IsFalse(UnsafeUtil.ContainsReferences<int?>());
        Assert.IsFalse(UnsafeUtil.ContainsReferences<bool?>());
        Assert.IsFalse(UnsafeUtil.ContainsReferences<float?>());
        Assert.IsFalse(UnsafeUtil.ContainsReferences<DateTime?>());
    }

    [TestMethod]
    public void ContainsReferences_SystemValueTypes_ReturnsFalse()
    {
        Assert.IsFalse(UnsafeUtil.ContainsReferences<DateTime>());
        Assert.IsFalse(UnsafeUtil.ContainsReferences<TimeSpan>());
        Assert.IsFalse(UnsafeUtil.ContainsReferences<Guid>());
    }

    [TestMethod]
    public void ContainsReferences_ConsistentResults_SameTypeMultipleCalls()
    {
        // Ensure that multiple calls return the same result (testing caching behavior)
        var result1 = UnsafeUtil.ContainsReferences<PrimitiveStruct>();
        var result2 = UnsafeUtil.ContainsReferences<PrimitiveStruct>();
        var result3 = UnsafeUtil.ContainsReferences<PrimitiveStruct>();

        Assert.AreEqual(result1, result2);
        Assert.AreEqual(result2, result3);
        Assert.IsFalse(result1);
    }

    [TestMethod]
    public void ContainsReferences_ConsistentResults_ReferenceTypeMultipleCalls()
    {
        // Ensure that multiple calls return the same result for reference types
        var result1 = UnsafeUtil.ContainsReferences<StructWithReference>();
        var result2 = UnsafeUtil.ContainsReferences<StructWithReference>();
        var result3 = UnsafeUtil.ContainsReferences<StructWithReference>();

        Assert.AreEqual(result1, result2);
        Assert.AreEqual(result2, result3);
        Assert.IsTrue(result1);
    }
}
