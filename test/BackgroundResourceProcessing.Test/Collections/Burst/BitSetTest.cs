using System;
using System.Collections.Generic;
using System.Linq;
using BackgroundResourceProcessing.Collections.Burst;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Unity.Collections;

namespace BackgroundResourceProcessing.Test.Collections.Burst;

[TestClass]
public sealed class BitSetTest
{
    [TestCleanup]
    public void Cleanup()
    {
        TestAllocator.Cleanup();
    }

    [TestMethod]
    public void Constructor_Default_CreatesEmptyBitSet()
    {
        var bitSet = new BitSet();

        Assert.AreEqual(0, bitSet.Capacity);
    }

    [TestMethod]
    public void Constructor_WithCapacity_CreatesCorrectCapacity()
    {
        const int capacity = 128;
        var bitSet = new BitSet(capacity, AllocatorHandle.Temp);

        Assert.IsTrue(bitSet.Capacity >= capacity);
    }

    [TestMethod]
    public void Constructor_WithSpan_CreatesFromSpan()
    {
        var span = new ulong[] { 0xFF, 0x00 };
        var bitSet = new BitSet(span, AllocatorHandle.Temp);

        Assert.AreEqual(128, bitSet.Capacity);
        Assert.IsTrue(bitSet[0]);
        Assert.IsTrue(bitSet[7]);
        Assert.IsFalse(bitSet[64]);
    }

    [TestMethod]
    public void Indexer_ValidIndex_SetsAndGetsCorrectly()
    {
        var bitSet = new BitSet(64, AllocatorHandle.Temp);

        bitSet[0] = true;
        bitSet[31] = true;
        bitSet[63] = true;

        Assert.IsTrue(bitSet[0]);
        Assert.IsTrue(bitSet[31]);
        Assert.IsTrue(bitSet[63]);
        Assert.IsFalse(bitSet[1]);
        Assert.IsFalse(bitSet[32]);
    }

    [TestMethod]
    public void Indexer_NegativeIndex_ThrowsException()
    {
        var bitSet = new BitSet(64, AllocatorHandle.Temp);

        Assert.ThrowsException<IndexOutOfRangeException>(() => _ = bitSet[-1]);
    }

    [TestMethod]
    public void Indexer_IndexOutOfBounds_ThrowsException()
    {
        var bitSet = new BitSet(64, AllocatorHandle.Temp);

        Assert.ThrowsException<IndexOutOfRangeException>(() => _ = bitSet[64]);
    }

    [TestMethod]
    public void Indexer_SetNegativeIndex_ThrowsException()
    {
        var bitSet = new BitSet(64, AllocatorHandle.Temp);

        Assert.ThrowsException<IndexOutOfRangeException>(() => bitSet[-1] = true);
    }

    [TestMethod]
    public void Indexer_SetIndexOutOfBounds_ThrowsException()
    {
        var bitSet = new BitSet(64, AllocatorHandle.Temp);

        Assert.ThrowsException<IndexOutOfRangeException>(() => bitSet[64] = true);
    }

    [TestMethod]
    public void Add_ValidIndex_SetsBit()
    {
        var bitSet = new BitSet(64, AllocatorHandle.Temp);

        bitSet.Add(5);
        bitSet.Add(31);

        Assert.IsTrue(bitSet[5]);
        Assert.IsTrue(bitSet[31]);
        Assert.IsFalse(bitSet[0]);
    }

    [TestMethod]
    public void Remove_ValidIndex_ClearsBit()
    {
        var bitSet = new BitSet(64, AllocatorHandle.Temp);
        bitSet.Add(5);
        bitSet.Add(31);

        bitSet.Remove(5);

        Assert.IsFalse(bitSet[5]);
        Assert.IsTrue(bitSet[31]);
    }

    [TestMethod]
    public void Contains_ValidIndex_ReturnsCorrectValue()
    {
        var bitSet = new BitSet(64, AllocatorHandle.Temp);
        bitSet.Add(10);

        Assert.IsTrue(bitSet.Contains(10));
        Assert.IsFalse(bitSet.Contains(11));
        Assert.IsFalse(bitSet.Contains(-1));
        Assert.IsFalse(bitSet.Contains(64));
    }

