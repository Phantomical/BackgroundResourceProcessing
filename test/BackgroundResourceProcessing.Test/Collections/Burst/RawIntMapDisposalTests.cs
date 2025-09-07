using System;
using System.Collections.Generic;
using System.Diagnostics;
using BackgroundResourceProcessing.Collections.Burst;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Unity.Collections;
using static BackgroundResourceProcessing.Test.Collections.Burst.RawListDisposalTests;

namespace BackgroundResourceProcessing.Test.Collections.Burst;

/// <summary>
/// Comprehensive test suite for RawIntMap disposal behavior.
///
/// These tests validate that RawIntMap correctly disposes of disposable items according
/// to the following rules:
/// 1. Operations that should dispose items: Clear(), Remove(key), Set() on existing key,
///    Entry.Insert() on occupied slot, Dispose()
/// 2. Operations that return items to caller do NOT dispose them: TryRemove(), indexer access,
///    TryGetValue(), Entry.Value access
/// 3. The map maintains correct state after disposal operations
/// 4. Exception handling doesn't break disposal semantics
/// 5. Only Present entries are disposed, empty slots are ignored
/// </summary>
[TestClass]
public sealed class RawIntMapDisposalTests
{
    TestAllocator.TestGuard? guard = null;

    [TestInitialize]
    public void Setup()
    {
        TestTracker.Reset();
        guard = new TestAllocator.TestGuard();
    }

    [TestCleanup]
    public void Teardown()
    {
        guard?.Dispose();
        guard = null;
        TestTracker.AssertNoDuplicates();
        TestTracker.Reset();
    }

    #region Constructor/Disposal Tests

    [TestMethod]
    public void Dispose_WithDisposableItems_DisposesAllItemsInOrder()
    {
        using var map = new RawIntMap<TrackingDisposable>(10, Allocator.Temp);
        var items = new[]
        {
            new TrackingDisposable(0),
            new TrackingDisposable(1),
            new TrackingDisposable(2),
            new TrackingDisposable(3),
            new TrackingDisposable(4),
        };

        // Add items at sparse keys to test disposal order
        map.Add(1, items[0]);
        map.Add(3, items[1]);
        map.Add(5, items[2]);
        map.Add(7, items[3]);
        map.Add(9, items[4]);

        map.Dispose();

        // Verify map is in disposed state
        Assert.AreEqual(0, map.Capacity);

        // All items should be disposed in key order (1, 3, 5, 7, 9 -> ids 0, 1, 2, 3, 4)
        TestTracker.AssertDisposalCount(5);
        AssertUtils.SequenceEqual(new[] { 0, 1, 2, 3, 4 }, TestTracker.DisposalOrder.ToArray());
    }

    [TestMethod]
    public void Dispose_EmptyMap_NoDisposalCalls()
    {
        using var map = new RawIntMap<TrackingDisposable>(10, Allocator.Temp);
        map.Dispose();

        Assert.AreEqual(0, map.Capacity);
        TestTracker.AssertDisposalCount(0);
    }

    [TestMethod]
    public void Dispose_CalledTwice_DoesNotThrow()
    {
        var map = new RawIntMap<TrackingDisposable>(10, Allocator.Temp);
        var items = new[]
        {
            new TrackingDisposable(0),
            new TrackingDisposable(1),
            new TrackingDisposable(2),
        };

        map.Add(1, items[0]);
        map.Add(3, items[1]);
        map.Add(5, items[2]);

        map.Dispose();
        Assert.AreEqual(0, map.Capacity);

        // Second dispose should not throw
        map.Dispose();
        Assert.AreEqual(0, map.Capacity);

        // Each item should be disposed exactly once
        TestTracker.AssertDisposalCount(3);
        TestTracker.AssertNoDuplicates();
    }

    #endregion

    #region Operations That Should Dispose Items

