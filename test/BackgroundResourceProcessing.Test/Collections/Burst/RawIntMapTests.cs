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
    TestAllocator.TestGuard? guard = null;

    [TestInitialize]
    public void Init()
    {
        guard = new TestAllocator.TestGuard();
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (guard is null)
            return;

        guard.Value.Dispose();
        guard = null;
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
    public void Constructor_WithCapacityAndAllocator_CreatesMapWithCapacity()
    {
        const int capacity = 10;
        using var map = new RawIntMap<int>(capacity, Allocator.Temp);

        Assert.AreEqual(capacity, map.Capacity);
        Assert.AreEqual(Allocator.Temp, map.Allocator);
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
        using var map = new RawIntMap<int>(10, Allocator.Temp);

        map.Add(5, 42);

        Assert.IsTrue(map.ContainsKey(5));
        Assert.AreEqual(42, map[5]);
        Assert.AreEqual(1, map.GetCount());
    }

    [TestMethod]
    public void Add_MultipleValidKeys_AddsAllValues()
    {
        using var map = new RawIntMap<int>(10, Allocator.Temp);

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
        using var map = new RawIntMap<int>(10, Allocator.Temp);

        Assert.ThrowsException<IndexOutOfRangeException>(() => map.Add(-1, 42));
    }

    [TestMethod]
    public void Add_KeyOutOfBounds_ThrowsIndexOutOfRangeException()
    {
        using var map = new RawIntMap<int>(10, Allocator.Temp);

        Assert.ThrowsException<IndexOutOfRangeException>(() => map.Add(10, 42));
    }

    [TestMethod]
    public void Add_DuplicateKey_ThrowsArgumentException()
    {
        using var map = new RawIntMap<int>(10, Allocator.Temp);
        map.Add(5, 42);

        Assert.ThrowsException<ArgumentException>(() => map.Add(5, 99));
    }

    #endregion

    #region Set Tests

    [TestMethod]
    public void Set_ValidKey_SetsValue()
    {
        using var map = new RawIntMap<int>(10, Allocator.Temp);

        map.Set(5, 42);

        Assert.IsTrue(map.ContainsKey(5));
        Assert.AreEqual(42, map[5]);
        Assert.AreEqual(1, map.GetCount());
    }

    [TestMethod]
    public void Set_ExistingKey_OverwritesValue()
    {
        using var map = new RawIntMap<int>(10, Allocator.Temp);
        map.Add(5, 42);

        map.Set(5, 99);

        Assert.IsTrue(map.ContainsKey(5));
        Assert.AreEqual(99, map[5]);
        Assert.AreEqual(1, map.GetCount());
    }

    [TestMethod]
    public void Set_NegativeKey_ThrowsIndexOutOfRangeException()
    {
        using var map = new RawIntMap<int>(10, Allocator.Temp);

        Assert.ThrowsException<IndexOutOfRangeException>(() => map.Set(-1, 42));
    }

    [TestMethod]
    public void Set_KeyOutOfBounds_ThrowsIndexOutOfRangeException()
    {
        using var map = new RawIntMap<int>(10, Allocator.Temp);

        Assert.ThrowsException<IndexOutOfRangeException>(() => map.Set(10, 42));
    }

    #endregion

    #region Indexer Tests

    [TestMethod]
    public void Indexer_ValidKey_ReturnsValue()
    {
        using var map = new RawIntMap<int>(10, Allocator.Temp);
        map.Add(5, 42);

        var result = map[5];

        Assert.AreEqual(42, result);
    }

    [TestMethod]
    public void Indexer_ValidKey_CanModifyValue()
    {
        using var map = new RawIntMap<TestValue>(10, Allocator.Temp);
        map.Add(5, new TestValue(42));

        map[5] = new TestValue(99);

        Assert.AreEqual(99, map[5].Value);
    }

    [TestMethod]
    public void Indexer_KeyNotPresent_ThrowsKeyNotFoundException()
    {
        using var map = new RawIntMap<int>(10, Allocator.Temp);

        Assert.ThrowsException<KeyNotFoundException>(() => _ = map[5]);
    }

    [TestMethod]
    public void Indexer_NegativeKey_ThrowsIndexOutOfRangeException()
    {
        using var map = new RawIntMap<int>(10, Allocator.Temp);

        Assert.ThrowsException<IndexOutOfRangeException>(() => _ = map[-1]);
    }

    [TestMethod]
    public void Indexer_KeyOutOfBounds_ThrowsIndexOutOfRangeException()
    {
        using var map = new RawIntMap<int>(10, Allocator.Temp);

        Assert.ThrowsException<IndexOutOfRangeException>(() => _ = map[10]);
    }

    #endregion

    #region TryGetValue Tests

    [TestMethod]
    public void TryGetValue_ValidKey_ReturnsTrue()
    {
        using var map = new RawIntMap<int>(10, Allocator.Temp);
        map.Add(5, 42);

        bool result = map.TryGetValue(5, out int value);

        Assert.IsTrue(result);
        Assert.AreEqual(42, value);
    }

    [TestMethod]
    public void TryGetValue_KeyNotPresent_ReturnsFalse()
    {
        using var map = new RawIntMap<int>(10, Allocator.Temp);

        bool result = map.TryGetValue(5, out int value);

        Assert.IsFalse(result);
        Assert.AreEqual(0, value);
    }

    [TestMethod]
    public void TryGetValue_NegativeKey_ReturnsFalse()
    {
        using var map = new RawIntMap<int>(10, Allocator.Temp);

        bool result = map.TryGetValue(-1, out int value);

        Assert.IsFalse(result);
        Assert.AreEqual(0, value);
    }

    [TestMethod]
    public void TryGetValue_KeyOutOfBounds_ReturnsFalse()
    {
        using var map = new RawIntMap<int>(10, Allocator.Temp);

        bool result = map.TryGetValue(10, out int value);

        Assert.IsFalse(result);
        Assert.AreEqual(0, value);
    }

    #endregion

    #region ContainsKey Tests

    [TestMethod]
    public void ContainsKey_ValidKey_ReturnsTrue()
    {
        using var map = new RawIntMap<int>(10, Allocator.Temp);
        map.Add(5, 42);

        Assert.IsTrue(map.ContainsKey(5));
    }

    [TestMethod]
    public void ContainsKey_KeyNotPresent_ReturnsFalse()
    {
        using var map = new RawIntMap<int>(10, Allocator.Temp);

        Assert.IsFalse(map.ContainsKey(5));
    }

    [TestMethod]
    public void ContainsKey_NegativeKey_ReturnsFalse()
    {
        using var map = new RawIntMap<int>(10, Allocator.Temp);

        Assert.IsFalse(map.ContainsKey(-1));
    }

    [TestMethod]
    public void ContainsKey_KeyOutOfBounds_ReturnsFalse()
    {
        using var map = new RawIntMap<int>(10, Allocator.Temp);

        Assert.IsFalse(map.ContainsKey(10));
    }

    #endregion

    #region Remove Tests

    [TestMethod]
    public void Remove_ValidKey_RemovesKeyAndReturnsTrue()
    {
        using var map = new RawIntMap<int>(10, Allocator.Temp);
        map.Add(5, 42);

        bool result = map.Remove(5);

        Assert.IsTrue(result);
        Assert.IsFalse(map.ContainsKey(5));
        Assert.AreEqual(0, map.GetCount());
    }

    [TestMethod]
    public void Remove_KeyNotPresent_ReturnsFalse()
    {
        using var map = new RawIntMap<int>(10, Allocator.Temp);

        bool result = map.Remove(5);

        Assert.IsFalse(result);
    }

    [TestMethod]
    public void Remove_NegativeKey_ReturnsFalse()
    {
        using var map = new RawIntMap<int>(10, Allocator.Temp);

        bool result = map.Remove(-1);

        Assert.IsFalse(result);
    }

    [TestMethod]
    public void Remove_KeyOutOfBounds_ReturnsFalse()
    {
        using var map = new RawIntMap<int>(10, Allocator.Temp);

        bool result = map.Remove(10);

        Assert.IsFalse(result);
    }

    [TestMethod]
    public void Remove_MultipleKeys_RemovesCorrectKeys()
    {
        using var map = new RawIntMap<int>(10, Allocator.Temp);
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
        using var map = new RawIntMap<int>(10, Allocator.Temp);

        map.Clear();

        Assert.AreEqual(0, map.GetCount());
    }

    [TestMethod]
    public void Clear_NonEmptyMap_RemovesAllEntries()
    {
        using var map = new RawIntMap<int>(10, Allocator.Temp);
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
        using var map = new RawIntMap<int>(10, Allocator.Temp);

        Assert.AreEqual(0, map.GetCount());
    }

    [TestMethod]
    public void GetCount_WithEntries_ReturnsCorrectCount()
    {
        using var map = new RawIntMap<int>(10, Allocator.Temp);
        map.Add(1, 10);
        map.Add(3, 30);
        map.Add(5, 50);

        Assert.AreEqual(3, map.GetCount());
    }

    [TestMethod]
    public void GetCount_AfterRemoval_ReturnsCorrectCount()
    {
        using var map = new RawIntMap<int>(10, Allocator.Temp);
        map.Add(1, 10);
        map.Add(3, 30);
        map.Add(5, 50);

        map.Remove(3);

        Assert.AreEqual(2, map.GetCount());
    }

    #endregion

    #region Entry Tests

    [TestMethod]
    public void GetEntry_ValidKey_ReturnsEntry()
    {
        using var map = new RawIntMap<int>(10, Allocator.Temp);
        map.Add(5, 42);

        var entry = map.GetEntry(5);

        Assert.IsTrue(entry.HasValue);
        Assert.AreEqual(42, entry.Value);
    }

    [TestMethod]
    public void GetEntry_KeyNotPresent_ReturnsEntryWithoutValue()
    {
        using var map = new RawIntMap<int>(10, Allocator.Temp);

        var entry = map.GetEntry(5);

        Assert.IsFalse(entry.HasValue);
    }

    [TestMethod]
    public void GetEntry_NegativeKey_ThrowsIndexOutOfRangeException()
    {
        using var map = new RawIntMap<int>(10, Allocator.Temp);

        Assert.ThrowsException<IndexOutOfRangeException>(() => map.GetEntry(-1));
    }

    [TestMethod]
    public void GetEntry_KeyOutOfBounds_ThrowsIndexOutOfRangeException()
    {
        using var map = new RawIntMap<int>(10, Allocator.Temp);

        Assert.ThrowsException<IndexOutOfRangeException>(() => map.GetEntry(10));
    }

    [TestMethod]
    public void EntryInsert_ValidKey_InsertsValue()
    {
        using var map = new RawIntMap<TestValue>(10, Allocator.Temp);
        var entry = map.GetEntry(5);

        ref var inserted = ref entry.Insert(new TestValue(42));

        Assert.IsTrue(map.ContainsKey(5));
        Assert.AreEqual(42, map[5].Value);
        Assert.AreEqual(42, inserted.Value);
    }

    [TestMethod]
    public void EntryValue_KeyNotPresent_ThrowsKeyNotFoundException()
    {
        using var map = new RawIntMap<int>(10, Allocator.Temp);
        var entry = map.GetEntry(5);

        Assert.ThrowsException<KeyNotFoundException>(() => _ = entry.Value);
    }

    #endregion

    #region Enumeration Tests

    [TestMethod]
    public void GetEnumerator_EmptyMap_ReturnsEmptySequence()
    {
        using var map = new RawIntMap<int>(10, Allocator.Temp);

        var items = map.ToList();

        Assert.AreEqual(0, items.Count);
    }

    [TestMethod]
    public void GetEnumerator_WithEntries_ReturnsCorrectKeyValuePairs()
    {
        using var map = new RawIntMap<int>(10, Allocator.Temp);
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
        using var map = new RawIntMap<int>(10, Allocator.Temp);

        var keys = map.Keys.ToList();

        Assert.AreEqual(0, keys.Count);
    }

    [TestMethod]
    public void Keys_WithEntries_ReturnsCorrectKeys()
    {
        using var map = new RawIntMap<int>(10, Allocator.Temp);
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
        using var map = new RawIntMap<int>(10, Allocator.Temp);

        var values = map.Values.ToList();

        Assert.AreEqual(0, values.Count);
    }

    [TestMethod]
    public void Values_WithEntries_ReturnsCorrectValues()
    {
        using var map = new RawIntMap<int>(10, Allocator.Temp);
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
        using var map = new RawIntMap<int>(10, Allocator.Temp);
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

    #region Dispose Tests

    [TestMethod]
    public void Dispose_EmptyMap_DoesNotThrow()
    {
        var map = new RawIntMap<int>(10, Allocator.Temp);

        map.Dispose();
    }

    [TestMethod]
    public void Dispose_NonEmptyMap_DisposesSuccessfully()
    {
        var map = new RawIntMap<int>(10, Allocator.Temp);
        map.Add(1, 10);
        map.Add(3, 30);
        map.Add(5, 50);

        map.Dispose();
    }

    [TestMethod]
    public void Dispose_CalledTwice_DoesNotThrow()
    {
        var map = new RawIntMap<int>(10, Allocator.Temp);
        map.Add(1, 10);

        map.Dispose();
        map.Dispose();
    }

    #endregion

    #region Edge Cases and Stress Tests

    [TestMethod]
    public void StressTest_ManyOperations_MaintainsConsistency()
    {
        using var map = new RawIntMap<int>(1000, Allocator.Temp);

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
        using var map = new RawIntMap<int>(10000, Allocator.Temp);

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
        using var map = new RawIntMap<int>(20, Allocator.Temp);
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
