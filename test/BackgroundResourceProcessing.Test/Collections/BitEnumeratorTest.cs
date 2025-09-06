using System;
using System.Collections.Generic;
using System.Linq;
using BackgroundResourceProcessing.Collections;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace BackgroundResourceProcessing.Test.Collections;

[TestClass]
public sealed class BitEnumeratorTest
{
    [TestMethod]
    public void Constructor_WithZeroBits_CreatesEnumeratorAtStartIndex()
    {
        var enumerator = new BitEnumerator(10, 0UL);

        // Should be at start - 1 initially (per implementation)
        Assert.AreEqual(9, enumerator.Current);
    }

    [TestMethod]
    public void Constructor_WithNonZeroBits_CreatesEnumeratorAtStartIndex()
    {
        var enumerator = new BitEnumerator(64, 0x1UL);

        // Should be at start - 1 initially
        Assert.AreEqual(63, enumerator.Current);
    }

    [TestMethod]
    public void MoveNext_WithZeroBits_ReturnsFalse()
    {
        var enumerator = new BitEnumerator(0, 0UL);

        bool result = enumerator.MoveNext();

        Assert.IsFalse(result);
        Assert.AreEqual(-1, enumerator.Current); // Should remain at start - 1
    }

    [TestMethod]
    public void MoveNext_WithSingleBitAtPosition0_ReturnsCorrectIndex()
    {
        var enumerator = new BitEnumerator(10, 0x1UL); // Bit at position 0

        bool result = enumerator.MoveNext();

        Assert.IsTrue(result);
        Assert.AreEqual(10, enumerator.Current); // start + bit position
    }

    [TestMethod]
    public void MoveNext_WithSingleBitAtPosition5_ReturnsCorrectIndex()
    {
        var enumerator = new BitEnumerator(20, 0x20UL); // Bit at position 5 (binary: 100000)

        bool result = enumerator.MoveNext();

        Assert.IsTrue(result);
        Assert.AreEqual(25, enumerator.Current); // 20 + 5
    }

    [TestMethod]
    public void MoveNext_WithSingleBitAtPosition63_ReturnsCorrectIndex()
    {
        var enumerator = new BitEnumerator(0, 0x8000000000000000UL); // Bit at position 63

        bool result = enumerator.MoveNext();

        Assert.IsTrue(result);
        Assert.AreEqual(63, enumerator.Current);

        // Second call should return false as bit 63 sets bits to 0
        bool secondResult = enumerator.MoveNext();
        Assert.IsFalse(secondResult);
    }

    [TestMethod]
    public void MoveNext_WithMultipleBits_IteratesInOrder()
    {
        // Binary: 10001001 (bits at positions 0, 3, 7)
        var enumerator = new BitEnumerator(10, 0x89UL);
        var results = new List<int>();

        while (enumerator.MoveNext())
        {
            results.Add(enumerator.Current);
        }

        AssertUtils.SequenceEqual([10, 13, 17], results); // 10+0, 10+3, 10+7
    }

    [TestMethod]
    public void MoveNext_WithAllBitsSet_IteratesAllPositions()
    {
        var enumerator = new BitEnumerator(0, 0xFFUL); // First 8 bits set
        var results = new List<int>();

        while (enumerator.MoveNext())
        {
            results.Add(enumerator.Current);
        }

        AssertUtils.SequenceEqual([0, 1, 2, 3, 4, 5, 6, 7], results);
    }

    [TestMethod]
    public void MoveNext_WithAlternatingBits_IteratesCorrectly()
    {
        // Binary: 01010101 (bits at positions 0, 2, 4, 6)
        var enumerator = new BitEnumerator(100, 0x55UL);
        var results = new List<int>();

        while (enumerator.MoveNext())
        {
            results.Add(enumerator.Current);
        }

        AssertUtils.SequenceEqual([100, 102, 104, 106], results);
    }

    [TestMethod]
    public void MoveNext_WithSparseHighBits_IteratesCorrectly()
    {
        // Bits at positions 32 and 48
        ulong value = (1UL << 32) | (1UL << 48);
        var enumerator = new BitEnumerator(0, value);
        var results = new List<int>();

        while (enumerator.MoveNext())
        {
            results.Add(enumerator.Current);
        }

        AssertUtils.SequenceEqual([32, 48], results);
    }

