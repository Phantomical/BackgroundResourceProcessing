using System;
using System.Collections.Generic;
using System.Linq;
using BackgroundResourceProcessing.Collections.Burst;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Unity.Collections;

namespace BackgroundResourceProcessing.Test.Collections.Burst;

[TestClass]
public sealed class RawIntMapTests
{
    [TestCleanup]
    public void Cleanup()
    {
        TestAllocator.Cleanup();
    }

    private struct TestValue : IEquatable<TestValue>
    {
        public int Value { get; set; }

        public TestValue(int value)
        {
            Value = value;
        }

        public bool Equals(TestValue other) => Value == other.Value;

        public override bool Equals(object obj) => obj is TestValue other && Equals(other);

        public override int GetHashCode() => Value.GetHashCode();

        public static bool operator ==(TestValue left, TestValue right) => left.Equals(right);

        public static bool operator !=(TestValue left, TestValue right) => !left.Equals(right);
    }

    #region Constructor Tests

    [TestMethod]
    public void Constructor_WithCapacity_CreatesMapWithCapacity()
    {
        const int capacity = 10;
        var map = new RawIntMap<int>(capacity);

        Assert.AreEqual(capacity, map.Capacity);
    }

    [TestMethod]
    public void Constructor_Default_CreatesEmptyMap()
    {
        var map = new RawIntMap<int>();

        Assert.AreEqual(0, map.Capacity);
    }

    #endregion

    #region Add Tests

    [TestMethod]
    public void Add_ValidKey_AddsValue()
    {
        var map = new RawIntMap<int>(10);

        map.Add(5, 42);

        Assert.IsTrue(map.ContainsKey(5));
        Assert.AreEqual(42, map[5]);
        Assert.AreEqual(1, map.GetCount());
    }

    [TestMethod]
    public void Add_MultipleValidKeys_AddsAllValues()
    {
        var map = new RawIntMap<int>(10);

        map.Add(1, 10);
        map.Add(3, 30);
        map.Add(7, 70);

        Assert.IsTrue(map.ContainsKey(1));
        Assert.IsTrue(map.ContainsKey(3));
        Assert.IsTrue(map.ContainsKey(7));
        Assert.AreEqual(10, map[1]);
        Assert.AreEqual(30, map[3]);
        Assert.AreEqual(70, map[7]);
        Assert.AreEqual(3, map.GetCount());
    }

    [TestMethod]
    public void Add_NegativeKey_ThrowsIndexOutOfRangeException()
    {
        var map = new RawIntMap<int>(10);

        Assert.ThrowsException<IndexOutOfRangeException>(() => map.Add(-1, 42));
    }

    [TestMethod]
    public void Add_KeyOutOfBounds_ThrowsIndexOutOfRangeException()
    {
        var map = new RawIntMap<int>(10);

        Assert.ThrowsException<IndexOutOfRangeException>(() => map.Add(10, 42));
    }

    [TestMethod]
    public void Add_DuplicateKey_ThrowsArgumentException()
    {
        var map = new RawIntMap<int>(10);
        map.Add(5, 42);

        Assert.ThrowsException<ArgumentException>(() => map.Add(5, 99));
    }

    #endregion

    #region Set Tests

    [TestMethod]
    public void Set_ValidKey_SetsValue()
    {
        var map = new RawIntMap<int>(10);

        map.Set(5, 42);

        Assert.IsTrue(map.ContainsKey(5));
        Assert.AreEqual(42, map[5]);
        Assert.AreEqual(1, map.GetCount());
    }

    [TestMethod]
    public void Set_ExistingKey_OverwritesValue()
    {
        var map = new RawIntMap<int>(10);
        map.Add(5, 42);

        map.Set(5, 99);

        Assert.IsTrue(map.ContainsKey(5));
        Assert.AreEqual(99, map[5]);
        Assert.AreEqual(1, map.GetCount());
    }

    [TestMethod]
    public void Set_NegativeKey_ThrowsIndexOutOfRangeException()
    {
        var map = new RawIntMap<int>(10);

        Assert.ThrowsException<IndexOutOfRangeException>(() => map.Set(-1, 42));
    }

