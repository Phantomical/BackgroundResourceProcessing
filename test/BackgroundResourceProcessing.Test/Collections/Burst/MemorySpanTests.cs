using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BackgroundResourceProcessing.Collections.Burst;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace BackgroundResourceProcessing.Test.Collections.Burst;

[TestClass]
public sealed class MemorySpanTests
{
    [TestCleanup]
    public void Cleanup()
    {
        TestAllocator.Cleanup();
    }

    #region Basic Foreach Iteration Tests

    [TestMethod]
    public void ForeachIteration_EmptySpan_DoesNotExecuteLoop()
    {
        var array = new RawArray<int>(0);
        var span = new MemorySpan<int>(array);
        int iterationCount = 0;

        foreach (var item in span)
        {
            iterationCount++;
        }

        Assert.AreEqual(0, iterationCount);
    }

    [TestMethod]
    public void ForeachIteration_SingleElement_IteratesOnce()
    {
        var array = new RawArray<int>(1);
        array[0] = 42;
        var span = new MemorySpan<int>(array);
        int iterationCount = 0;
        int capturedValue = 0;

        foreach (var item in span)
        {
            iterationCount++;
            capturedValue = item;
        }

        Assert.AreEqual(1, iterationCount);
        Assert.AreEqual(42, capturedValue);
    }

    [TestMethod]
    public void ForeachIteration_MultipleElements_IteratesInOrder()
    {
        var array = new RawArray<int>(5);
        for (int i = 0; i < 5; i++)
            array[i] = i * 10;

        var span = new MemorySpan<int>(array);
        var capturedValues = new List<int>();

        foreach (var item in span)
        {
            capturedValues.Add(item);
        }

        Assert.AreEqual(5, capturedValues.Count);
        CollectionAssert.AreEqual(new[] { 0, 10, 20, 30, 40 }, capturedValues);
    }

    [TestMethod]
    public void ForeachIteration_ByReference_AllowsModification()
    {
        var array = new RawArray<int>(3);
        array[0] = 1;
        array[1] = 2;
        array[2] = 3;
        var span = new MemorySpan<int>(array);

        foreach (ref var item in span)
        {
            item *= 2;
        }

        Assert.AreEqual(2, array[0]);
        Assert.AreEqual(4, array[1]);
        Assert.AreEqual(6, array[2]);
    }

    #endregion

    #region Manual Enumerator Tests

    [TestMethod]
    public void ManualEnumerator_EmptySpan_MoveNextReturnsFalse()
    {
        var array = new RawArray<int>(0);
        var span = new MemorySpan<int>(array);

        using var enumerator = span.GetEnumerator();
        bool result = enumerator.MoveNext();

        Assert.IsFalse(result);
    }

    [TestMethod]
    public void ManualEnumerator_SingleElement_MoveNextOnceThenFalse()
    {
        var array = new RawArray<int>(1);
        array[0] = 99;
        var span = new MemorySpan<int>(array);

        using var enumerator = span.GetEnumerator();

        bool firstMove = enumerator.MoveNext();
        Assert.IsTrue(firstMove);
        Assert.AreEqual(99, enumerator.Current);

        bool secondMove = enumerator.MoveNext();
        Assert.IsFalse(secondMove);
    }

    [TestMethod]
    public void ManualEnumerator_MultipleElements_IteratesCorrectly()
    {
        var array = new RawArray<int>(4);
        array[0] = 10;
        array[1] = 20;
        array[2] = 30;
        array[3] = 40;
        var span = new MemorySpan<int>(array);

        using var enumerator = span.GetEnumerator();
        var values = new List<int>();

        while (enumerator.MoveNext())
        {
            values.Add(enumerator.Current);
        }

        CollectionAssert.AreEqual(new[] { 10, 20, 30, 40 }, values);
    }

    [TestMethod]
    public void ManualEnumerator_CurrentByReference_AllowsModification()
    {
        var array = new RawArray<int>(2);
        array[0] = 5;
        array[1] = 15;
        var span = new MemorySpan<int>(array);

        using var enumerator = span.GetEnumerator();

        enumerator.MoveNext();
        enumerator.Current = 50;

        enumerator.MoveNext();
        enumerator.Current = 150;

        Assert.AreEqual(50, array[0]);
        Assert.AreEqual(150, array[1]);
    }