    [TestMethod]
    public void Clear_WithDisposableItems_DisposesAllItems()
    {
        using var map = new RawIntMap<TrackingDisposable>(10, Allocator.Temp);

        // Add items at various keys (keys 2, 4, 6, 8 - all within capacity 10)
        for (int i = 1; i <= 4; i++)
        {
            map.Add(i * 2, new TrackingDisposable(i));
        }

        Assert.AreEqual(4, map.GetCount());

        // Clear should dispose all items
        map.Clear();

        Assert.AreEqual(0, map.GetCount());
        TestTracker.AssertDisposalCount(4);
    }

    [TestMethod]
    public void Remove_WithDisposableItems_DisposesRemovedItem()
    {
        using var map = new RawIntMap<TrackingDisposable>(10, Allocator.Temp);
        var items = new[]
        {
            new TrackingDisposable(0),
            new TrackingDisposable(1),
            new TrackingDisposable(2),
        };

        map.Add(1, items[0]);
        map.Add(3, items[1]);
        map.Add(5, items[2]);

        // Remove item at key 3
        bool result = map.Remove(3);

        Assert.IsTrue(result);
        Assert.AreEqual(2, map.GetCount());
        Assert.IsFalse(map.ContainsKey(3));

        // Only the removed item should be disposed
        TestTracker.AssertDisposalCount(1);
        TestTracker.AssertDisposed(1); // items[1] has id 1
        TestTracker.AssertNotDisposed(0);
        TestTracker.AssertNotDisposed(2);
    }

    [TestMethod]
    public void Set_OverwriteExisting_DisposesOldValue()
    {
        using var map = new RawIntMap<TrackingDisposable>(10, Allocator.Temp);
        var oldItem = new TrackingDisposable(100);
        var newItem = new TrackingDisposable(200);

        map.Add(5, oldItem);
        Assert.AreEqual(1, map.GetCount());

        // Set should dispose old value and store new one
        map.Set(5, newItem);

        Assert.AreEqual(1, map.GetCount());
        Assert.AreEqual(200, map[5].Id);

        // Old item should be disposed, new item should not
        TestTracker.AssertDisposalCount(1);
        TestTracker.AssertDisposed(100);
        TestTracker.AssertNotDisposed(200);
    }

    [TestMethod]
    public void Add_ToExistingKey_ThrowsException_NoDisposal()
    {
        using var map = new RawIntMap<TrackingDisposable>(10, Allocator.Temp);
        var existingItem = new TrackingDisposable(100);
        var duplicateItem = new TrackingDisposable(200);

        map.Add(5, existingItem);

        Assert.ThrowsException<ArgumentException>(() => map.Add(5, duplicateItem));

        Assert.AreEqual(1, map.GetCount());
        Assert.AreEqual(100, map[5].Id);

        // No disposal should occur when Add fails
        TestTracker.AssertDisposalCount(0);
        TestTracker.AssertNotDisposed(100);
        TestTracker.AssertNotDisposed(200);
    }

    #endregion

    #region Operations That Return Items to Caller (Should NOT Dispose)

    [TestMethod]
    public void TryRemove_WithDisposableItems_DoesNotDisposeItem()
    {
        using var map = new RawIntMap<TrackingDisposable>(10, Allocator.Temp);
        var items = new[]
        {
            new TrackingDisposable(0),
            new TrackingDisposable(1),
            new TrackingDisposable(2),
        };

        map.Add(1, items[0]);
        map.Add(3, items[1]);
        map.Add(5, items[2]);

        // TryRemove should return item without disposing it
        bool result = map.TryRemove(3, out var removedItem);

        Assert.IsTrue(result);
        Assert.AreEqual(1, removedItem.Id);
        Assert.AreEqual(2, map.GetCount());
        Assert.IsFalse(map.ContainsKey(3));

        // TryRemove should NOT dispose the returned item
        TestTracker.AssertDisposalCount(0);
        TestTracker.AssertNotDisposed(1);

        // Manual cleanup for test
        removedItem.Dispose();
    }