    [TestMethod]
    public void Set_KeyOutOfBounds_ThrowsIndexOutOfRangeException()
    {
        var map = new RawIntMap<int>(10);

        Assert.ThrowsException<IndexOutOfRangeException>(() => map.Set(10, 42));
    }

    #endregion

    #region Indexer Tests

    [TestMethod]
    public void Indexer_ValidKey_ReturnsValue()
    {
        var map = new RawIntMap<int>(10);
        map.Add(5, 42);

        var result = map[5];

        Assert.AreEqual(42, result);
    }

    [TestMethod]
    public void Indexer_ValidKey_CanModifyValue()
    {
        var map = new RawIntMap<TestValue>(10);
        map.Add(5, new TestValue(42));

        map[5] = new TestValue(99);

        Assert.AreEqual(99, map[5].Value);
    }

    [TestMethod]
    public void Indexer_KeyNotPresent_ThrowsKeyNotFoundException()
    {
        var map = new RawIntMap<int>(10);

        Assert.ThrowsException<KeyNotFoundException>(() => _ = map[5]);
    }

    [TestMethod]
    public void Indexer_NegativeKey_ThrowsIndexOutOfRangeException()
    {
        var map = new RawIntMap<int>(10);

        Assert.ThrowsException<IndexOutOfRangeException>(() => _ = map[-1]);
    }

    [TestMethod]
    public void Indexer_KeyOutOfBounds_ThrowsIndexOutOfRangeException()
    {
        var map = new RawIntMap<int>(10);

        Assert.ThrowsException<IndexOutOfRangeException>(() => _ = map[10]);
    }

    #endregion

    #region TryGetValue Tests

    [TestMethod]
    public void TryGetValue_ValidKey_ReturnsTrue()
    {
        var map = new RawIntMap<int>(10);
        map.Add(5, 42);

        bool result = map.TryGetValue(5, out int value);

        Assert.IsTrue(result);
        Assert.AreEqual(42, value);
    }

    [TestMethod]
    public void TryGetValue_KeyNotPresent_ReturnsFalse()
    {
        var map = new RawIntMap<int>(10);

        bool result = map.TryGetValue(5, out int value);

        Assert.IsFalse(result);
        Assert.AreEqual(0, value);
    }

    [TestMethod]
    public void TryGetValue_NegativeKey_ReturnsFalse()
    {
        var map = new RawIntMap<int>(10);

        bool result = map.TryGetValue(-1, out int value);

        Assert.IsFalse(result);
        Assert.AreEqual(0, value);
    }

    [TestMethod]
    public void TryGetValue_KeyOutOfBounds_ReturnsFalse()
    {
        var map = new RawIntMap<int>(10);

        bool result = map.TryGetValue(10, out int value);

        Assert.IsFalse(result);
        Assert.AreEqual(0, value);
    }

    #endregion

    #region ContainsKey Tests

    [TestMethod]
    public void ContainsKey_ValidKey_ReturnsTrue()
    {
        var map = new RawIntMap<int>(10);
        map.Add(5, 42);

        Assert.IsTrue(map.ContainsKey(5));
    }

    [TestMethod]
    public void ContainsKey_KeyNotPresent_ReturnsFalse()
    {
        var map = new RawIntMap<int>(10);

        Assert.IsFalse(map.ContainsKey(5));
    }

    [TestMethod]
    public void ContainsKey_NegativeKey_ReturnsFalse()
    {
        var map = new RawIntMap<int>(10);

        Assert.IsFalse(map.ContainsKey(-1));
    }

    [TestMethod]
    public void ContainsKey_KeyOutOfBounds_ReturnsFalse()
    {
        var map = new RawIntMap<int>(10);

        Assert.IsFalse(map.ContainsKey(10));
    }

    #endregion

    #region Remove Tests

