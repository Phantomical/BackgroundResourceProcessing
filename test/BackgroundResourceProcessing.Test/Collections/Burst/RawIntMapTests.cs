using System;
using System.Collections.Generic;
using System.Linq;
using BackgroundResourceProcessing.Collections.Burst;
using KSP.Testing;
using Unity.Collections;

namespace BackgroundResourceProcessing.Test.Collections.Burst;

public sealed class RawIntMapTests : BRPTestBase
{
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

    [TestInfo("RawIntMapTests_Constructor_WithCapacity_CreatesMapWithCapacity")]
    public void Constructor_WithCapacity_CreatesMapWithCapacity()
    {
        const int capacity = 10;
        var map = new RawIntMap<int>(capacity, AllocatorHandle.Temp);

        Assert.AreEqual(capacity, map.Capacity);
    }

    [TestInfo("RawIntMapTests_Constructor_Default_CreatesEmptyMap")]
    public void Constructor_Default_CreatesEmptyMap()
    {
        var map = new RawIntMap<int>(0, AllocatorHandle.Temp);

        Assert.AreEqual(0, map.Capacity);
    }

    #endregion

    #region Add Tests

    [TestInfo("RawIntMapTests_Add_ValidKey_AddsValue")]
    public void Add_ValidKey_AddsValue()
    {
        var map = new RawIntMap<int>(10, AllocatorHandle.Temp);

        map.Add(5, 42);

        Assert.IsTrue(map.ContainsKey(5));
        Assert.AreEqual(42, map[5]);
        Assert.AreEqual(1, map.GetCount());
    }