    [TestMethod]
    public void Clear_PopulatedBitSet_ClearsAllBits()
    {
        var bitSet = new BitSet(64, AllocatorHandle.Temp);
        bitSet.Add(0);
        bitSet.Add(31);
        bitSet.Add(63);

        bitSet.Clear();

        Assert.IsFalse(bitSet[0]);
        Assert.IsFalse(bitSet[31]);
        Assert.IsFalse(bitSet[63]);
        Assert.AreEqual(0, bitSet.GetCount());
    }

    [TestMethod]
    public void Fill_EmptyBitSet_SetsAllBitsToTrue()
    {
        var bitSet = new BitSet(64, AllocatorHandle.Temp);

        bitSet.Fill(true);

        Assert.IsTrue(bitSet[0]);
        Assert.IsTrue(bitSet[31]);
        Assert.IsTrue(bitSet[63]);
        Assert.AreEqual(64, bitSet.GetCount());
    }

    [TestMethod]
    public void Fill_PopulatedBitSet_SetsAllBitsToFalse()
    {
        var bitSet = new BitSet(64, AllocatorHandle.Temp);
        bitSet.Add(10);
        bitSet.Add(20);

        bitSet.Fill(false);

        Assert.IsFalse(bitSet[10]);
        Assert.IsFalse(bitSet[20]);
        Assert.AreEqual(0, bitSet.GetCount());
    }

    [TestMethod]
    public void GetCount_EmptyBitSet_ReturnsZero()
    {
        var bitSet = new BitSet(64, AllocatorHandle.Temp);

        Assert.AreEqual(0, bitSet.GetCount());
    }

    [TestMethod]
    public void GetCount_PopulatedBitSet_ReturnsCorrectCount()
    {
        var bitSet = new BitSet(128, AllocatorHandle.Temp);
        bitSet.Add(0);
        bitSet.Add(31);
        bitSet.Add(63);
        bitSet.Add(127);

        Assert.AreEqual(4, bitSet.GetCount());
    }

    [TestMethod]
    public void ClearUpFrom_ValidIndex_ClearsBitsFromIndex()
    {
        var bitSet = new BitSet(128, AllocatorHandle.Temp);
        bitSet.Fill(true);

        bitSet.ClearUpFrom(64);

        Assert.IsTrue(bitSet[63]);
        Assert.IsFalse(bitSet[64]);
        Assert.IsFalse(bitSet[127]);
        Assert.AreEqual(64, bitSet.GetCount());
    }

    [TestMethod]
    public void ClearUpFrom_NegativeIndex_ThrowsException()
    {
        var bitSet = new BitSet(64, AllocatorHandle.Temp);

        Assert.ThrowsException<ArgumentOutOfRangeException>(() => bitSet.ClearUpFrom(-1));
    }

    [TestMethod]
    public void ClearUpTo_ValidIndex_ClearsBitsUpToIndex()
    {
        var bitSet = new BitSet(128, AllocatorHandle.Temp);
        bitSet.Fill(true);

        bitSet.ClearUpTo(64);

        Assert.IsFalse(bitSet[0]);
        Assert.IsFalse(bitSet[63]);
        Assert.IsTrue(bitSet[64]);
        Assert.IsTrue(bitSet[127]);
        Assert.AreEqual(64, bitSet.GetCount());
    }

    [TestMethod]
    public void ClearUpTo_NegativeIndex_ThrowsException()
    {
        var bitSet = new BitSet(64, AllocatorHandle.Temp);

        Assert.ThrowsException<ArgumentOutOfRangeException>(() => bitSet.ClearUpTo(-1));
    }

    [TestMethod]
    public void ClearOutsideRange_ValidRange_ClearsBitsOutsideRange()
    {
        var bitSet = new BitSet(128, AllocatorHandle.Temp);
        bitSet.Fill(true);

        bitSet.ClearOutsideRange(32, 96);

        Assert.IsFalse(bitSet[0]);
        Assert.IsFalse(bitSet[31]);
        Assert.IsTrue(bitSet[32]);
        Assert.IsTrue(bitSet[95]);
        Assert.IsFalse(bitSet[96]);
        Assert.IsFalse(bitSet[127]);
        Assert.AreEqual(64, bitSet.GetCount());
    }