    [TestMethod]
    public void ManualEnumerator_CallingCurrentBeforeMoveNext_ReturnsInvalidReference()
    {
        var array = new RawArray<int>(1);
        array[0] = 42;
        var span = new MemorySpan<int>(array);

        using var enumerator = span.GetEnumerator();

        // Current before MoveNext should point to invalid memory (span.data - 1)
        // This is implementation-specific behavior - we're testing that it doesn't crash
        var _ = enumerator.Current; // Should not throw, but value is undefined
    }

    #endregion

    #region IEnumerable Interface Tests

    [TestMethod]
    public void IEnumerableInterface_EmptySpan_WorksCorrectly()
    {
        var array = new RawArray<int>(0);
        var span = new MemorySpan<int>(array);
        IEnumerable<int> enumerable = span;

        var result = enumerable.ToList();

        Assert.AreEqual(0, result.Count);
    }

    [TestMethod]
    public void IEnumerableInterface_WithElements_WorksCorrectly()
    {
        var array = new RawArray<int>(3);
        array[0] = 100;
        array[1] = 200;
        array[2] = 300;
        var span = new MemorySpan<int>(array);
        IEnumerable<int> enumerable = span;

        var result = enumerable.ToArray();

        CollectionAssert.AreEqual(new[] { 100, 200, 300 }, result);
    }

    [TestMethod]
    public void IEnumerableInterface_NonGeneric_WorksCorrectly()
    {
        var array = new RawArray<int>(2);
        array[0] = 11;
        array[1] = 22;
        var span = new MemorySpan<int>(array);
        IEnumerable nonGenericEnumerable = span;

        var result = new List<object>();
        foreach (var item in nonGenericEnumerable)
        {
            result.Add(item);
        }

        Assert.AreEqual(2, result.Count);
        Assert.AreEqual(11, result[0]);
        Assert.AreEqual(22, result[1]);
    }

    #endregion

    #region Sliced Span Iteration Tests

    [TestMethod]
    public void SlicedSpan_StartIndex_IteratesFromCorrectPosition()
    {
        var array = new RawArray<int>(5);
        for (int i = 0; i < 5; i++)
            array[i] = i + 1; // 1, 2, 3, 4, 5

        var span = new MemorySpan<int>(array);
        var sliced = span.Slice(2); // Should contain 3, 4, 5
        var result = new List<int>();

        foreach (var item in sliced)
        {
            result.Add(item);
        }

        CollectionAssert.AreEqual(new[] { 3, 4, 5 }, result);
    }

    [TestMethod]
    public void SlicedSpan_StartAndLength_IteratesCorrectSubset()
    {
        var array = new RawArray<int>(6);
        for (int i = 0; i < 6; i++)
            array[i] = (i + 1) * 10; // 10, 20, 30, 40, 50, 60

        var span = new MemorySpan<int>(array);
        var sliced = span.Slice(1, 3); // Should contain 20, 30, 40
        var result = new List<int>();

        foreach (var item in sliced)
        {
            result.Add(item);
        }

        CollectionAssert.AreEqual(new[] { 20, 30, 40 }, result);
    }

    [TestMethod]
    public void SlicedSpan_EmptySlice_DoesNotIterate()
    {
        var array = new RawArray<int>(3);
        array[0] = 1;
        array[1] = 2;
        array[2] = 3;
        var span = new MemorySpan<int>(array);
        var emptySlice = span.Slice(1, 0); // Empty slice
        int iterationCount = 0;

        foreach (var item in emptySlice)
        {
            iterationCount++;
        }

        Assert.AreEqual(0, iterationCount);
    }

    [TestMethod]
    public void SlicedSpan_SingleElementSlice_IteratesOnce()
    {
        var array = new RawArray<int>(5);
        for (int i = 0; i < 5; i++)
            array[i] = i * 5;

        var span = new MemorySpan<int>(array);
        var singleSlice = span.Slice(3, 1); // Should contain only element at index 3 (15)
        var result = new List<int>();

        foreach (var item in singleSlice)
        {
            result.Add(item);
        }

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual(15, result[0]);
    }

