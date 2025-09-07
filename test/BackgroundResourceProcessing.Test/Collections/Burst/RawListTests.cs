using System;
using BackgroundResourceProcessing.Collections.Burst;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Unity.Collections;

namespace BackgroundResourceProcessing.Test.Collections.Burst;

[TestClass]
public sealed class RawListTest
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

    private struct MockDisposable : IDisposable, IEquatable<MockDisposable>
    {
        public int Value { get; set; }
        public bool IsDisposed { get; private set; }

        public MockDisposable(int value)
        {
            Value = value;
            IsDisposed = false;
        }

        public void Dispose()
        {
            IsDisposed = true;
        }

        public bool Equals(MockDisposable other) =>
            Value == other.Value && IsDisposed == other.IsDisposed;

        public override bool Equals(object obj) => obj is MockDisposable other && Equals(other);

        public override int GetHashCode() => 0;

        public static bool operator ==(MockDisposable left, MockDisposable right) =>
            left.Equals(right);

        public static bool operator !=(MockDisposable left, MockDisposable right) =>
            !left.Equals(right);
    }

    private struct NonDisposableValue : IEquatable<NonDisposableValue>
    {
        public int Value { get; set; }

        public NonDisposableValue(int value)
        {
            Value = value;
        }

        public bool Equals(NonDisposableValue other) => Value == other.Value;

        public override bool Equals(object obj) => obj is NonDisposableValue other && Equals(other);

        public override int GetHashCode() => Value.GetHashCode();
    }

    #region Constructor Tests

    [TestMethod]
    public void Constructor_WithAllocator_CreatesEmptyList()
    {
        using var list = new RawList<int>(Allocator.Temp);

        Assert.AreEqual(0, list.Count);
        Assert.IsTrue(list.Capacity >= 0);
        Assert.IsTrue(list.IsEmpty);
        Assert.AreEqual(Allocator.Temp, list.Allocator);
    }

    [TestMethod]
    public void Constructor_WithCapacityAndAllocator_CreatesListWithCapacity()
    {
        const int capacity = 10;
        using var list = new RawList<int>(capacity, Allocator.Temp);

        Assert.AreEqual(0, list.Count);
        Assert.IsTrue(list.Capacity >= capacity);
        Assert.IsTrue(list.IsEmpty);
        Assert.AreEqual(Allocator.Temp, list.Allocator);
    }

    [TestMethod]
    public void Constructor_WithNegativeCapacity_ThrowsArgumentOutOfRangeException()
    {
        Assert.ThrowsException<ArgumentOutOfRangeException>(() =>
        {
            using var list = new RawList<int>(-1, Allocator.Temp);
        });
    }

    #endregion

    #region Add Tests

    [TestMethod]
    public void Add_SingleItem_IncreasesCount()
    {
        using var list = new RawList<int>(Allocator.Temp);

        list.Add(42);

        Assert.AreEqual(1, list.Count);
        Assert.AreEqual(42, list[0]);
        Assert.IsFalse(list.IsEmpty);
    }

    [TestMethod]
    public void Add_MultipleItems_PreservesOrder()
    {
        using var list = new RawList<int>(Allocator.Temp);

        list.Add(1);
        list.Add(2);
        list.Add(3);

        Assert.AreEqual(3, list.Count);
        Assert.AreEqual(1, list[0]);
        Assert.AreEqual(2, list[1]);
        Assert.AreEqual(3, list[2]);
    }

    [TestMethod]
    public void Add_ExceedsCapacity_ExpandsAutomatically()
    {
        using var list = new RawList<int>(1, Allocator.Temp);
        var originalCapacity = list.Capacity;

        list.Add(1);
        list.Add(2); // This should trigger expansion

        Assert.AreEqual(2, list.Count);
        Assert.IsTrue(list.Capacity > originalCapacity);
        Assert.AreEqual(1, list[0]);
        Assert.AreEqual(2, list[1]);
    }

    #endregion

    #region AddRange Tests

    [TestMethod]
    public void AddRange_EmptySpan_DoesNotChangeList()
    {
        using var list = new RawList<int>(Allocator.Temp);

        list.AddRange(new int[] { });

        Assert.AreEqual(0, list.Count);
        Assert.IsTrue(list.IsEmpty);
    }

    [TestMethod]
    public void AddRange_ValidSpan_AddsAllElements()
    {
        using var list = new RawList<int>(Allocator.Temp);
        int[] values = [10, 20, 30];

        list.AddRange(values);

        Assert.AreEqual(3, list.Count);
        Assert.AreEqual(10, list[0]);
        Assert.AreEqual(20, list[1]);
        Assert.AreEqual(30, list[2]);
    }

    [TestMethod]
    public void AddRange_ExceedsCapacity_ExpandsAutomatically()
    {
        using var list = new RawList<int>(2, Allocator.Temp);
        int[] values = [1, 2, 3, 4, 5];

        list.AddRange(values);

        Assert.AreEqual(5, list.Count);
        Assert.IsTrue(list.Capacity >= 5);
        for (int i = 0; i < 5; i++)
        {
            Assert.AreEqual(i + 1, list[i]);
        }
    }

    #endregion

    #region Push/Pop Tests

    [TestMethod]
    public void Push_SingleItem_AddsItemToEnd()
    {
        using var list = new RawList<int>(Allocator.Temp);

        list.Push(42);

        Assert.AreEqual(1, list.Count);
        Assert.AreEqual(42, list[0]);
    }

    [TestMethod]
    public void Pop_EmptyList_ThrowsInvalidOperationException()
    {
        using var list = new RawList<int>(Allocator.Temp);

        Assert.ThrowsException<InvalidOperationException>(() => list.Pop());
    }

    [TestMethod]
    public void Pop_NonEmptyList_ReturnsLastItemAndReducesCount()
    {
        using var list = new RawList<int>(Allocator.Temp);
        list.Add(10);
        list.Add(20);
        list.Add(30);

        int result = list.Pop();

        Assert.AreEqual(30, result);
        Assert.AreEqual(2, list.Count);
    }

    [TestMethod]
    public void TryPop_EmptyList_ReturnsFalse()
    {
        using var list = new RawList<int>(Allocator.Temp);

        bool result = list.TryPop(out int item);

        Assert.IsFalse(result);
        Assert.AreEqual(default(int), item);
    }

    [TestMethod]
    public void TryPop_NonEmptyList_ReturnsLastItemAndReducesCount()
    {
        using var list = new RawList<int>(Allocator.Temp);
        list.Add(10);
        list.Add(20);
        list.Add(30);

        bool result = list.TryPop(out int item);

        Assert.IsTrue(result);
        Assert.AreEqual(30, item);
        Assert.AreEqual(2, list.Count);
    }

    #endregion

    #region Indexer Tests

    [TestMethod]
    public void Indexer_ValidIndex_ReturnsCorrectItem()
    {
        using var list = new RawList<int>(Allocator.Temp);
        list.Add(10);
        list.Add(20);

        Assert.AreEqual(10, list[0]);
        Assert.AreEqual(20, list[1]);
    }

    [TestMethod]
    public void Indexer_NegativeIndex_ThrowsIndexOutOfRangeException()
    {
        using var list = new RawList<int>(Allocator.Temp);
        list.Add(42);

        Assert.ThrowsException<IndexOutOfRangeException>(() => _ = list[-1]);
    }

    [TestMethod]
    public void Indexer_IndexOutOfBounds_ThrowsIndexOutOfRangeException()
    {
        using var list = new RawList<int>(Allocator.Temp);
        list.Add(42);

        Assert.ThrowsException<IndexOutOfRangeException>(() => _ = list[1]);
    }

    [TestMethod]
    public void Indexer_CanModifyExistingItem()
    {
        using var list = new RawList<int>(Allocator.Temp);
        list.Add(10);

        list[0] = 99;

        Assert.AreEqual(99, list[0]);
    }

    #endregion

    #region Clear Tests

    [TestMethod]
    public void Clear_EmptyList_RemainsEmpty()
    {
        using var list = new RawList<int>(Allocator.Temp);

        list.Clear();

        Assert.AreEqual(0, list.Count);
        Assert.IsTrue(list.IsEmpty);
    }

    [TestMethod]
    public void Clear_NonEmptyList_MakesListEmpty()
    {
        using var list = new RawList<int>(Allocator.Temp);
        list.Add(1);
        list.Add(2);
        list.Add(3);

        list.Clear();

        Assert.AreEqual(0, list.Count);
        Assert.IsTrue(list.IsEmpty);
    }

    [TestMethod]
    public void Clear_WithDisposableItems_DisposesAllItems()
    {
        using var list = new RawList<MockDisposable>(Allocator.Temp);
        list.Add(new MockDisposable(1));
        list.Add(new MockDisposable(2));
        list.Add(new MockDisposable(3));

        list.Clear();

        Assert.AreEqual(0, list.Count);
        Assert.IsTrue(list.IsEmpty);
        // Note: We can't easily verify disposal since ItemDisposer handles it internally
    }

    #endregion

    #region RemoveAt Tests

    [TestMethod]
    public void RemoveAt_ValidIndex_RemovesItemAndShiftsElements()
    {
        using var list = new RawList<int>(Allocator.Temp);
        list.Add(10);
        list.Add(20);
        list.Add(30);

        list.RemoveAt(1);

        Assert.AreEqual(2, list.Count);
        Assert.AreEqual(10, list[0]);
        Assert.AreEqual(30, list[1]);
    }

    [TestMethod]
    public void RemoveAt_LastIndex_RemovesItemWithoutShifting()
    {
        using var list = new RawList<int>(Allocator.Temp);
        list.Add(10);
        list.Add(20);
        list.Add(30);

        list.RemoveAt(2);

        Assert.AreEqual(2, list.Count);
        Assert.AreEqual(10, list[0]);
        Assert.AreEqual(20, list[1]);
    }

    [TestMethod]
    public void RemoveAt_NegativeIndex_ThrowsArgumentOutOfRangeException()
    {
        using var list = new RawList<int>(Allocator.Temp);
        list.Add(42);

        Assert.ThrowsException<ArgumentOutOfRangeException>(() => list.RemoveAt(-1));
    }

    [TestMethod]
    public void RemoveAt_IndexOutOfBounds_ThrowsArgumentOutOfRangeException()
    {
        using var list = new RawList<int>(Allocator.Temp);
        list.Add(42);

        Assert.ThrowsException<ArgumentOutOfRangeException>(() => list.RemoveAt(1));
    }

    [TestMethod]
    public void RemoveAt_WithDisposableItem_DisposesRemovedItem()
    {
        using var list = new RawList<MockDisposable>(Allocator.Temp);
        list.Add(new MockDisposable(10));
        list.Add(new MockDisposable(20));
        list.Add(new MockDisposable(30));

        list.RemoveAt(1);

        Assert.AreEqual(2, list.Count);
        Assert.AreEqual(10, list[0].Value);
        Assert.AreEqual(30, list[1].Value);
        // Note: ItemDisposer handles disposal internally
    }

    #endregion

    #region RemoveAtSwapBack Tests

    [TestMethod]
    public void RemoveAtSwapBack_ValidIndex_RemovesItemAndSwapsWithLast()
    {
        using var list = new RawList<int>(Allocator.Temp);
        list.Add(10);
        list.Add(20);
        list.Add(30);

        list.RemoveAtSwapBack(1);

        Assert.AreEqual(2, list.Count);
        Assert.AreEqual(10, list[0]);
        Assert.AreEqual(30, list[1]); // Last element moved to index 1
    }

    [TestMethod]
    public void RemoveAtSwapBack_LastIndex_RemovesItemWithoutSwapping()
    {
        using var list = new RawList<int>(Allocator.Temp);
        list.Add(10);
        list.Add(20);
        list.Add(30);

        list.RemoveAtSwapBack(2);

        Assert.AreEqual(2, list.Count);
        Assert.AreEqual(10, list[0]);
        Assert.AreEqual(20, list[1]);
    }

    [TestMethod]
    public void RemoveAtSwapBack_NegativeIndex_ThrowsArgumentOutOfRangeException()
    {
        using var list = new RawList<int>(Allocator.Temp);
        list.Add(42);

        Assert.ThrowsException<ArgumentOutOfRangeException>(() => list.RemoveAtSwapBack(-1));
    }

    [TestMethod]
    public void RemoveAtSwapBack_IndexOutOfBounds_ThrowsArgumentOutOfRangeException()
    {
        using var list = new RawList<int>(Allocator.Temp);
        list.Add(42);

        Assert.ThrowsException<ArgumentOutOfRangeException>(() => list.RemoveAtSwapBack(1));
    }

    #endregion

    #region Resize Tests

    [TestMethod]
    public void Resize_ToLargerSize_IncreasesCount()
    {
        using var list = new RawList<int>(Allocator.Temp);
        list.Add(10);
        list.Add(20);

        list.Resize(5);

        Assert.AreEqual(5, list.Count);
        Assert.AreEqual(10, list[0]);
        Assert.AreEqual(20, list[1]);
        Assert.AreEqual(0, list[2]); // Default initialized
        Assert.AreEqual(0, list[3]);
        Assert.AreEqual(0, list[4]);
    }

    [TestMethod]
    public void Resize_ToSmallerSize_DecreasesCount()
    {
        using var list = new RawList<int>(Allocator.Temp);
        list.Add(10);
        list.Add(20);
        list.Add(30);

        list.Resize(2);

        Assert.AreEqual(2, list.Count);
        Assert.AreEqual(10, list[0]);
        Assert.AreEqual(20, list[1]);
    }

    [TestMethod]
    public void Resize_ToZero_MakesListEmpty()
    {
        using var list = new RawList<int>(Allocator.Temp);
        list.Add(10);
        list.Add(20);

        list.Resize(0);

        Assert.AreEqual(0, list.Count);
        Assert.IsTrue(list.IsEmpty);
    }

    [TestMethod]
    public void Resize_WithNegativeSize_ThrowsArgumentOutOfRangeException()
    {
        using var list = new RawList<int>(Allocator.Temp);

        Assert.ThrowsException<ArgumentOutOfRangeException>(() => list.Resize(-1));
    }

    [TestMethod]
    public void Resize_WithUninitializedMemory_DoesNotClearNewElements()
    {
        using var list = new RawList<int>(Allocator.Temp);
        list.Add(10);

        list.Resize(3, NativeArrayOptions.UninitializedMemory);

        Assert.AreEqual(3, list.Count);
        Assert.AreEqual(10, list[0]);
        // Elements at indices 1 and 2 may contain uninitialized data
    }

    [TestMethod]
    public void Resize_WithDisposableItems_DisposesRemovedItems()
    {
        using var list = new RawList<MockDisposable>(Allocator.Temp);
        list.Add(new MockDisposable(10));
        list.Add(new MockDisposable(20));
        list.Add(new MockDisposable(30));

        list.Resize(1); // Should dispose items at indices 1 and 2

        Assert.AreEqual(1, list.Count);
        Assert.AreEqual(10, list[0].Value);
    }

    #endregion

    #region Truncate Tests

    [TestMethod]
    public void Truncate_ValidIndex_ReducesCountToIndex()
    {
        using var list = new RawList<int>(Allocator.Temp);
        list.Add(10);
        list.Add(20);
        list.Add(30);

        list.Truncate(2);

        Assert.AreEqual(2, list.Count);
        Assert.AreEqual(10, list[0]);
        Assert.AreEqual(20, list[1]);
    }

    [TestMethod]
    public void Truncate_ToZero_MakesListEmpty()
    {
        using var list = new RawList<int>(Allocator.Temp);
        list.Add(10);
        list.Add(20);

        list.Truncate(0);

        Assert.AreEqual(0, list.Count);
        Assert.IsTrue(list.IsEmpty);
    }

    [TestMethod]
    public void Truncate_BeyondCount_DoesNotChangeList()
    {
        using var list = new RawList<int>(Allocator.Temp);
        list.Add(10);
        list.Add(20);

        list.Truncate(5);

        Assert.AreEqual(2, list.Count);
        Assert.AreEqual(10, list[0]);
        Assert.AreEqual(20, list[1]);
    }

    [TestMethod]
    public void Truncate_NegativeIndex_ThrowsArgumentOutOfRangeException()
    {
        using var list = new RawList<int>(Allocator.Temp);

        Assert.ThrowsException<ArgumentOutOfRangeException>(() => list.Truncate(-1));
    }

    [TestMethod]
    public void Truncate_WithDisposableItems_DisposesRemovedItems()
    {
        using var list = new RawList<MockDisposable>(Allocator.Temp);
        list.Add(new MockDisposable(10));
        list.Add(new MockDisposable(20));
        list.Add(new MockDisposable(30));

        list.Truncate(1);

        Assert.AreEqual(1, list.Count);
        Assert.AreEqual(10, list[0].Value);
    }

    #endregion

    #region Reserve Tests

    [TestMethod]
    public void Reserve_ValidCapacity_IncreasesCapacity()
    {
        using var list = new RawList<int>(Allocator.Temp);
        var originalCapacity = list.Capacity;

        list.Reserve(100);

        Assert.IsTrue(list.Capacity >= 100);
        Assert.IsTrue(list.Capacity >= originalCapacity);
    }

    [TestMethod]
    public void Reserve_SmallerThanCurrentCapacity_DoesNotChangeCapacity()
    {
        using var list = new RawList<int>(10, Allocator.Temp);
        var originalCapacity = list.Capacity;

        list.Reserve(5);

        Assert.AreEqual(originalCapacity, list.Capacity);
    }

    [TestMethod]
    public void Reserve_NegativeCapacity_ThrowsArgumentOutOfRangeException()
    {
        using var list = new RawList<int>(Allocator.Temp);

        Assert.ThrowsException<ArgumentOutOfRangeException>(() => list.Reserve(-1));
    }

    [TestMethod]
    public void Reserve_PreservesExistingData()
    {
        using var list = new RawList<int>(Allocator.Temp);
        list.Add(10);
        list.Add(20);
        list.Add(30);

        list.Reserve(100);

        Assert.AreEqual(3, list.Count);
        Assert.AreEqual(10, list[0]);
        Assert.AreEqual(20, list[1]);
        Assert.AreEqual(30, list[2]);
    }

    #endregion

    #region Span Tests

    [TestMethod]
    public void Span_EmptyList_ReturnsEmptySpan()
    {
        using var list = new RawList<int>(Allocator.Temp);

        var span = list.Span;

        Assert.AreEqual(0, span.Length);
    }

    [TestMethod]
    public void Span_NonEmptyList_ReturnsCorrectSpan()
    {
        using var list = new RawList<int>(Allocator.Temp);
        list.Add(10);
        list.Add(20);
        list.Add(30);

        var span = list.Span;

        Assert.AreEqual(3, span.Length);
        Assert.AreEqual(10, span[0]);
        Assert.AreEqual(20, span[1]);
        Assert.AreEqual(30, span[2]);
    }

    [TestMethod]
    public void Span_CanModifyData()
    {
        using var list = new RawList<int>(Allocator.Temp);
        list.Add(10);
        list.Add(20);

        var span = list.Span;
        span[0] = 99;
        span[1] = 88;

        Assert.AreEqual(99, list[0]);
        Assert.AreEqual(88, list[1]);
    }

    #endregion

    #region Dispose Tests

    [TestMethod]
    public void Dispose_EmptyList_DoesNotThrow()
    {
        var list = new RawList<int>(Allocator.Temp);

        // Should not throw
        list.Dispose();
    }

    [TestMethod]
    public void Dispose_NonEmptyList_DisposesSuccessfully()
    {
        var list = new RawList<int>(Allocator.Temp);
        list.Add(10);
        list.Add(20);
        list.Add(30);

        // Should not throw
        list.Dispose();
    }

    [TestMethod]
    public void Dispose_WithDisposableItems_DisposesAllItems()
    {
        var list = new RawList<MockDisposable>(Allocator.Temp);
        list.Add(new MockDisposable(10));
        list.Add(new MockDisposable(20));
        list.Add(new MockDisposable(30));

        // Should dispose all items through ItemDisposer
        list.Dispose();
    }

    [TestMethod]
    public void Dispose_WithNonDisposableItems_DisposesSuccessfully()
    {
        var list = new RawList<NonDisposableValue>(Allocator.Temp);
        list.Add(new NonDisposableValue(10));
        list.Add(new NonDisposableValue(20));

        // Should not throw even with non-disposable items
        list.Dispose();
    }

    [TestMethod]
    public void Dispose_CalledTwice_DoesNotThrow()
    {
        var list = new RawList<int>(Allocator.Temp);
        list.Add(42);

        list.Dispose();
        // Should not throw on second disposal
        list.Dispose();
    }

    #endregion

    #region Ptr Tests

    [TestMethod]
    public void Ptr_EmptyList_ReturnsValidPointer()
    {
        using var list = new RawList<int>(Allocator.Temp);

        unsafe
        {
            var ptr = list.Ptr;
            // We can't make many assertions about null/non-null since it depends on internal allocation
            // Just verify it doesn't throw
        }
    }

    [TestMethod]
    public void Ptr_NonEmptyList_ReturnsValidPointer()
    {
        using var list = new RawList<int>(Allocator.Temp);
        list.Add(42);

        unsafe
        {
            var ptr = list.Ptr;
            Assert.IsTrue(ptr != null);
            Assert.AreEqual(42, *ptr);
        }
    }

    #endregion

    #region Edge Cases and Stress Tests

    [TestMethod]
    public void StressTest_ManyOperations_MaintainsConsistency()
    {
        using var list = new RawList<int>(Allocator.Temp);

        // Add many items
        for (int i = 0; i < 1000; i++)
        {
            list.Add(i);
        }

        Assert.AreEqual(1000, list.Count);

        // Remove every other item
        for (int i = 999; i >= 0; i -= 2)
        {
            list.RemoveAt(i);
        }

        Assert.AreEqual(500, list.Count);

        // Verify remaining items are correct
        for (int i = 0; i < 500; i++)
        {
            Assert.AreEqual(i * 2, list[i]);
        }
    }

    [TestMethod]
    public void LargeCapacityReservation_WorksCorrectly()
    {
        using var list = new RawList<int>(Allocator.Temp);

        list.Reserve(10000);

        Assert.IsTrue(list.Capacity >= 10000);
        Assert.AreEqual(0, list.Count);

        // Add items up to capacity
        for (int i = 0; i < 1000; i++)
        {
            list.Add(i);
        }

        Assert.AreEqual(1000, list.Count);
        Assert.IsTrue(list.Capacity >= 10000);
    }

    #endregion
}