    [TestMethod]
    public void ClearOutsideRange_NegativeStart_ThrowsException()
    {
        var bitSet = new BitSet(64, AllocatorHandle.Temp);

        Assert.ThrowsException<ArgumentOutOfRangeException>(() => bitSet.ClearOutsideRange(-1, 32));
    }

    [TestMethod]
    public void ClearOutsideRange_EndLessThanStart_ThrowsException()
    {
        var bitSet = new BitSet(64, AllocatorHandle.Temp);

        Assert.ThrowsException<ArgumentOutOfRangeException>(() => bitSet.ClearOutsideRange(32, 16));
    }

    [TestMethod]
    public void ClearOutsideRange_EndExceedsCapacity_ThrowsException()
    {
        var bitSet = new BitSet(64, AllocatorHandle.Temp);

        Assert.ThrowsException<ArgumentOutOfRangeException>(() => bitSet.ClearOutsideRange(0, 65));
    }

    [TestMethod]
    public void SetUpTo_ValidIndex_SetsBitsUpToIndex()
    {
        var bitSet = new BitSet(128, AllocatorHandle.Temp);

        bitSet.SetUpTo(64);

        Assert.IsTrue(bitSet[0]);
        Assert.IsTrue(bitSet[63]);
        Assert.IsFalse(bitSet[64]);
        Assert.IsFalse(bitSet[127]);
        Assert.AreEqual(64, bitSet.GetCount());
    }

    [TestMethod]
    public void SetUpTo_NegativeIndex_ThrowsException()
    {
        var bitSet = new BitSet(64, AllocatorHandle.Temp);

        Assert.ThrowsException<ArgumentOutOfRangeException>(() => bitSet.SetUpTo(-1));
    }

    [TestMethod]
    public void SetUpTo_IndexExceedsCapacity_ThrowsException()
    {
        var bitSet = new BitSet(64, AllocatorHandle.Temp);

        Assert.ThrowsException<ArgumentOutOfRangeException>(() => bitSet.SetUpTo(65));
    }

    [TestMethod]
    public void CopyFrom_SameCapacity_CopiesAllBits()
    {
        var source = new BitSet(128, AllocatorHandle.Temp);
        var target = new BitSet(128, AllocatorHandle.Temp);

        source.Add(0);
        source.Add(31);
        source.Add(127);

        target.CopyFrom(source);

        Assert.IsTrue(target[0]);
        Assert.IsTrue(target[31]);
        Assert.IsTrue(target[127]);
        Assert.AreEqual(3, target.GetCount());
    }

    [TestMethod]
    public void CopyFrom_DifferentCapacity_ThrowsException()
    {
        var source = new BitSet(64, AllocatorHandle.Temp);
        var target = new BitSet(128, AllocatorHandle.Temp);

        Assert.ThrowsException<ArgumentException>(() => target.CopyFrom(source));
    }

    [TestMethod]
    public void CopyInverseFrom_SameCapacity_CopiesInvertedBits()
    {
        var source = new BitSet(128, AllocatorHandle.Temp);
        var target = new BitSet(128, AllocatorHandle.Temp);

        source.Add(0);
        source.Add(31);

        target.CopyInverseFrom(source);

        Assert.IsFalse(target[0]);
        Assert.IsFalse(target[31]);
        Assert.IsTrue(target[1]);
        Assert.IsTrue(target[32]);
        Assert.AreEqual(126, target.GetCount());
    }

    [TestMethod]
    public void CopyInverseFrom_DifferentCapacity_ThrowsException()
    {
        var source = new BitSet(64, AllocatorHandle.Temp);
        var target = new BitSet(128, AllocatorHandle.Temp);

        Assert.ThrowsException<ArgumentException>(() => target.CopyInverseFrom(source));
    }