    [TestMethod]
    public void Remove_ValidKey_RemovesKeyAndReturnsTrue()
    {
        var map = new RawIntMap<int>(10);
        map.Add(5, 42);

        bool result = map.Remove(5);

        Assert.IsTrue(result);
        Assert.IsFalse(map.ContainsKey(5));
        Assert.AreEqual(0, map.GetCount());
    }

    [TestMethod]
    public void Remove_KeyNotPresent_ReturnsFalse()
    {
        var map = new RawIntMap<int>(10);

        bool result = map.Remove(5);

        Assert.IsFalse(result);
    }

    [TestMethod]
    public void Remove_NegativeKey_ReturnsFalse()
    {
        var map = new RawIntMap<int>(10);

        bool result = map.Remove(-1);

        Assert.IsFalse(result);
    }

    [TestMethod]
    public void Remove_KeyOutOfBounds_ReturnsFalse()
    {
        var map = new RawIntMap<int>(10);

        bool result = map.Remove(10);

        Assert.IsFalse(result);
    }

    [TestMethod]
    public void Remove_MultipleKeys_RemovesCorrectKeys()
    {
        var map = new RawIntMap<int>(10);
        map.Add(1, 10);
        map.Add(3, 30);
        map.Add(5, 50);

        map.Remove(3);

        Assert.IsTrue(map.ContainsKey(1));
        Assert.IsFalse(map.ContainsKey(3));
        Assert.IsTrue(map.ContainsKey(5));
        Assert.AreEqual(2, map.GetCount());
    }

    #endregion

    #region Clear Tests

    [TestMethod]
    public void Clear_EmptyMap_RemainsEmpty()
    {
        var map = new RawIntMap<int>(10);

        map.Clear();

        Assert.AreEqual(0, map.GetCount());
    }

    [TestMethod]
    public void Clear_NonEmptyMap_RemovesAllEntries()
    {
        var map = new RawIntMap<int>(10);
        map.Add(1, 10);
        map.Add(3, 30);
        map.Add(5, 50);

        map.Clear();

        Assert.AreEqual(0, map.GetCount());
        Assert.IsFalse(map.ContainsKey(1));
        Assert.IsFalse(map.ContainsKey(3));
        Assert.IsFalse(map.ContainsKey(5));
    }

    #endregion

    #region GetCount Tests

    [TestMethod]
    public void GetCount_EmptyMap_ReturnsZero()
    {
        var map = new RawIntMap<int>(10);

        Assert.AreEqual(0, map.GetCount());
    }

    [TestMethod]
    public void GetCount_WithEntries_ReturnsCorrectCount()
    {
        var map = new RawIntMap<int>(10);
        map.Add(1, 10);
        map.Add(3, 30);
        map.Add(5, 50);

        Assert.AreEqual(3, map.GetCount());
    }

    [TestMethod]
    public void GetCount_AfterRemoval_ReturnsCorrectCount()
    {
        var map = new RawIntMap<int>(10);
        map.Add(1, 10);
        map.Add(3, 30);
        map.Add(5, 50);

        map.Remove(3);

        Assert.AreEqual(2, map.GetCount());
    }

    #endregion

    #region Count Property Tests

    [TestMethod]
    public void Count_EmptyMap_ReturnsZero()
    {
        var map = new RawIntMap<int>(10);

        Assert.AreEqual(0, map.Count);
    }

    [TestMethod]
    public void Count_AfterAdd_IncrementsCorrectly()
    {
        var map = new RawIntMap<int>(10);

        Assert.AreEqual(0, map.Count);

        map.Add(0, 10);
        Assert.AreEqual(1, map.Count);

        map.Add(5, 50);
        Assert.AreEqual(2, map.Count);

        map.Add(9, 90);
        Assert.AreEqual(3, map.Count);
    }

    [TestMethod]
    public void Count_AfterSet_OnNewKey_IncrementsCorrectly()
    {
        var map = new RawIntMap<int>(10);

        Assert.AreEqual(0, map.Count);

        map.Set(3, 30);
        Assert.AreEqual(1, map.Count);

        map.Set(7, 70);
        Assert.AreEqual(2, map.Count);
    }