    [TestMethod]
    public void Indexer_AccessValue_DoesNotDisposeItem()
    {
        using var map = new RawIntMap<TrackingDisposable>(10, Allocator.Temp);
        var item = new TrackingDisposable(100);

        map.Add(5, item);

        // Access value via indexer
        var accessed = map[5];

        Assert.AreEqual(100, accessed.Id);
        Assert.AreEqual(1, map.GetCount());

        // Indexer access should not dispose anything
        TestTracker.AssertDisposalCount(0);
        TestTracker.AssertNotDisposed(100);
    }

    [TestMethod]
    public void TryGetValue_WithDisposableItems_DoesNotDisposeItem()
    {
        using var map = new RawIntMap<TrackingDisposable>(10, Allocator.Temp);
        var item = new TrackingDisposable(100);

        map.Add(5, item);

        // TryGetValue should not dispose the accessed item
        bool result = map.TryGetValue(5, out var value);

        Assert.IsTrue(result);
        Assert.AreEqual(100, value.Id);
        Assert.AreEqual(1, map.GetCount());

        TestTracker.AssertDisposalCount(0);
        TestTracker.AssertNotDisposed(100);
    }

    [TestMethod]
    public void EntryValue_Access_DoesNotDisposeItem()
    {
        using var map = new RawIntMap<TrackingDisposable>(10, Allocator.Temp);
        var item = new TrackingDisposable(100);

        map.Add(5, item);

        var entry = map.GetEntry(5);
        Assert.IsTrue(entry.HasValue);

        // Access value via Entry
        var accessed = entry.Value;

        Assert.AreEqual(100, accessed.Id);
        Assert.AreEqual(1, map.GetCount());

        // Entry.Value access should not dispose anything
        TestTracker.AssertDisposalCount(0);
        TestTracker.AssertNotDisposed(100);
    }

    #endregion

    #region Entry API Disposal Tests

    [TestMethod]
    public void EntryInsert_OverwriteExisting_DisposesOldValue()
    {
        using var map = new RawIntMap<TrackingDisposable>(10, Allocator.Temp);
        var oldItem = new TrackingDisposable(100);
        var newItem = new TrackingDisposable(200);

        map.Add(5, oldItem);
        var entry = map.GetEntry(5);

        Assert.IsTrue(entry.HasValue);
        Assert.AreEqual(100, entry.Value.Id);

        // Entry.Insert should dispose old value
        ref var inserted = ref entry.Insert(newItem);

        Assert.AreEqual(200, inserted.Id);
        Assert.AreEqual(200, map[5].Id);

        // Old item should be disposed, new item should not
        TestTracker.AssertDisposalCount(1);
        TestTracker.AssertDisposed(100);
        TestTracker.AssertNotDisposed(200);
    }

    [TestMethod]
    public void EntryInsert_EmptySlot_NoDisposal()
    {
        using var map = new RawIntMap<TrackingDisposable>(10, Allocator.Temp);
        var item = new TrackingDisposable(100);

        var entry = map.GetEntry(5);
        Assert.IsFalse(entry.HasValue);

        // Entry.Insert into empty slot should not dispose anything
        ref var inserted = ref entry.Insert(item);

        Assert.AreEqual(100, inserted.Id);
        Assert.AreEqual(1, map.GetCount());
        Assert.IsTrue(map.ContainsKey(5));

        TestTracker.AssertDisposalCount(0);
        TestTracker.AssertNotDisposed(100);
    }

    #endregion

    #region Exception Handling Tests

    [TestMethod]
    public void IndexerAccess_KeyNotFound_NoDisposalOnException()
    {
        using var map = new RawIntMap<TrackingDisposable>(10, Allocator.Temp);
        var item = new TrackingDisposable(100);

        map.Add(5, item);

        Assert.ThrowsException<KeyNotFoundException>(() => _ = map[7]);

        Assert.AreEqual(1, map.GetCount());

        // Exception should not cause disposal
        TestTracker.AssertDisposalCount(0);
        TestTracker.AssertNotDisposed(100);
    }