    [TestMethod]
    public void RemoveAll_SameCapacity_RemovesMatchingBits()
    {
        var target = new BitSet(128, AllocatorHandle.Temp);
        var toRemove = new BitSet(128, AllocatorHandle.Temp);

        target.Add(0);
        target.Add(31);
        target.Add(63);
        target.Add(127);

        toRemove.Add(31);
        toRemove.Add(63);

        target.RemoveAll(toRemove);

        Assert.IsTrue(target[0]);
        Assert.IsFalse(target[31]);
        Assert.IsFalse(target[63]);
        Assert.IsTrue(target[127]);
        Assert.AreEqual(2, target.GetCount());
    }

    [TestMethod]
    public void RemoveAll_DifferentCapacity_ThrowsException()
    {
        var target = new BitSet(128, AllocatorHandle.Temp);
        var toRemove = new BitSet(64, AllocatorHandle.Temp);

        Assert.ThrowsException<ArgumentException>(() => target.RemoveAll(toRemove));
    }

    [TestMethod]
    public void Clone_PopulatedBitSet_CreatesIndependentCopy()
    {
        var original = new BitSet(128, AllocatorHandle.Temp);
        original.Add(0);
        original.Add(63);
        original.Add(127);

        var clone = original.Clone();

        Assert.AreEqual(original.Capacity, clone.Capacity);
        Assert.IsTrue(clone[0]);
        Assert.IsTrue(clone[63]);
        Assert.IsTrue(clone[127]);

        clone.Add(31);
        Assert.IsFalse(original[31]);
        Assert.IsTrue(clone[31]);
    }

    [TestMethod]
    public void GetEnumerator_EmptyBitSet_IteratesZeroTimes()
    {
        var bitSet = new BitSet(64, AllocatorHandle.Temp);

        List<int> result = bitSet.ToList();

        Assert.AreEqual(0, result.Count);
    }

    [TestMethod]
    public void GetEnumerator_PopulatedBitSet_IteratesOverSetBits()
    {
        var bitSet = new BitSet(128, AllocatorHandle.Temp);
        bitSet.Add(0);
        bitSet.Add(31);
        bitSet.Add(63);
        bitSet.Add(127);

        List<int> result = bitSet.ToList();

        AssertUtils.SequenceEqual([0, 31, 63, 127], result);
    }

    [TestMethod]
    public void GetEnumerator_CrossingWordBoundaries_IteratesCorrectly()
    {
        var bitSet = new BitSet(192, AllocatorHandle.Temp);
        bitSet.Add(63); // End of first word
        bitSet.Add(64); // Start of second word
        bitSet.Add(127); // End of second word
        bitSet.Add(128); // Start of third word

        List<int> result = [.. bitSet];

        AssertUtils.SequenceEqual([63, 64, 127, 128], result);
    }

    [TestMethod]
    public void Span_Property_ReturnsCorrectSpan()
    {
        var bitSet = new BitSet(128, AllocatorHandle.Temp);
        bitSet.Add(0);
        bitSet.Add(64);

        var span = bitSet.Span.Span;

        Assert.AreEqual(2, span.Length);
        Assert.AreNotEqual(0u, span[0]);
        Assert.AreNotEqual(0u, span[1]);
    }

    [TestMethod]
    public void BitSet_AcrossMultipleWords_WorksCorrectly()
    {
        var bitSet = new BitSet(256, AllocatorHandle.Temp);

        // Test across 4 64-bit words
        bitSet.Add(0); // Word 0
        bitSet.Add(64); // Word 1
        bitSet.Add(128); // Word 2
        bitSet.Add(192); // Word 3
        bitSet.Add(255); // Word 3, last bit

        Assert.IsTrue(bitSet[0]);
        Assert.IsTrue(bitSet[64]);
        Assert.IsTrue(bitSet[128]);
        Assert.IsTrue(bitSet[192]);
        Assert.IsTrue(bitSet[255]);
        Assert.AreEqual(5, bitSet.GetCount());

        List<int> setBits = bitSet.ToList();
        AssertUtils.SequenceEqual([0, 64, 128, 192, 255], setBits);
    }
}