    [TestMethod]
    public void Count_AfterSet_OnExistingKey_RemainsUnchanged()
    {
        var map = new RawIntMap<int>(10);
        map.Add(5, 50);

        Assert.AreEqual(1, map.Count);

        map.Set(5, 99);
        Assert.AreEqual(1, map.Count);

        map.Set(5, 42);
        Assert.AreEqual(1, map.Count);
    }

    [TestMethod]
    public void Count_AfterRemove_DecrementsCorrectly()
    {
        var map = new RawIntMap<int>(10);
        map.Add(1, 10);
        map.Add(3, 30);
        map.Add(5, 50);

        Assert.AreEqual(3, map.Count);

        map.Remove(3);
        Assert.AreEqual(2, map.Count);

        map.Remove(1);
        Assert.AreEqual(1, map.Count);

        map.Remove(5);
        Assert.AreEqual(0, map.Count);
    }

    [TestMethod]
    public void Count_AfterRemove_NonExistentKey_RemainsUnchanged()
    {
        var map = new RawIntMap<int>(10);
        map.Add(5, 50);

        Assert.AreEqual(1, map.Count);

        map.Remove(3);
        Assert.AreEqual(1, map.Count);

        map.Remove(7);
        Assert.AreEqual(1, map.Count);
    }

    [TestMethod]
    public void Count_AfterTryRemove_Success_DecrementsCorrectly()
    {
        var map = new RawIntMap<int>(10);
        map.Add(2, 20);
        map.Add(4, 40);
        map.Add(6, 60);

        Assert.AreEqual(3, map.Count);

        bool removed = map.TryRemove(4, out int value);
        Assert.IsTrue(removed);
        Assert.AreEqual(40, value);
        Assert.AreEqual(2, map.Count);
    }

    [TestMethod]
    public void Count_AfterTryRemove_Failure_RemainsUnchanged()
    {
        var map = new RawIntMap<int>(10);
        map.Add(5, 50);

        Assert.AreEqual(1, map.Count);

        bool removed = map.TryRemove(3, out int value);
        Assert.IsFalse(removed);
        Assert.AreEqual(0, value);
        Assert.AreEqual(1, map.Count);
    }

    [TestMethod]
    public void Count_AfterClear_ReturnsZero()
    {
        var map = new RawIntMap<int>(10);
        map.Add(1, 10);
        map.Add(3, 30);
        map.Add(5, 50);

        Assert.AreEqual(3, map.Count);

        map.Clear();
        Assert.AreEqual(0, map.Count);
    }

    [TestMethod]
    public void Count_AfterClear_EmptyMap_RemainsZero()
    {
        var map = new RawIntMap<int>(10);

        Assert.AreEqual(0, map.Count);

        map.Clear();
        Assert.AreEqual(0, map.Count);
    }

    [TestMethod]
    public void Count_ComplexModificationSequence_TracksCorrectly()
    {
        var map = new RawIntMap<int>(10);

        // Start empty
        Assert.AreEqual(0, map.Count);

        // Add some entries
        map.Add(1, 10);
        map.Add(3, 30);
        map.Add(5, 50);
        Assert.AreEqual(3, map.Count);

        // Remove one
        map.Remove(3);
        Assert.AreEqual(2, map.Count);

        // Set existing (no change in count)
        map.Set(1, 11);
        Assert.AreEqual(2, map.Count);

        // Set new (increment count)
        map.Set(7, 70);
        Assert.AreEqual(3, map.Count);

        // Try remove existing (decrement count)
        map.TryRemove(5, out _);
        Assert.AreEqual(2, map.Count);

        // Try remove non-existent (no change)
        map.TryRemove(9, out _);
        Assert.AreEqual(2, map.Count);

        // Add another
        map.Add(0, 0);
        Assert.AreEqual(3, map.Count);

        // Clear all
        map.Clear();
        Assert.AreEqual(0, map.Count);

        // Add after clear
        map.Add(2, 20);
        Assert.AreEqual(1, map.Count);
    }