    [TestMethod]
    public void Remove_InvalidKey_NoDisposalOnException()
    {
        using var map = new RawIntMap<TrackingDisposable>(10, Allocator.Temp);
        var item = new TrackingDisposable(100);

        map.Add(5, item);

        // Remove non-existent key should return false, not dispose anything
        bool result = map.Remove(7);

        Assert.IsFalse(result);
        Assert.AreEqual(1, map.GetCount());

        TestTracker.AssertDisposalCount(0);
        TestTracker.AssertNotDisposed(100);
    }

    [TestMethod]
    public void EntryValue_KeyNotPresent_NoDisposalOnException()
    {
        using var map = new RawIntMap<TrackingDisposable>(10, Allocator.Temp);
        var item = new TrackingDisposable(100);

        map.Add(5, item);
        var entry = map.GetEntry(7); // Empty slot

        Assert.IsFalse(entry.HasValue);
        Assert.ThrowsException<KeyNotFoundException>(() => _ = entry.Value);

        Assert.AreEqual(1, map.GetCount());

        TestTracker.AssertDisposalCount(0);
        TestTracker.AssertNotDisposed(100);
    }

    #endregion

    #region Complex Scenario Tests

    [TestMethod]
    public void MixedOperations_StateConsistency()
    {
        using var map = new RawIntMap<TrackingDisposable>(20, Allocator.Temp);

        // Add items at various keys (0-9)
        for (int i = 0; i < 10; i++)
        {
            map.Add(i * 2, new TrackingDisposable(i));
        }
        Assert.AreEqual(10, map.GetCount());

        // Set overwrites existing key 4 (disposes item with id 2)
        map.Set(4, new TrackingDisposable(100));
        TestTracker.AssertDisposalCount(1);
        TestTracker.AssertDisposed(2);

        // Remove key 6 (disposes item with id 3)
        bool removeResult = map.Remove(6);
        Assert.IsTrue(removeResult);
        TestTracker.AssertDisposalCount(2);
        TestTracker.AssertDisposed(3);

        // TryRemove key 8 (does NOT dispose, returns to caller)
        bool tryRemoveResult = map.TryRemove(8, out var removedItem);
        Assert.IsTrue(tryRemoveResult);
        Assert.AreEqual(4, removedItem.Id);
        TestTracker.AssertDisposalCount(2); // Still only 2 disposals
        TestTracker.AssertNotDisposed(4);

        // Clear disposes all remaining items
        int remainingCount = map.GetCount();
        map.Clear();
        Assert.AreEqual(0, map.GetCount());

        // Verify final disposal count: 1 (Set) + 1 (Remove) + remainingCount (Clear) = total
        int expectedTotalDisposals = 2 + remainingCount;
        TestTracker.AssertDisposalCount(expectedTotalDisposals);

        // Manual cleanup of returned item
        removedItem.Dispose();
    }

    [TestMethod]
    public void CapacityVsCount_OnlyDisposesValidEntries()
    {
        using var map = new RawIntMap<TrackingDisposable>(100, Allocator.Temp); // Large capacity
        var items = new[]
        {
            new TrackingDisposable(0),
            new TrackingDisposable(1),
            new TrackingDisposable(2),
        };

        // Add only 3 items in a sparse pattern
        map.Add(10, items[0]);
        map.Add(50, items[1]);
        map.Add(90, items[2]);

        // Ensure Capacity >> Count
        Assert.AreEqual(100, map.Capacity);
        Assert.AreEqual(3, map.GetCount());

        map.Dispose();

        // Should dispose exactly Count items (3), not Capacity items (100)
        TestTracker.AssertDisposalCount(3);
        AssertUtils.SequenceEqual(new[] { 0, 1, 2 }, TestTracker.DisposalOrder.ToArray());
    }