    [TestInfo("RawIntMapTests_Add_MultipleValidKeys_AddsAllValues")]
    public void Add_MultipleValidKeys_AddsAllValues()
    {
        var map = new RawIntMap<int>(10, AllocatorHandle.Temp);

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

    [TestInfo("RawIntMapTests_Add_NegativeKey_ThrowsIndexOutOfRangeException")]
    public void Add_NegativeKey_ThrowsIndexOutOfRangeException()
    {
        var map = new RawIntMap<int>(10, AllocatorHandle.Temp);

        Assert.ThrowsException<KeyNotFoundException>(() => map.Add(-1, 42));
    }

    [TestInfo("RawIntMapTests_Add_KeyOutOfBounds_ThrowsIndexOutOfRangeException")]
    public void Add_KeyOutOfBounds_ThrowsIndexOutOfRangeException()
    {
        var map = new RawIntMap<int>(10, AllocatorHandle.Temp);

        Assert.ThrowsException<KeyNotFoundException>(() => map.Add(10, 42));
    }

    [TestInfo("RawIntMapTests_Add_DuplicateKey_ThrowsArgumentException")]
    public void Add_DuplicateKey_ThrowsArgumentException()
    {
        var map = new RawIntMap<int>(10, AllocatorHandle.Temp);
        map.Add(5, 42);

        Assert.ThrowsException<ArgumentException>(() => map.Add(5, 99));
    }

    #endregion

    #region Set Tests

    [TestInfo("RawIntMapTests_Set_ValidKey_SetsValue")]
    public void Set_ValidKey_SetsValue()
    {
        var map = new RawIntMap<int>(10, AllocatorHandle.Temp);

        map.Set(5, 42);

        Assert.IsTrue(map.ContainsKey(5));
        Assert.AreEqual(42, map[5]);
        Assert.AreEqual(1, map.GetCount());
    }

    [TestInfo("RawIntMapTests_Set_ExistingKey_OverwritesValue")]
    public void Set_ExistingKey_OverwritesValue()
    {
        var map = new RawIntMap<int>(10, AllocatorHandle.Temp);
        map.Add(5, 42);

        map.Set(5, 99);

        Assert.IsTrue(map.ContainsKey(5));
        Assert.AreEqual(99, map[5]);
        Assert.AreEqual(1, map.GetCount());
    }

    [TestInfo("RawIntMapTests_Set_NegativeKey_ThrowsIndexOutOfRangeException")]
    public void Set_NegativeKey_ThrowsIndexOutOfRangeException()
    {
        var map = new RawIntMap<int>(10, AllocatorHandle.Temp);

        Assert.ThrowsException<KeyNotFoundException>(() => map.Set(-1, 42));
    }

    [TestInfo("RawIntMapTests_Set_KeyOutOfBounds_ThrowsIndexOutOfRangeException")]
    public void Set_KeyOutOfBounds_ThrowsIndexOutOfRangeException()
    {
        var map = new RawIntMap<int>(10, AllocatorHandle.Temp);

        Assert.ThrowsException<KeyNotFoundException>(() => map.Set(10, 42));
    }

    #endregion

    #region Indexer Tests

    [TestInfo("RawIntMapTests_Indexer_ValidKey_ReturnsValue")]
    public void Indexer_ValidKey_ReturnsValue()
    {
        var map = new RawIntMap<int>(10, AllocatorHandle.Temp);
        map.Add(5, 42);

        var result = map[5];

        Assert.AreEqual(42, result);
    }

    [TestInfo("RawIntMapTests_Indexer_ValidKey_CanModifyValue")]
    public void Indexer_ValidKey_CanModifyValue()
    {
        var map = new RawIntMap<TestValue>(10, AllocatorHandle.Temp);
        map.Add(5, new TestValue(42));

        map[5] = new TestValue(99);

        Assert.AreEqual(99, map[5].Value);
    }

    [TestInfo("RawIntMapTests_Indexer_KeyNotPresent_ThrowsKeyNotFoundException")]
    public void Indexer_KeyNotPresent_ThrowsKeyNotFoundException()
    {
        var map = new RawIntMap<int>(10, AllocatorHandle.Temp);

        Assert.ThrowsException<KeyNotFoundException>(() => _ = map[5]);
    }

    [TestInfo("RawIntMapTests_Indexer_NegativeKey_ThrowsIndexOutOfRangeException")]
    public void Indexer_NegativeKey_ThrowsIndexOutOfRangeException()
    {
        var map = new RawIntMap<int>(10, AllocatorHandle.Temp);

        Assert.ThrowsException<KeyNotFoundException>(() => _ = map[-1]);
    }

    [TestInfo("RawIntMapTests_Indexer_KeyOutOfBounds_ThrowsIndexOutOfRangeException")]
    public void Indexer_KeyOutOfBounds_ThrowsIndexOutOfRangeException()
    {
        var map = new RawIntMap<int>(10, AllocatorHandle.Temp);

        Assert.ThrowsException<KeyNotFoundException>(() => _ = map[10]);
    }

    #endregion

    #region TryGetValue Tests

    [TestInfo("RawIntMapTests_TryGetValue_ValidKey_ReturnsTrue")]
    public void TryGetValue_ValidKey_ReturnsTrue()
    {
        var map = new RawIntMap<int>(10, AllocatorHandle.Temp);
        map.Add(5, 42);

        bool result = map.TryGetValue(5, out int value);

        Assert.IsTrue(result);
        Assert.AreEqual(42, value);
    }

    [TestInfo("RawIntMapTests_TryGetValue_KeyNotPresent_ReturnsFalse")]
    public void TryGetValue_KeyNotPresent_ReturnsFalse()
    {
        var map = new RawIntMap<int>(10, AllocatorHandle.Temp);

        bool result = map.TryGetValue(5, out int value);

        Assert.IsFalse(result);
        Assert.AreEqual(0, value);
    }

    [TestInfo("RawIntMapTests_TryGetValue_NegativeKey_ReturnsFalse")]
    public void TryGetValue_NegativeKey_ReturnsFalse()
    {
        var map = new RawIntMap<int>(10, AllocatorHandle.Temp);

        bool result = map.TryGetValue(-1, out int value);

        Assert.IsFalse(result);
        Assert.AreEqual(0, value);
    }

    [TestInfo("RawIntMapTests_TryGetValue_KeyOutOfBounds_ReturnsFalse")]
    public void TryGetValue_KeyOutOfBounds_ReturnsFalse()
    {
        var map = new RawIntMap<int>(10, AllocatorHandle.Temp);

        bool result = map.TryGetValue(10, out int value);

        Assert.IsFalse(result);
        Assert.AreEqual(0, value);
    }

    #endregion

    #region ContainsKey Tests

    [TestInfo("RawIntMapTests_ContainsKey_ValidKey_ReturnsTrue")]
    public void ContainsKey_ValidKey_ReturnsTrue()
    {
        var map = new RawIntMap<int>(10, AllocatorHandle.Temp);
        map.Add(5, 42);

        Assert.IsTrue(map.ContainsKey(5));
    }

    [TestInfo("RawIntMapTests_ContainsKey_KeyNotPresent_ReturnsFalse")]
    public void ContainsKey_KeyNotPresent_ReturnsFalse()
    {
        var map = new RawIntMap<int>(10, AllocatorHandle.Temp);

        Assert.IsFalse(map.ContainsKey(5));
    }

    [TestInfo("RawIntMapTests_ContainsKey_NegativeKey_ReturnsFalse")]
    public void ContainsKey_NegativeKey_ReturnsFalse()
    {
        var map = new RawIntMap<int>(10, AllocatorHandle.Temp);

        Assert.IsFalse(map.ContainsKey(-1));
    }

    [TestInfo("RawIntMapTests_ContainsKey_KeyOutOfBounds_ReturnsFalse")]
    public void ContainsKey_KeyOutOfBounds_ReturnsFalse()
    {
        var map = new RawIntMap<int>(10, AllocatorHandle.Temp);

        Assert.IsFalse(map.ContainsKey(10));
    }

    #endregion

    #region Remove Tests

    [TestInfo("RawIntMapTests_Remove_ValidKey_RemovesKeyAndReturnsTrue")]
    public void Remove_ValidKey_RemovesKeyAndReturnsTrue()
    {
        var map = new RawIntMap<int>(10, AllocatorHandle.Temp);
        map.Add(5, 42);

        bool result = map.Remove(5);

        Assert.IsTrue(result);
        Assert.IsFalse(map.ContainsKey(5));
        Assert.AreEqual(0, map.GetCount());
    }

    [TestInfo("RawIntMapTests_Remove_KeyNotPresent_ReturnsFalse")]
    public void Remove_KeyNotPresent_ReturnsFalse()
    {
        var map = new RawIntMap<int>(10, AllocatorHandle.Temp);

        bool result = map.Remove(5);

        Assert.IsFalse(result);
    }

    [TestInfo("RawIntMapTests_Remove_NegativeKey_ReturnsFalse")]
    public void Remove_NegativeKey_ReturnsFalse()
    {
        var map = new RawIntMap<int>(10, AllocatorHandle.Temp);

        bool result = map.Remove(-1);

        Assert.IsFalse(result);
    }

    [TestInfo("RawIntMapTests_Remove_KeyOutOfBounds_ReturnsFalse")]
    public void Remove_KeyOutOfBounds_ReturnsFalse()
    {
        var map = new RawIntMap<int>(10, AllocatorHandle.Temp);

        bool result = map.Remove(10);

        Assert.IsFalse(result);
    }

    [TestInfo("RawIntMapTests_Remove_MultipleKeys_RemovesCorrectKeys")]
    public void Remove_MultipleKeys_RemovesCorrectKeys()
    {
        var map = new RawIntMap<int>(10, AllocatorHandle.Temp);
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

    [TestInfo("RawIntMapTests_Clear_EmptyMap_RemainsEmpty")]
    public void Clear_EmptyMap_RemainsEmpty()
    {
        var map = new RawIntMap<int>(10, AllocatorHandle.Temp);

        map.Clear();

        Assert.AreEqual(0, map.GetCount());
    }

    [TestInfo("RawIntMapTests_Clear_NonEmptyMap_RemovesAllEntries")]
    public void Clear_NonEmptyMap_RemovesAllEntries()
    {
        var map = new RawIntMap<int>(10, AllocatorHandle.Temp);
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

    [TestInfo("RawIntMapTests_GetCount_EmptyMap_ReturnsZero")]
    public void GetCount_EmptyMap_ReturnsZero()
    {
        var map = new RawIntMap<int>(10, AllocatorHandle.Temp);

        Assert.AreEqual(0, map.GetCount());
    }

    [TestInfo("RawIntMapTests_GetCount_WithEntries_ReturnsCorrectCount")]
    public void GetCount_WithEntries_ReturnsCorrectCount()
    {
        var map = new RawIntMap<int>(10, AllocatorHandle.Temp);
        map.Add(1, 10);
        map.Add(3, 30);
        map.Add(5, 50);

        Assert.AreEqual(3, map.GetCount());
    }

    [TestInfo("RawIntMapTests_GetCount_AfterRemoval_ReturnsCorrectCount")]
    public void GetCount_AfterRemoval_ReturnsCorrectCount()
    {
        var map = new RawIntMap<int>(10, AllocatorHandle.Temp);
        map.Add(1, 10);
        map.Add(3, 30);
        map.Add(5, 50);

        map.Remove(3);

        Assert.AreEqual(2, map.GetCount());
    }

    #endregion

    #region Count Property Tests

    [TestInfo("RawIntMapTests_Count_EmptyMap_ReturnsZero")]
    public void Count_EmptyMap_ReturnsZero()
    {
        var map = new RawIntMap<int>(10, AllocatorHandle.Temp);

        Assert.AreEqual(0, map.Count);
    }

    [TestInfo("RawIntMapTests_Count_AfterAdd_IncrementsCorrectly")]
    public void Count_AfterAdd_IncrementsCorrectly()
    {
        var map = new RawIntMap<int>(10, AllocatorHandle.Temp);

        Assert.AreEqual(0, map.Count);

        map.Add(0, 10);
        Assert.AreEqual(1, map.Count);

        map.Add(5, 50);
        Assert.AreEqual(2, map.Count);

        map.Add(9, 90);
        Assert.AreEqual(3, map.Count);
    }

    [TestInfo("RawIntMapTests_Count_AfterSet_OnNewKey_IncrementsCorrectly")]
    public void Count_AfterSet_OnNewKey_IncrementsCorrectly()
    {
        var map = new RawIntMap<int>(10, AllocatorHandle.Temp);

        Assert.AreEqual(0, map.Count);

        map.Set(3, 30);
        Assert.AreEqual(1, map.Count);

        map.Set(7, 70);
        Assert.AreEqual(2, map.Count);
    }

    [TestInfo("RawIntMapTests_Count_AfterSet_OnExistingKey_RemainsUnchanged")]
    public void Count_AfterSet_OnExistingKey_RemainsUnchanged()
    {
        var map = new RawIntMap<int>(10, AllocatorHandle.Temp);
        map.Add(5, 50);

        Assert.AreEqual(1, map.Count);

        map.Set(5, 99);
        Assert.AreEqual(1, map.Count);

        map.Set(5, 42);
        Assert.AreEqual(1, map.Count);
    }

    [TestInfo("RawIntMapTests_Count_AfterRemove_DecrementsCorrectly")]
    public void Count_AfterRemove_DecrementsCorrectly()
    {
        var map = new RawIntMap<int>(10, AllocatorHandle.Temp);
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

    [TestInfo("RawIntMapTests_Count_AfterRemove_NonExistentKey_RemainsUnchanged")]
    public void Count_AfterRemove_NonExistentKey_RemainsUnchanged()
    {
        var map = new RawIntMap<int>(10, AllocatorHandle.Temp);
        map.Add(5, 50);

        Assert.AreEqual(1, map.Count);

        map.Remove(3);
        Assert.AreEqual(1, map.Count);

        map.Remove(7);
        Assert.AreEqual(1, map.Count);
    }

    [TestInfo("RawIntMapTests_Count_AfterTryRemove_Success_DecrementsCorrectly")]
    public void Count_AfterTryRemove_Success_DecrementsCorrectly()
    {
        var map = new RawIntMap<int>(10, AllocatorHandle.Temp);
        map.Add(2, 20);
        map.Add(4, 40);
        map.Add(6, 60);

        Assert.AreEqual(3, map.Count);

        bool removed = map.TryRemove(4, out int value);
        Assert.IsTrue(removed);
        Assert.AreEqual(40, value);
        Assert.AreEqual(2, map.Count);
    }

    [TestInfo("RawIntMapTests_Count_AfterTryRemove_Failure_RemainsUnchanged")]
    public void Count_AfterTryRemove_Failure_RemainsUnchanged()
    {
        var map = new RawIntMap<int>(10, AllocatorHandle.Temp);
        map.Add(5, 50);

        Assert.AreEqual(1, map.Count);

        bool removed = map.TryRemove(3, out int value);
        Assert.IsFalse(removed);
        Assert.AreEqual(0, value);
        Assert.AreEqual(1, map.Count);
    }

    [TestInfo("RawIntMapTests_Count_AfterClear_ReturnsZero")]
    public void Count_AfterClear_ReturnsZero()
    {
        var map = new RawIntMap<int>(10, AllocatorHandle.Temp);
        map.Add(1, 10);
        map.Add(3, 30);
        map.Add(5, 50);

        Assert.AreEqual(3, map.Count);

        map.Clear();
        Assert.AreEqual(0, map.Count);
    }

    [TestInfo("RawIntMapTests_Count_AfterClear_EmptyMap_RemainsZero")]
    public void Count_AfterClear_EmptyMap_RemainsZero()
    {
        var map = new RawIntMap<int>(10, AllocatorHandle.Temp);

        Assert.AreEqual(0, map.Count);

        map.Clear();
        Assert.AreEqual(0, map.Count);
    }

    [TestInfo("RawIntMapTests_Count_ComplexModificationSequence_TracksCorrectly")]
    public void Count_ComplexModificationSequence_TracksCorrectly()
    {
        var map = new RawIntMap<int>(10, AllocatorHandle.Temp);

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

    [TestInfo("RawIntMapTests_Count_WithBoundaryKeys_TracksCorrectly")]
    public void Count_WithBoundaryKeys_TracksCorrectly()
    {
        var map = new RawIntMap<int>(10, AllocatorHandle.Temp);

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

    [TestInfo("RawIntMapTests_Count_ConsistentWithEnumerationCount")]
    public void Count_ConsistentWithEnumerationCount()
    {
        var map = new RawIntMap<int>(20, AllocatorHandle.Temp);

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

    [TestInfo("RawIntMapTests_Count_AfterFailedOperations_RemainsCorrect")]
    public void Count_AfterFailedOperations_RemainsCorrect()
    {
        var map = new RawIntMap<int>(5, AllocatorHandle.Temp);
        map.Add(2, 20);

        Assert.AreEqual(1, map.Count);

        // Try to add duplicate key (should fail but count unchanged)
        Assert.ThrowsException<ArgumentException>(() => map.Add(2, 99));
        Assert.AreEqual(1, map.Count);

        // Try operations with out-of-bounds keys (should fail but count unchanged)
        Assert.ThrowsException<KeyNotFoundException>(() => map.Add(-1, 10));
        Assert.AreEqual(1, map.Count);

        Assert.ThrowsException<KeyNotFoundException>(() => map.Add(5, 10));
        Assert.AreEqual(1, map.Count);

        Assert.ThrowsException<KeyNotFoundException>(() => map.Set(-1, 10));
        Assert.AreEqual(1, map.Count);

        Assert.ThrowsException<KeyNotFoundException>(() => map.Set(10, 10));
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

    [TestInfo("RawIntMapTests_GetEnumerator_EmptyMap_ReturnsEmptySequence")]
    public void GetEnumerator_EmptyMap_ReturnsEmptySequence()
    {
        var map = new RawIntMap<int>(10, AllocatorHandle.Temp);

        var items = map.ToList();

        Assert.AreEqual(0, items.Count);
    }

    [TestInfo("RawIntMapTests_GetEnumerator_WithEntries_ReturnsCorrectKeyValuePairs")]
    public void GetEnumerator_WithEntries_ReturnsCorrectKeyValuePairs()
    {
        var map = new RawIntMap<int>(10, AllocatorHandle.Temp);
        map.Add(1, 10);
        map.Add(3, 30);
        map.Add(7, 70);

        var items = map.ToList();

        Assert.AreEqual(3, items.Count);
        Assert.IsTrue(items.Contains(new KeyValuePair<int, int>(1, 10)));
        Assert.IsTrue(items.Contains(new KeyValuePair<int, int>(3, 30)));
        Assert.IsTrue(items.Contains(new KeyValuePair<int, int>(7, 70)));
    }

    [TestInfo("RawIntMapTests_Keys_EmptyMap_ReturnsEmptySequence")]
    public void Keys_EmptyMap_ReturnsEmptySequence()
    {
        var map = new RawIntMap<int>(10, AllocatorHandle.Temp);

        var keys = map.Keys.ToList();

        Assert.AreEqual(0, keys.Count);
    }

    [TestInfo("RawIntMapTests_Keys_WithEntries_ReturnsCorrectKeys")]
    public void Keys_WithEntries_ReturnsCorrectKeys()
    {
        var map = new RawIntMap<int>(10, AllocatorHandle.Temp);
        map.Add(1, 10);
        map.Add(3, 30);
        map.Add(7, 70);

        var keys = map.Keys.ToList();

        Assert.AreEqual(3, keys.Count);
        Assert.IsTrue(keys.Contains(1));
        Assert.IsTrue(keys.Contains(3));
        Assert.IsTrue(keys.Contains(7));
    }

    [TestInfo("RawIntMapTests_Values_EmptyMap_ReturnsEmptySequence")]
    public void Values_EmptyMap_ReturnsEmptySequence()
    {
        var map = new RawIntMap<int>(10, AllocatorHandle.Temp);
        var values = map.Values.ToList();

        Assert.AreEqual(0, values.Count);
    }

    [TestInfo("RawIntMapTests_Values_WithEntries_ReturnsCorrectValues")]
    public void Values_WithEntries_ReturnsCorrectValues()
    {
        var map = new RawIntMap<int>(10, AllocatorHandle.Temp);
        map.Add(1, 10);
        map.Add(3, 30);
        map.Add(7, 70);

        var values = map.Values.ToList();

        Assert.AreEqual(3, values.Count);
        Assert.IsTrue(values.Contains(10));
        Assert.IsTrue(values.Contains(30));
        Assert.IsTrue(values.Contains(70));
    }

    [TestInfo("RawIntMapTests_GetEnumeratorAt_ValidOffset_StartsFromCorrectPosition")]
    public void GetEnumeratorAt_ValidOffset_StartsFromCorrectPosition()
    {
        var map = new RawIntMap<int>(10, AllocatorHandle.Temp);
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

    [TestInfo("RawIntMapTests_StressTest_ManyOperations_MaintainsConsistency")]
    public void StressTest_ManyOperations_MaintainsConsistency()
    {
        var map = new RawIntMap<int>(1000, AllocatorHandle.Temp);

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

    [TestInfo("RawIntMapTests_LargeCapacity_WorksCorrectly")]
    public void LargeCapacity_WorksCorrectly()
    {
        var map = new RawIntMap<int>(10000, AllocatorHandle.Temp);

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

    [TestInfo("RawIntMapTests_EnumerationOrder_ConsistentAcrossMultipleCalls")]
    public void EnumerationOrder_ConsistentAcrossMultipleCalls()
    {
        var map = new RawIntMap<int>(20, AllocatorHandle.Temp);
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