    [TestMethod]
    public void Count_WithBoundaryKeys_TracksCorrectly()
    {
        var map = new RawIntMap<int>(10);

        // Test with key 0 (lower boundary)
        map.Add(0, 100);
        Assert.AreEqual(1, map.Count);

        // Test with key 9 (upper boundary for capacity 10)
        map.Add(9, 900);
        Assert.AreEqual(2, map.Count);

        // Remove boundary keys
        map.Remove(0);
        Assert.AreEqual(1, map.Count);

        map.Remove(9);
        Assert.AreEqual(0, map.Count);
    }

    [TestMethod]
    public void Count_ConsistentWithEnumerationCount()
    {
        var map = new RawIntMap<int>(20);

        // Add entries at various positions
        map.Add(0, 10);
        map.Add(5, 50);
        map.Add(10, 100);
        map.Add(15, 150);
        map.Add(19, 190);

        int enumeratedCount = map.Count();
        Assert.AreEqual(5, map.Count);
        Assert.AreEqual(map.Count, enumeratedCount);

        // Remove some entries
        map.Remove(5);
        map.Remove(15);

        enumeratedCount = map.Count();
        Assert.AreEqual(3, map.Count);
        Assert.AreEqual(map.Count, enumeratedCount);
    }

    [TestMethod]
    public void Count_AfterFailedOperations_RemainsCorrect()
    {
        var map = new RawIntMap<int>(5);
        map.Add(2, 20);

        Assert.AreEqual(1, map.Count);

        // Try to add duplicate key (should fail but count unchanged)
        Assert.ThrowsException<ArgumentException>(() => map.Add(2, 99));
        Assert.AreEqual(1, map.Count);

        // Try operations with out-of-bounds keys (should fail but count unchanged)
        Assert.ThrowsException<IndexOutOfRangeException>(() => map.Add(-1, 10));
        Assert.AreEqual(1, map.Count);

        Assert.ThrowsException<IndexOutOfRangeException>(() => map.Add(5, 10));
        Assert.AreEqual(1, map.Count);

        Assert.ThrowsException<IndexOutOfRangeException>(() => map.Set(-1, 10));
        Assert.AreEqual(1, map.Count);

        Assert.ThrowsException<IndexOutOfRangeException>(() => map.Set(10, 10));
        Assert.AreEqual(1, map.Count);

        // Operations that don't throw but return false shouldn't change count
        bool removed = map.Remove(-1);
        Assert.IsFalse(removed);
        Assert.AreEqual(1, map.Count);

        removed = map.Remove(10);
        Assert.IsFalse(removed);
        Assert.AreEqual(1, map.Count);

        bool tryRemoved = map.TryRemove(-1, out _);
        Assert.IsFalse(tryRemoved);
        Assert.AreEqual(1, map.Count);

        tryRemoved = map.TryRemove(10, out _);
        Assert.IsFalse(tryRemoved);
        Assert.AreEqual(1, map.Count);
    }

    #endregion

    #region

    #endregion

    #region Enumeration Tests

    [TestMethod]
    public void GetEnumerator_EmptyMap_ReturnsEmptySequence()
    {
        var map = new RawIntMap<int>(10);

        var items = map.ToList();

        Assert.AreEqual(0, items.Count);
    }

    [TestMethod]
    public void GetEnumerator_WithEntries_ReturnsCorrectKeyValuePairs()
    {
        var map = new RawIntMap<int>(10);
        map.Add(1, 10);
        map.Add(3, 30);
        map.Add(7, 70);

        var items = map.ToList();

        Assert.AreEqual(3, items.Count);
        Assert.IsTrue(items.Contains(new KeyValuePair<int, int>(1, 10)));
        Assert.IsTrue(items.Contains(new KeyValuePair<int, int>(3, 30)));
        Assert.IsTrue(items.Contains(new KeyValuePair<int, int>(7, 70)));
    }

    [TestMethod]
    public void Keys_EmptyMap_ReturnsEmptySequence()
    {
        var map = new RawIntMap<int>(10);

        var keys = map.Keys.ToList();

        Assert.AreEqual(0, keys.Count);
    }