    [TestMethod]
    public void MoveNext_WithMaxValue_IteratesAll64Bits()
    {
        var enumerator = new BitEnumerator(0, ulong.MaxValue);
        var results = new List<int>();

        while (enumerator.MoveNext())
        {
            results.Add(enumerator.Current);
        }

        Assert.AreEqual(64, results.Count);
        for (int i = 0; i < 64; i++)
        {
            Assert.AreEqual(i, results[i], $"Expected bit {i} at position {i}");
        }
    }

    [TestMethod]
    public void MoveNext_AfterExhausted_ContinuesToReturnFalse()
    {
        var enumerator = new BitEnumerator(0, 0x1UL); // Single bit

        // First call should succeed
        Assert.IsTrue(enumerator.MoveNext());
        Assert.AreEqual(0, enumerator.Current);

        // Subsequent calls should fail
        Assert.IsFalse(enumerator.MoveNext());
        Assert.IsFalse(enumerator.MoveNext());
        Assert.IsFalse(enumerator.MoveNext());
    }

    [TestMethod]
    public void Current_BeforeMoveNext_ReturnsStartMinusOne()
    {
        var enumerator1 = new BitEnumerator(0, 0x1UL);
        var enumerator2 = new BitEnumerator(100, 0x1UL);

        Assert.AreEqual(-1, enumerator1.Current);
        Assert.AreEqual(99, enumerator2.Current);
    }

    [TestMethod]
    public void Current_AfterSuccessfulMoveNext_ReturnsCorrectIndex()
    {
        var enumerator = new BitEnumerator(50, 0x8UL); // Bit at position 3

        enumerator.MoveNext();

        Assert.AreEqual(53, enumerator.Current); // 50 + 3
    }

    [TestMethod]
    public void BitShifting_HandlesBoundaryConditions()
    {
        // Test bit 62 (should shift normally)
        var enumerator1 = new BitEnumerator(0, 1UL << 62);
        Assert.IsTrue(enumerator1.MoveNext());
        Assert.AreEqual(62, enumerator1.Current);
        Assert.IsFalse(enumerator1.MoveNext()); // Should be exhausted

        // Test bit 63 (special case - sets bits to 0)
        var enumerator2 = new BitEnumerator(0, 1UL << 63);
        Assert.IsTrue(enumerator2.MoveNext());
        Assert.AreEqual(63, enumerator2.Current);
        Assert.IsFalse(enumerator2.MoveNext()); // Should be exhausted
    }

    [TestMethod]
    public void ComplexBitPattern_IteratesCorrectly()
    {
        // Test a complex bit pattern: 0xF0F0F0F0F0F0F0F0
        // This has alternating nibbles set (4 bits on, 4 bits off)
        ulong pattern = 0xF0F0F0F0F0F0F0F0UL;
        var enumerator = new BitEnumerator(0, pattern);
        var results = new List<int>();

        while (enumerator.MoveNext())
        {
            results.Add(enumerator.Current);
        }

        // Expected bits: 4,5,6,7, 12,13,14,15, 20,21,22,23, etc.
        var expected = new List<int>();
        for (int nibble = 0; nibble < 16; nibble += 2)
        {
            for (int bit = 0; bit < 4; bit++)
            {
                expected.Add(nibble * 4 + bit + 4);
            }
        }

        AssertUtils.SequenceEqual(expected, results);
    }

    [TestMethod]
    public void EdgeCase_StartAtMaxInt_WorksCorrectly()
    {
        // Test with a very high start index
        const int highStart = int.MaxValue - 10;
        var enumerator = new BitEnumerator(highStart, 0x7UL); // Bits 0,1,2
        var results = new List<int>();

        while (enumerator.MoveNext())
        {
            results.Add(enumerator.Current);
        }

        var expected = new List<int> { highStart, highStart + 1, highStart + 2 };
        AssertUtils.SequenceEqual(expected, results);
    }

    [TestMethod]
    public void EdgeCase_AllOddBits_IteratesCorrectly()
    {
        // Set all odd-numbered bits (1, 3, 5, 7, ...)
        ulong oddBits = 0xAAAAAAAAAAAAAAAAUL;
        var enumerator = new BitEnumerator(0, oddBits);
        var results = new List<int>();

        while (enumerator.MoveNext())
        {
            results.Add(enumerator.Current);
        }

        // Should have 32 results (every odd bit from 1 to 63)
        Assert.AreEqual(32, results.Count);
        for (int i = 0; i < 32; i++)
        {
            int expectedBit = i * 2 + 1; // 1, 3, 5, 7, ...
            Assert.AreEqual(
                expectedBit,
                results[i],
                $"Expected bit {expectedBit} at result index {i}"
            );
        }
    }
}
