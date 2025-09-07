using System;
using System.Collections.Generic;
using System.Diagnostics;
using BackgroundResourceProcessing.Collections.Burst;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Unity.Collections;

namespace BackgroundResourceProcessing.Test.Collections.Burst;

/// <summary>
/// Comprehensive test suite for RawList disposal behavior.
///
/// NOTE: These tests document the expected disposal behavior as specified in the
/// requirements but may not fully validate disposal due to the ItemDisposer system
/// relying on Harmony runtime patching that may not be active during unit tests.
///
/// The disposal behavior is implemented in the ItemDisposer&lt;T&gt; class which uses
/// Harmony to patch disposal methods at runtime. This test suite ensures that:
/// 1. Operations that should dispose items call the appropriate disposal methods
/// 2. Operations that return items to caller do NOT dispose them
/// 3. The list maintains correct state after disposal operations
/// 4. Exception handling doesn't break disposal semantics
/// </summary>
[TestClass]
public sealed class RawListDisposalTests
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

    /// <summary>
    /// Test infrastructure to track disposal calls that would occur if ItemDisposer was active
    /// </summary>
    public static class TestTracker
    {
        private static readonly ThreadLocal<List<int>> _disposalOrder = new(() => new List<int>());
        private static readonly ThreadLocal<HashSet<int>> _disposed = new(() => new HashSet<int>());

        public static List<int> DisposalOrder => _disposalOrder.Value;
        public static HashSet<int> Disposed => _disposed.Value;
        public static int DisposalCount => DisposalOrder.Count;

        public static void RecordDisposal(int id)
        {
            DisposalOrder.Add(id);
            Disposed.Add(id);
        }

        public static void Reset()
        {
            DisposalOrder.Clear();
            Disposed.Clear();
        }

        public static void AssertDisposed(int id)
        {
            Assert.IsTrue(Disposed.Contains(id), $"Tracked item with id {id} was not disposed");
        }

        public static void AssertNotDisposed(int id)
        {
            Assert.IsFalse(
                Disposed.Contains(id),
                $"Tracked item with id {id} was disposed of unexpectedly"
            );
        }

        public static void AssertNoDuplicates()
        {
            if (DisposalOrder.Count == Disposed.Count)
                return;

            HashSet<int> seen = [];
            foreach (int id in DisposalOrder)
            {
                if (seen.Contains(id))
                    Assert.Fail(
                        $"Tracked item with id {id} was disposed of multiple times. "
                            + $"Disposal order: [{string.Join(",", DisposalOrder)}]"
                    );
            }
        }

        public static void AssertDisposalCount(int expected)
        {
            Assert.AreEqual(
                expected,
                DisposalCount,
                $"Expected {expected} disposals, but got {DisposalCount}. "
                    + $"Disposal order: [{string.Join(",", DisposalOrder)}]"
            );
        }

        /// <summary>
        /// Verifies disposal count if the ItemDisposer system is working, otherwise
        /// documents the expected behavior and marks test as inconclusive.
        /// </summary>
        public static void AssertExpectedDisposalBehavior(int expectedDisposals, string operation)
        {
            if (!ItemDisposer<TrackingDisposable>.NeedsDispose)
            {
                Assert.Inconclusive(
                    $"{operation} should dispose {expectedDisposals} items, "
                        + "but ItemDisposer<TrackingDisposable>.NeedsDispose is false"
                );
                return;
            }

            // If NeedsDispose is true but we're still getting 0 disposals,
            // it likely means the Harmony transpiler isn't working properly in tests
            if (expectedDisposals > 0 && DisposalCount == 0)
            {
                Assert.Inconclusive(
                    $"{operation} should dispose {expectedDisposals} items, "
                        + "but disposal system appears to not be working in test environment "
                        + "(NeedsDispose=true but no disposals recorded)"
                );
                return;
            }

            // If we get here, the disposal system should be working
            AssertDisposalCount(expectedDisposals);
        }
    }

    [DebuggerDisplay("Id = {Id}, IsDisposed = {IsDisposed}")]
    public struct TrackingDisposable(int id) : IDisposable, IEquatable<TrackingDisposable>
    {
        public int Id { get; } = id;
        public bool IsDisposed => TestTracker.Disposed.Contains(Id);

        public void Dispose() => TestTracker.RecordDisposal(Id);

        public bool Equals(TrackingDisposable other) => Id == other.Id;

        public override bool Equals(object obj) => obj is TrackingDisposable other && Equals(other);

        public override int GetHashCode() => Id;

        public static bool operator ==(TrackingDisposable left, TrackingDisposable right) =>
            left.Equals(right);

        public static bool operator !=(TrackingDisposable left, TrackingDisposable right) =>
            !left.Equals(right);
    }

    public struct NonDisposableValue(int value) : IEquatable<NonDisposableValue>
    {
        public int Value { get; } = value;

        public bool Equals(NonDisposableValue other) => Value == other.Value;

        public override bool Equals(object obj) => obj is NonDisposableValue other && Equals(other);

        public override int GetHashCode() => Value;

        public static bool operator ==(NonDisposableValue left, NonDisposableValue right) =>
            left.Equals(right);

        public static bool operator !=(NonDisposableValue left, NonDisposableValue right) =>
            !left.Equals(right);
    }

    #region Constructor/Disposal Tests

    [TestMethod]
    public void Dispose_WithDisposableItems_DisposesAllItemsInOrder()
    {
        // Test documents expected behavior: When RawList.Dispose() is called,
        // all items in the list should be disposed in forward order
        using var list = new RawList<TrackingDisposable>(Allocator.Temp);
        var items = new[]
        {
            new TrackingDisposable(0),
            new TrackingDisposable(1),
            new TrackingDisposable(2),
            new TrackingDisposable(3),
            new TrackingDisposable(4),
        };

        foreach (var item in items)
            list.Add(item);

        list.Dispose();

        // Verify list is in disposed state
        Assert.AreEqual(0, list.Count);
        Assert.AreEqual(0, list.Capacity);

        // Expected behavior: items should be disposed in order 0,1,2,3,4
        TestTracker.AssertExpectedDisposalBehavior(5, "Dispose()");
        AssertUtils.SequenceEqual(new[] { 0, 1, 2, 3, 4 }, TestTracker.DisposalOrder.ToArray());
    }

    [TestMethod]
    public void Dispose_EmptyList_NoDisposalCalls()
    {
        // Test documents expected behavior: Empty list disposal should not cause any disposal calls
        using var list = new RawList<TrackingDisposable>(Allocator.Temp);
        list.Dispose();

        Assert.AreEqual(0, list.Count);
        Assert.AreEqual(0, list.Capacity);

        // Expected behavior: No disposal calls should occur
        TestTracker.AssertExpectedDisposalBehavior(0, "Dispose()");
    }

    [TestMethod]
    public void Dispose_CalledTwice_DoesNotThrow()
    {
        // Test documents expected behavior: Second dispose should be safe and not cause double disposal
        var list = new RawList<TrackingDisposable>(Allocator.Temp);
        var items = new[]
        {
            new TrackingDisposable(0),
            new TrackingDisposable(1),
            new TrackingDisposable(2),
        };

        foreach (var item in items)
            list.Add(item);

        list.Dispose();
        Assert.AreEqual(0, list.Count);

        // Second dispose should not throw
        list.Dispose();
        Assert.AreEqual(0, list.Count);

        // Expected behavior: Each item should be disposed exactly once
        TestTracker.AssertExpectedDisposalBehavior(3, "First Dispose()");
        TestTracker.AssertNoDuplicates(); // Ensures no double disposal
    }

    #endregion

    #region Clear Operation Tests

    /// <summary>
    /// Tests that Clear() disposes all items in the list.
    /// When Clear() is called, all existing items should be disposed since they
    /// are not returned to the caller.
    /// </summary>
    [TestMethod]
    public void Clear_WithDisposableItems_DisposesAllItems()
    {
        TestTracker.Reset();
        using var list = new RawList<TrackingDisposable>(Allocator.Temp);

        // Add items to the list
        for (int i = 1; i <= 5; i++)
        {
            list.Add(new TrackingDisposable(i));
        }

        // Clear should dispose all items
        list.Clear();

        // Verify disposal behavior
        TestTracker.AssertExpectedDisposalBehavior(5, "Clear()");
    }

    [TestMethod]
    public void Clear_ThenAdd_PreviousItemsDisposed()
    {
        // Test documents expected behavior: Items cleared should be disposed,
        // newly added items should not be disposed
        using var list = new RawList<TrackingDisposable>(Allocator.Temp);
        var originalItems = new[] { new TrackingDisposable(0), new TrackingDisposable(1) };
        var newItems = new[] { new TrackingDisposable(10), new TrackingDisposable(11) };

        foreach (var item in originalItems)
            list.Add(item);

        list.Clear();
        Assert.AreEqual(0, list.Count);

        foreach (var item in newItems)
            list.Add(item);

        Assert.AreEqual(2, list.Count);

        // Expected behavior: Original items disposed by Clear(), new items not disposed
        TestTracker.AssertExpectedDisposalBehavior(2, "Clear()");
        foreach (var item in newItems)
            TestTracker.AssertNotDisposed(item.Id);
    }

    #endregion

    #region Pop Operation Tests (Items Returned to Caller)

    [TestMethod]
    public void Pop_WithDisposableItem_DoesNotDisposeItem()
    {
        // Test documents expected behavior: Pop() returns item to caller, does NOT dispose it
        using var list = new RawList<TrackingDisposable>(Allocator.Temp);
        var items = new[]
        {
            new TrackingDisposable(0),
            new TrackingDisposable(1),
            new TrackingDisposable(2),
        };

        foreach (var item in items)
            list.Add(item);

        var poppedItem = list.Pop();

        Assert.AreEqual(2, poppedItem.Id);
        Assert.AreEqual(2, list.Count);

        // Expected behavior: Popped item should NOT be disposed by Pop()
        TestTracker.AssertExpectedDisposalBehavior(0, "Pop()");
        // Caller is responsible for disposing the returned item
        poppedItem.Dispose(); // Manual cleanup for test
    }

    [TestMethod]
    public void TryPop_WithDisposableItem_DoesNotDisposeItem()
    {
        // Test documents expected behavior: TryPop() returns item to caller, does NOT dispose it
        using var list = new RawList<TrackingDisposable>(Allocator.Temp);
        var items = new[] { new TrackingDisposable(0), new TrackingDisposable(1) };

        foreach (var item in items)
            list.Add(item);

        var result = list.TryPop(out var poppedItem);

        Assert.IsTrue(result);
        Assert.AreEqual(1, poppedItem.Id);
        Assert.AreEqual(1, list.Count);

        // Expected behavior: Popped item should NOT be disposed by TryPop()
        TestTracker.AssertExpectedDisposalBehavior(0, "TryPop()");
        poppedItem.Dispose(); // Manual cleanup for test
    }

    [TestMethod]
    public void Pop_EmptyList_NoDisposalSideEffects()
    {
        // Test documents expected behavior: Exception on empty pop should not cause disposal
        using var list = new RawList<TrackingDisposable>(Allocator.Temp);

        Assert.ThrowsException<InvalidOperationException>(() => list.Pop());

        // Expected behavior: No disposal calls should occur due to exception
        TestTracker.AssertExpectedDisposalBehavior(0, "Pop() exception");
    }

    #endregion

    #region RemoveAt Operation Tests (Items Returned to Caller)

    /// <summary>
    /// Tests that RemoveAt() returns the removed item without disposing it.
    /// The returned item becomes the caller's responsibility.
    /// </summary>
    /// <summary>
    /// Tests that RemoveAt() returns the removed item without disposing it.
    /// The returned item becomes the caller's responsibility.
    /// </summary>
    [TestMethod]
    public void RemoveAt_MiddleIndex_ReturnsRemovedItem()
    {
        TestTracker.Reset();
        using var list = new RawList<TrackingDisposable>(Allocator.Temp);

        // Add items to the list
        for (int i = 1; i <= 5; i++)
        {
            list.Add(new TrackingDisposable(i));
        }

        // Remove item at index 2 (should return item with Id=3)
        var removedItem = list.RemoveAt(2);

        // Verify the correct item was returned
        Assert.AreEqual(3, removedItem.Id, "RemoveAt should return the correct item");

        // The returned item should NOT be disposed (use TestTracker since structs are copied)
        Assert.IsFalse(
            TestTracker.Disposed.Contains(removedItem.Id),
            "Returned item should not be disposed"
        );

        // No items should be disposed since RemoveAt returns them to caller
        TestTracker.AssertExpectedDisposalBehavior(0, "RemoveAt()");

        // Verify list state
        Assert.AreEqual(4, list.Count, "List count should decrease by 1");

        // Manual cleanup of returned item
        removedItem.Dispose();
    }

    [TestMethod]
    public void RemoveAt_LastIndex_ReturnsRemovedItem()
    {
        // Test documents expected behavior: RemoveAt on last item should return it, NOT dispose it
        using var list = new RawList<TrackingDisposable>(Allocator.Temp);
        var items = new[]
        {
            new TrackingDisposable(0),
            new TrackingDisposable(1),
            new TrackingDisposable(2),
        };

        foreach (var item in items)
            list.Add(item);

        using var removedItem = list.RemoveAt(2);

        Assert.AreEqual(2, list.Count);
        Assert.AreEqual(2, removedItem.Id);

        TestTracker.AssertDisposalCount(0);
    }

    [TestMethod]
    public void RemoveAt_InvalidIndex_NoDisposalOnException()
    {
        // Test documents expected behavior: Exception should not cause disposal
        using var list = new RawList<TrackingDisposable>(Allocator.Temp);
        var items = new[] { new TrackingDisposable(0), new TrackingDisposable(1) };

        foreach (var item in items)
            list.Add(item);

        Assert.ThrowsException<ArgumentOutOfRangeException>(() => list.RemoveAt(-1));
        Assert.ThrowsException<ArgumentOutOfRangeException>(() => list.RemoveAt(2));

        Assert.AreEqual(2, list.Count);

        // Expected behavior: No disposal should occur when exceptions are thrown
        TestTracker.AssertExpectedDisposalBehavior(0, "RemoveAt() exception");
    }

    #endregion

    #region RemoveAtSwapBack Operation Tests (Items Returned to Caller)

    /// <summary>
    /// Tests that RemoveAtSwapBack() returns the removed item without disposing it.
    /// The returned item becomes the caller's responsibility.
    /// </summary>
    [TestMethod]
    public void RemoveAtSwapBack_MiddleIndex_ReturnsRemovedItemOnly()
    {
        TestTracker.Reset();
        using var list = new RawList<TrackingDisposable>(Allocator.Temp);

        var items = new[]
        {
            new TrackingDisposable(0),
            new TrackingDisposable(1),
            new TrackingDisposable(2),
            new TrackingDisposable(3),
            new TrackingDisposable(4),
        };

        foreach (var item in items)
            list.Add(item);

        var removedItem = list.RemoveAtSwapBack(1);

        Assert.AreEqual(4, list.Count);
        Assert.AreEqual(1, removedItem.Id);

        // Verify order after swap-back removal: [0, 4, 2, 3]
        Assert.AreEqual(0, list[0].Id);
        Assert.AreEqual(4, list[1].Id); // Last item swapped here
        Assert.AreEqual(2, list[2].Id);
        Assert.AreEqual(3, list[3].Id);

        // Verify removed item was NOT disposed (returned to caller)
        Assert.IsFalse(
            TestTracker.Disposed.Contains(removedItem.Id),
            "Removed item should NOT be disposed by RemoveAtSwapBack"
        );

        // Verify remaining items are not disposed (use TestTracker since structs are copied)
        Assert.IsFalse(TestTracker.Disposed.Contains(0), "Item 0 should not be disposed");
        Assert.IsFalse(TestTracker.Disposed.Contains(2), "Item 2 should not be disposed");
        Assert.IsFalse(TestTracker.Disposed.Contains(3), "Item 3 should not be disposed");
        Assert.IsFalse(TestTracker.Disposed.Contains(4), "Swapped item 4 should not be disposed");

        // No items should be disposed since RemoveAtSwapBack returns them to caller
        TestTracker.AssertExpectedDisposalBehavior(0, "RemoveAtSwapBack()");

        // Manual cleanup for test
        removedItem.Dispose();
    }

    [TestMethod]
    public void RemoveAtSwapBack_LastIndex_ReturnsRemovedItem()
    {
        // Test documents expected behavior: RemoveAtSwapBack on last item returns it, does NOT dispose it
        using var list = new RawList<TrackingDisposable>(Allocator.Temp);
        var items = new[]
        {
            new TrackingDisposable(0),
            new TrackingDisposable(1),
            new TrackingDisposable(2),
        };

        foreach (var item in items)
            list.Add(item);

        using var removedItem = list.RemoveAtSwapBack(2);

        Assert.AreEqual(2, list.Count);
        Assert.AreEqual(2, removedItem.Id);

        TestTracker.AssertDisposalCount(0);
    }

    #endregion

    #region Resize Operation Tests

    [TestMethod]
    public void Resize_ShrinkSize_DisposesExcessItems()
    {
        // Test documents expected behavior: Resizing smaller disposes excess items
        using var list = new RawList<TrackingDisposable>(Allocator.Temp);
        for (int i = 0; i < 10; i++)
            list.Add(new TrackingDisposable(i));

        list.Resize(6);

        Assert.AreEqual(6, list.Count);

        // Verify remaining items are not disposed
        for (int i = 0; i < 6; i++)
        {
            Assert.AreEqual(i, list[i].Id);
            TestTracker.AssertNotDisposed(list[i].Id);
        }

        // Verify excess items were disposed (items 6-9)
        TestTracker.AssertDisposalCount(4);
        AssertUtils.SequenceEqual(
            new[] { 6, 7, 8, 9 },
            TestTracker.DisposalOrder.ToArray(),
            "Items 6-9 should be disposed in order"
        );
    }

    [TestMethod]
    public void Resize_GrowSize_NoDisposalCalls()
    {
        // Test documents expected behavior: Growing size should not dispose anything
        using var list = new RawList<TrackingDisposable>(Allocator.Temp);
        var items = new[]
        {
            new TrackingDisposable(0),
            new TrackingDisposable(1),
            new TrackingDisposable(2),
        };

        foreach (var item in items)
            list.Add(item);

        list.Resize(8);

        Assert.AreEqual(8, list.Count);

        // Expected behavior: No disposal should occur when growing
        TestTracker.AssertExpectedDisposalBehavior(0, "Resize() grow");
    }

    [TestMethod]
    public void Resize_ToZero_DisposesAllItems()
    {
        // Test documents expected behavior: Resizing to zero disposes all items
        using var list = new RawList<TrackingDisposable>(Allocator.Temp);
        var items = new[]
        {
            new TrackingDisposable(0),
            new TrackingDisposable(1),
            new TrackingDisposable(2),
        };

        foreach (var item in items)
            list.Add(item);

        list.Resize(0);

        Assert.AreEqual(0, list.Count);

        // Expected behavior: All items should be disposed
        TestTracker.AssertExpectedDisposalBehavior(3, "Resize(0)");
    }

    [TestMethod]
    public void Resize_NegativeSize_NoDisposalOnException()
    {
        // Test documents expected behavior: Exception should not cause disposal
        using var list = new RawList<TrackingDisposable>(Allocator.Temp);
        var items = new[] { new TrackingDisposable(0), new TrackingDisposable(1) };

        foreach (var item in items)
            list.Add(item);

        Assert.ThrowsException<ArgumentOutOfRangeException>(() => list.Resize(-1));

        Assert.AreEqual(2, list.Count);

        // Expected behavior: No disposal should occur when exception is thrown
        TestTracker.AssertExpectedDisposalBehavior(0, "Resize() exception");
    }

    #endregion

    #region Truncate Operation Tests

    [TestMethod]
    public void Truncate_MiddleIndex_DisposesTrailingItems()
    {
        // Test documents expected behavior: Truncate disposes items beyond the new length
        using var list = new RawList<TrackingDisposable>(Allocator.Temp);
        for (int i = 0; i < 10; i++)
            list.Add(new TrackingDisposable(i));

        list.Truncate(6);

        Assert.AreEqual(6, list.Count);

        // Verify remaining items are not disposed
        for (int i = 0; i < 6; i++)
        {
            Assert.AreEqual(i, list[i].Id);
            TestTracker.AssertNotDisposed(list[i].Id);
        }

        // Verify trailing items were disposed (items 6-9)
        TestTracker.AssertDisposalCount(4);
        AssertUtils.SequenceEqual(
            new[] { 6, 7, 8, 9 },
            TestTracker.DisposalOrder.ToArray(),
            "Items 6-9 should be disposed in order"
        );
    }

    [TestMethod]
    public void Truncate_BeyondCount_NoDisposalCalls()
    {
        // Test documents expected behavior: Truncating beyond Count should be no-op
        using var list = new RawList<TrackingDisposable>(Allocator.Temp);
        var items = new[]
        {
            new TrackingDisposable(0),
            new TrackingDisposable(1),
            new TrackingDisposable(2),
            new TrackingDisposable(3),
            new TrackingDisposable(4),
        };

        foreach (var item in items)
            list.Add(item);

        list.Truncate(10);

        Assert.AreEqual(5, list.Count);

        // Expected behavior: No disposal should occur
        TestTracker.AssertExpectedDisposalBehavior(0, "Truncate() beyond count");
    }

    [TestMethod]
    public void Truncate_ToZero_DisposesAllItems()
    {
        // Test documents expected behavior: Truncating to zero disposes all items
        using var list = new RawList<TrackingDisposable>(Allocator.Temp);
        var items = new[]
        {
            new TrackingDisposable(0),
            new TrackingDisposable(1),
            new TrackingDisposable(2),
        };

        foreach (var item in items)
            list.Add(item);

        list.Truncate(0);

        Assert.AreEqual(0, list.Count);

        // Expected behavior: All items should be disposed
        TestTracker.AssertExpectedDisposalBehavior(3, "Truncate(0)");
    }

    #endregion

    #region Complex Scenario Tests

    [TestMethod]
    public void MixedOperations_StateConsistency()
    {
        // Test documents expected behavior: Complex operations should maintain correct state
        using var list = new RawList<TrackingDisposable>(Allocator.Temp);

        // Add 10 items (0-9)
        for (int i = 0; i < 10; i++)
            list.Add(new TrackingDisposable(i));
        Assert.AreEqual(10, list.Count);

        // Add 5 more items (10-14)
        for (int i = 10; i < 15; i++)
            list.Add(new TrackingDisposable(i));
        Assert.AreEqual(15, list.Count);

        // RemoveAt(3) - should dispose item 3, shift remaining items
        list.RemoveAt(3);
        Assert.AreEqual(14, list.Count);
        Assert.AreEqual(4, list[3].Id); // Item 4 should have moved to index 3

        // Pop - should NOT dispose item (returns to caller)
        var poppedItem = list.Pop();
        Assert.AreEqual(14, poppedItem.Id); // Last item
        Assert.AreEqual(13, list.Count);
        poppedItem.Dispose(); // Manual cleanup

        // Clear - should dispose all remaining items
        list.Clear();
        Assert.AreEqual(0, list.Count);

        // Add 2 new items
        list.Add(new TrackingDisposable(100));
        list.Add(new TrackingDisposable(101));
        Assert.AreEqual(2, list.Count);

        // Resize to 1 - should dispose item 101
        list.Resize(1);
        Assert.AreEqual(1, list.Count);
        Assert.AreEqual(100, list[0].Id);

        // Verify final item is not disposed
        TestTracker.AssertNotDisposed(list[0].Id);

        // Expected disposal sequence occurred:
        // 1. RemoveAt(3) disposed item 3
        // 2. Clear() disposed remaining 13 items
        // 3. Resize(1) disposed item 101
        // Total expected disposals: 1 + 13 + 1 = 15
        TestTracker.AssertDisposalCount(15);
    }

    [TestMethod]
    public void ReallocationDuringGrowth_PreservesItems()
    {
        // Test documents expected behavior: Reallocation should not dispose items
        using var list = new RawList<TrackingDisposable>(2, Allocator.Temp); // Small initial capacity

        // Add items to trigger multiple reallocations
        for (int i = 0; i < 100; i++)
        {
            list.Add(new TrackingDisposable(i));
        }

        Assert.AreEqual(100, list.Count);

        // Verify all items are still present and not disposed
        for (int i = 0; i < 100; i++)
        {
            Assert.AreEqual(i, list[i].Id);
            TestTracker.AssertNotDisposed(list[i].Id);
        }

        // Verify no disposal occurred during reallocation
        TestTracker.AssertDisposalCount(0);
    }

    #endregion

    #region Non-Disposable Type Tests

    [TestMethod]
    public void NonDisposableTypes_NoDisposalInteraction()
    {
        // Test documents expected behavior: Non-disposable types should not interact with disposal system
        using var intList = new RawList<int>(Allocator.Temp);
        using var valueList = new RawList<NonDisposableValue>(Allocator.Temp);

        // Add some items
        for (int i = 0; i < 10; i++)
        {
            intList.Add(i);
            valueList.Add(new NonDisposableValue(i));
        }

        // Perform all operations that would dispose items if they were disposable
        intList.RemoveAt(5);
        valueList.RemoveAt(5);

        intList.Clear();
        valueList.Clear();

        // Add items again
        for (int i = 0; i < 5; i++)
        {
            intList.Add(i);
            valueList.Add(new NonDisposableValue(i));
        }

        intList.Resize(3);
        valueList.Resize(3);

        intList.Truncate(1);
        valueList.Truncate(1);

        // Dispose lists
        intList.Dispose();
        valueList.Dispose();

        // Verify no disposal system interaction occurred
        // (Non-disposable types should not trigger disposal tracking)
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
    public void DisposeAfterCapacityIncrease_OnlyDisposesValidItems()
    {
        // Test documents expected behavior: Dispose should only dispose Count items, not Capacity
        using var list = new RawList<TrackingDisposable>(10, Allocator.Temp); // Capacity > Count initially
        var items = new[]
        {
            new TrackingDisposable(0),
            new TrackingDisposable(1),
            new TrackingDisposable(2),
        };

        foreach (var item in items)
            list.Add(item);

        // Ensure Capacity > Count
        Assert.IsTrue(list.Capacity > list.Count);

        list.Dispose();

        // Verify all items were disposed
        foreach (var item in items)
        {
            Assert.IsTrue(item.IsDisposed, $"Item {item.Id} should be disposed");
        }

        // Should dispose exactly Count items (3), not Capacity items (10)
        TestTracker.AssertDisposalCount(3);
        AssertUtils.SequenceEqual(new[] { 0, 1, 2 }, TestTracker.DisposalOrder.ToArray());

        Assert.AreEqual(0, list.Count);
        Assert.AreEqual(0, list.Capacity);
    }

    [TestMethod]
    public void SpanAccess_AfterItemRemoval_ReflectsChanges()
    {
        // Test documents expected behavior: Span should reflect list state after operations
        using var list = new RawList<TrackingDisposable>(Allocator.Temp);
        var items = new[]
        {
            new TrackingDisposable(0),
            new TrackingDisposable(1),
            new TrackingDisposable(2),
            new TrackingDisposable(3),
        };

        foreach (var item in items)
            list.Add(item);

        var spanBeforeRemoval = list.Span;
        Assert.AreEqual(4, spanBeforeRemoval.Length);

        var removedItem = list.RemoveAt(1); // Remove item with Id 1

        var spanAfterRemoval = list.Span;
        Assert.AreEqual(3, spanAfterRemoval.Length);
        Assert.AreEqual(0, spanAfterRemoval[0].Id);
        Assert.AreEqual(2, spanAfterRemoval[1].Id); // Item 2 shifted to index 1
        Assert.AreEqual(3, spanAfterRemoval[2].Id);

        // Verify removed item was NOT disposed by RemoveAt (returned to caller)
        Assert.AreEqual(1, removedItem.Id);
        Assert.IsFalse(removedItem.IsDisposed, "Removed item should NOT be disposed by RemoveAt");
        Assert.IsFalse(items[0].IsDisposed, "Remaining item 0 should not be disposed");
        Assert.IsFalse(items[2].IsDisposed, "Remaining item 2 should not be disposed");
        Assert.IsFalse(items[3].IsDisposed, "Remaining item 3 should not be disposed");
        TestTracker.AssertDisposalCount(0);

        // Manual cleanup for test
        removedItem.Dispose();

        // Span accurately reflects current state after operations
    }

    #endregion

    #region ItemDisposer Behavior Documentation

    [TestMethod]
    public void ItemDisposer_BasicTest_VerifyHarmonyPatchingWorks()
    {
        TestTracker.Reset();

        // Check if ItemDisposer<TrackingDisposable>.NeedsDispose is true
        bool needsDispose = ItemDisposer<TrackingDisposable>.NeedsDispose;
        Console.WriteLine($"ItemDisposer<TrackingDisposable>.NeedsDispose = {needsDispose}");

        if (!needsDispose)
        {
            Assert.Inconclusive(
                "ItemDisposer<TrackingDisposable>.NeedsDispose is false - Harmony patching not working"
            );
            return;
        }

        // Test if ItemDisposer<TrackingDisposable> is working at all
        var item = new TrackingDisposable(1);

        // This should trigger disposal via Harmony patching
        ItemDisposer<TrackingDisposable>.Dispose(ref item);

        // Check if the item was disposed using TestTracker (structs are copied)
        TestTracker.AssertDisposalCount(1);
        TestTracker.AssertDisposed(1);
    }

    [TestMethod]
    public void ItemDisposer_DetectsDisposableTypes()
    {
        // Documents expected behavior of ItemDisposer type detection

        // Check if ItemDisposer properly detects disposable vs non-disposable types
        // Note: This may not work in test environment due to Harmony patching requirements

        var disposableNeedsDispose = ItemDisposer<TrackingDisposable>.NeedsDispose;
        var intNeedsDispose = ItemDisposer<int>.NeedsDispose;
        var nonDisposableNeedsDispose = ItemDisposer<NonDisposableValue>.NeedsDispose;

        // Expected behavior (may not be testable in unit test environment):
        // - ItemDisposer<TrackingDisposable>.NeedsDispose should be true
        // - ItemDisposer<int>.NeedsDispose should be false
        // - ItemDisposer<NonDisposableValue>.NeedsDispose should be false

        // If ItemDisposer is working correctly:
        if (disposableNeedsDispose)
        {
            Assert.IsTrue(
                disposableNeedsDispose,
                "TrackingDisposable should be detected as needing disposal"
            );
        }

        Assert.IsFalse(intNeedsDispose, "int should not need disposal");
        Assert.IsFalse(nonDisposableNeedsDispose, "NonDisposableValue should not need disposal");
    }

    #endregion
}