    [TestMethod]
    public void SparseMap_DisposalAccuracy()
    {
        using var map = new RawIntMap<TrackingDisposable>(50, Allocator.Temp);

        // Create a sparse pattern: keys 5, 15, 25, 35, 45
        var sparseKeys = new[] { 5, 15, 25, 35, 45 };
        for (int i = 0; i < sparseKeys.Length; i++)
        {
            map.Add(sparseKeys[i], new TrackingDisposable(i * 10));
        }

        Assert.AreEqual(5, map.GetCount());

        // Remove middle items (keys 15, 35)
        map.Remove(15);
        map.Remove(35);

        TestTracker.AssertDisposalCount(2);
        TestTracker.AssertDisposed(10); // Item at key 15
        TestTracker.AssertDisposed(30); // Item at key 35

        // Clear remaining items
        map.Clear();

        // Total disposals: 2 (Remove calls) + 3 (Clear)
        TestTracker.AssertDisposalCount(5);
    }

    #endregion

    #region Non-Disposable Type Tests

    [TestMethod]
    public void NonDisposableTypes_NoDisposalInteraction()
    {
        using var intMap = new RawIntMap<int>(20, Allocator.Temp);
        using var valueMap = new RawIntMap<NonDisposableValue>(20, Allocator.Temp);

        // Add some items
        for (int i = 0; i < 10; i++)
        {
            intMap.Add(i * 2, i);
            valueMap.Add(i * 2, new NonDisposableValue(i));
        }

        // Perform all operations that would dispose items if they were disposable
        intMap.Remove(4);
        valueMap.Remove(4);

        intMap.Set(6, 999);
        valueMap.Set(6, new NonDisposableValue(999));

        intMap.Clear();
        valueMap.Clear();

        // Add items again
        for (int i = 0; i < 5; i++)
        {
            intMap.Add(i, i);
            valueMap.Add(i, new NonDisposableValue(i));
        }

        // Dispose maps
        intMap.Dispose();
        valueMap.Dispose();

        // Verify no disposal system interaction occurred
        TestTracker.AssertDisposalCount(0);

        // Verify ItemDisposer behavior
        Assert.IsFalse(ItemDisposer<int>.NeedsDispose, "int should not need disposal");
        Assert.IsFalse(
            ItemDisposer<NonDisposableValue>.NeedsDispose,
            "NonDisposableValue should not need disposal"
        );
    }

    #endregion

    #region Edge Cases

    [TestMethod]
    public void EnumerationDuringDisposal_StateConsistency()
    {
        using var map = new RawIntMap<TrackingDisposable>(20, Allocator.Temp);
        var items = new[]
        {
            new TrackingDisposable(0),
            new TrackingDisposable(1),
            new TrackingDisposable(2),
            new TrackingDisposable(3),
        };

        map.Add(2, items[0]);
        map.Add(6, items[1]);
        map.Add(10, items[2]);
        map.Add(14, items[3]);

        var keysBeforeRemoval = new List<int>();
        foreach (var kvp in map)
        {
            keysBeforeRemoval.Add(kvp.Key);
        }

        Assert.AreEqual(4, keysBeforeRemoval.Count);

        // Remove item at key 6
        map.Remove(6);

        var keysAfterRemoval = new List<int>();
        foreach (var kvp in map)
        {
            keysAfterRemoval.Add(kvp.Key);
        }

        Assert.AreEqual(3, keysAfterRemoval.Count);
        Assert.IsFalse(keysAfterRemoval.Contains(6));

        // Only the removed item should be disposed
        TestTracker.AssertDisposalCount(1);
        TestTracker.AssertDisposed(1); // items[1] has id 1
    }

    [TestMethod]
    public void ItemDisposer_BasicFunctionality()
    {
        TestTracker.Reset();

        // Verify ItemDisposer is working for TrackingDisposable
        Assert.IsTrue(
            ItemDisposer<TrackingDisposable>.NeedsDispose,
            "TrackingDisposable should need disposal"
        );

        // Test direct ItemDisposer usage
        var item = new TrackingDisposable(1);
        ItemDisposer<TrackingDisposable>.Dispose(ref item);

        TestTracker.AssertDisposalCount(1);
        TestTracker.AssertDisposed(1);
    }

    #endregion
}