    #endregion

    #region Enumerator Edge Cases and Error Conditions

    [TestMethod]
    public void EnumeratorReset_ThrowsNotSupportedException()
    {
        var array = new RawArray<int>(1);
        var span = new MemorySpan<int>(array);

        IEnumerator<int> enumerator = span.GetEnumerator();

        Assert.ThrowsException<NotSupportedException>(() => enumerator.Reset());
    }

    [TestMethod]
    public void EnumeratorDispose_DoesNotThrow()
    {
        var array = new RawArray<int>(2);
        var span = new MemorySpan<int>(array);

        var enumerator = span.GetEnumerator();
        enumerator.MoveNext();

        // Should not throw
        enumerator.Dispose();
    }

    [TestMethod]
    public void MultipleEnumerators_WorkIndependently()
    {
        var array = new RawArray<int>(3);
        array[0] = 10;
        array[1] = 20;
        array[2] = 30;
        var span = new MemorySpan<int>(array);

        using var enumerator1 = span.GetEnumerator();
        using var enumerator2 = span.GetEnumerator();

        // Advance first enumerator
        enumerator1.MoveNext();
        Assert.AreEqual(10, enumerator1.Current);

        // Second enumerator should start from beginning
        enumerator2.MoveNext();
        Assert.AreEqual(10, enumerator2.Current);

        // Continue with first enumerator
        enumerator1.MoveNext();
        Assert.AreEqual(20, enumerator1.Current);

        // Second enumerator should still be at first position
        Assert.AreEqual(10, enumerator2.Current);
    }

    [TestMethod]
    public void EnumeratorAfterSpanSlice_IteratesCorrectly()
    {
        var array = new RawArray<int>(4);
        for (int i = 0; i < 4; i++)
            array[i] = i + 1;

        var originalSpan = new MemorySpan<int>(array);
        var slicedSpan = originalSpan.Slice(1, 2); // Contains 2, 3

        using var enumerator = slicedSpan.GetEnumerator();
        var values = new List<int>();

        while (enumerator.MoveNext())
        {
            values.Add(enumerator.Current);
        }

        CollectionAssert.AreEqual(new[] { 2, 3 }, values);
    }

    #endregion

    #region Different Data Types

    [TestMethod]
    public void Iteration_ByteType_WorksCorrectly()
    {
        var array = new RawArray<byte>(4);
        array[0] = 0xFF;
        array[1] = 0x80;
        array[2] = 0x40;
        array[3] = 0x01;
        var span = new MemorySpan<byte>(array);
        var result = new List<byte>();

        foreach (var item in span)
        {
            result.Add(item);
        }

        CollectionAssert.AreEqual(new byte[] { 0xFF, 0x80, 0x40, 0x01 }, result);
    }

    [TestMethod]
    public void Iteration_CustomStruct_WorksCorrectly()
    {
        var array = new RawArray<TestStruct>(3);
        array[0] = new TestStruct { Value = 10, Flag = true };
        array[1] = new TestStruct { Value = 20, Flag = false };
        array[2] = new TestStruct { Value = 30, Flag = true };
        var span = new MemorySpan<TestStruct>(array);
        var result = new List<TestStruct>();

        foreach (var item in span)
        {
            result.Add(item);
        }

        Assert.AreEqual(3, result.Count);
        Assert.AreEqual(10, result[0].Value);
        Assert.IsTrue(result[0].Flag);
        Assert.AreEqual(20, result[1].Value);
        Assert.IsFalse(result[1].Flag);
        Assert.AreEqual(30, result[2].Value);
        Assert.IsTrue(result[2].Flag);
    }

    #endregion

    [TestMethod]
    public void CollectionExpression_WorksRight()
    {
        var array = new RawArray<int>(5)
        {
            [0] = 1,
            [1] = 2,
            [2] = 3,
            [3] = 4,
            [4] = 5,
        };

        int[] collection = [.. array];

        AssertUtils.SequenceEqual(collection, [1, 2, 3, 4, 5]);
    }

    private struct TestStruct
    {
        public int Value;
        public bool Flag;
    }
}