    [TestMethod]
    public void Keys_WithEntries_ReturnsCorrectKeys()
    {
        var map = new RawIntMap<int>(10);
        map.Add(1, 10);
        map.Add(3, 30);
        map.Add(7, 70);

        var keys = map.Keys.ToList();

        Assert.AreEqual(3, keys.Count);
        Assert.IsTrue(keys.Contains(1));
        Assert.IsTrue(keys.Contains(3));
        Assert.IsTrue(keys.Contains(7));
    }

    [TestMethod]
    public void Values_EmptyMap_ReturnsEmptySequence()
    {
        var map = new RawIntMap<int>(10);
        var values = map.Values.ToList();

        Assert.AreEqual(0, values.Count);
    }

    [TestMethod]
    public void Values_WithEntries_ReturnsCorrectValues()
    {
        var map = new RawIntMap<int>(10);
        map.Add(1, 10);
        map.Add(3, 30);
        map.Add(7, 70);

        var values = map.Values.ToList();

        Assert.AreEqual(3, values.Count);
        Assert.IsTrue(values.Contains(10));
        Assert.IsTrue(values.Contains(30));
        Assert.IsTrue(values.Contains(70));
    }

    [TestMethod]
    public void GetEnumeratorAt_ValidOffset_StartsFromCorrectPosition()
    {
        var map = new RawIntMap<int>(10);
        map.Add(1, 10);
        map.Add(3, 30);
        map.Add(7, 70);

        var enumerator = map.GetEnumeratorAt(3);
        var remainingItems = new List<KeyValuePair<int, int>>();

        while (enumerator.MoveNext())
        {
            remainingItems.Add(enumerator.Current);
        }

        Assert.IsTrue(remainingItems.Contains(new KeyValuePair<int, int>(3, 30)));
        Assert.IsTrue(remainingItems.Contains(new KeyValuePair<int, int>(7, 70)));
        Assert.IsFalse(remainingItems.Contains(new KeyValuePair<int, int>(1, 10)));
    }

    #endregion


    #region Edge Cases and Stress Tests

    [TestMethod]
    public void StressTest_ManyOperations_MaintainsConsistency()
    {
        var map = new RawIntMap<int>(1000);

        // Add many items
        for (int i = 0; i < 500; i++)
        {
            map.Add(i * 2, i);
        }

        Assert.AreEqual(500, map.GetCount());

        // Verify all items are correct
        for (int i = 0; i < 500; i++)
        {
            Assert.IsTrue(map.ContainsKey(i * 2));
            Assert.AreEqual(i, map[i * 2]);
        }

        // Remove every other item
        for (int i = 0; i < 250; i++)
        {
            Assert.IsTrue(map.Remove(i * 4));
        }

        Assert.AreEqual(250, map.GetCount());

        // Verify remaining items are correct
        for (int i = 0; i < 250; i++)
        {
            int key = (i * 4) + 2;
            Assert.IsTrue(map.ContainsKey(key));
        }
    }

    [TestMethod]
    public void LargeCapacity_WorksCorrectly()
    {
        var map = new RawIntMap<int>(10000);

        // Add sparse items
        for (int i = 0; i < 100; i++)
        {
            int key = i * 100;
            map.Add(key, i);
        }

        Assert.AreEqual(100, map.GetCount());

        // Verify all items
        for (int i = 0; i < 100; i++)
        {
            int key = i * 100;
            Assert.IsTrue(map.ContainsKey(key));
            Assert.AreEqual(i, map[key]);
        }
    }

    [TestMethod]
    public void EnumerationOrder_ConsistentAcrossMultipleCalls()
    {
        var map = new RawIntMap<int>(20);
        map.Add(5, 50);
        map.Add(15, 150);
        map.Add(2, 20);
        map.Add(18, 180);

        var firstEnumeration = map.ToList();
        var secondEnumeration = map.ToList();

        CollectionAssert.AreEqual(firstEnumeration, secondEnumeration);
    }

    #endregion
}
