using System;
using BackgroundResourceProcessing.Collections.Burst;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Unity.Collections;

namespace BackgroundResourceProcessing.Test.Collections.Burst;

[TestClass]
public sealed class RawListTest
{
    [TestCleanup]
    public void Cleanup()
    {
        TestAllocator.Cleanup();
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
        var list = new RawList<int>(AllocatorHandle.Temp);

        Assert.AreEqual(0, list.Count);
        Assert.IsTrue(list.Capacity >= 0);
        Assert.IsTrue(list.IsEmpty);
    }

    [TestMethod]
    public void Constructor_WithCapacityAndAllocator_CreatesListWithCapacity()
    {
        const int capacity = 10;
        var list = new RawList<int>(capacity, AllocatorHandle.Temp);

        Assert.AreEqual(0, list.Count);
        Assert.IsTrue(list.Capacity >= capacity);
        Assert.IsTrue(list.IsEmpty);
    }

    [TestMethod]
    public void Constructor_WithNegativeCapacity_ThrowsArgumentOutOfRangeException()
    {
        Assert.ThrowsException<ArgumentOutOfRangeException>(() =>
        {
            var list = new RawList<int>(-1, AllocatorHandle.Temp);
        });
    }

    #endregion

    #region Add Tests

    [TestMethod]
    public void Add_SingleItem_IncreasesCount()
    {
        var list = new RawList<int>(AllocatorHandle.Temp);

        list.Add(42);

        Assert.AreEqual(1, list.Count);
        Assert.AreEqual(42, list[0]);
        Assert.IsFalse(list.IsEmpty);
    }

    [TestMethod]
    public void Add_MultipleItems_PreservesOrder()
    {
        var list = new RawList<int>(AllocatorHandle.Temp);

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
        var list = new RawList<int>(1, AllocatorHandle.Temp);
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
        var list = new RawList<int>(AllocatorHandle.Temp);

        list.AddRange(new int[] { });

        Assert.AreEqual(0, list.Count);
        Assert.IsTrue(list.IsEmpty);
    }

    [TestMethod]
    public void AddRange_ValidSpan_AddsAllElements()
    {
        var list = new RawList<int>(AllocatorHandle.Temp);
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
        var list = new RawList<int>(2, AllocatorHandle.Temp);
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
        var list = new RawList<int>(AllocatorHandle.Temp);

        list.Push(42);

        Assert.AreEqual(1, list.Count);
        Assert.AreEqual(42, list[0]);
    }

    [TestMethod]
    public void Pop_EmptyList_ThrowsInvalidOperationException()
    {
        var list = new RawList<int>(AllocatorHandle.Temp);

        Assert.ThrowsException<InvalidOperationException>(() => list.Pop());
    }

    [TestMethod]
    public void Pop_NonEmptyList_ReturnsLastItemAndReducesCount()
    {
        var list = new RawList<int>(AllocatorHandle.Temp);
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
        var list = new RawList<int>(AllocatorHandle.Temp);

        bool result = list.TryPop(out int item);

        Assert.IsFalse(result);
        Assert.AreEqual(default(int), item);
    }

    [TestMethod]
    public void TryPop_NonEmptyList_ReturnsLastItemAndReducesCount()
    {
        var list = new RawList<int>(AllocatorHandle.Temp);
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
        var list = new RawList<int>(AllocatorHandle.Temp);
        list.Add(10);
        list.Add(20);

        Assert.AreEqual(10, list[0]);
        Assert.AreEqual(20, list[1]);
    }

    [TestMethod]
    public void Indexer_NegativeIndex_ThrowsIndexOutOfRangeException()
    {
        var list = new RawList<int>(AllocatorHandle.Temp);
        list.Add(42);

        Assert.ThrowsException<IndexOutOfRangeException>(() => _ = list[-1]);
    }

    [TestMethod]
    public void Indexer_IndexOutOfBounds_ThrowsIndexOutOfRangeException()
    {
        var list = new RawList<int>(AllocatorHandle.Temp);
        list.Add(42);

        Assert.ThrowsException<IndexOutOfRangeException>(() => _ = list[1]);
    }

    [TestMethod]
    public void Indexer_CanModifyExistingItem()
    {
        var list = new RawList<int>(AllocatorHandle.Temp);
        list.Add(10);

        list[0] = 99;

        Assert.AreEqual(99, list[0]);
    }

    #endregion

    #region Clear Tests

    [TestMethod]
    public void Clear_EmptyList_RemainsEmpty()
    {
        var list = new RawList<int>(AllocatorHandle.Temp);

        list.Clear();

        Assert.AreEqual(0, list.Count);
        Assert.IsTrue(list.IsEmpty);
    }

    [TestMethod]
    public void Clear_NonEmptyList_MakesListEmpty()
    {
        var list = new RawList<int>(AllocatorHandle.Temp);
        list.Add(1);
        list.Add(2);
        list.Add(3);

        list.Clear();

        Assert.AreEqual(0, list.Count);
        Assert.IsTrue(list.IsEmpty);
    }

    #endregion

    #region RemoveAt Tests

    [TestMethod]
    public void RemoveAt_ValidIndex_RemovesItemAndShiftsElements()
    {
        var list = new RawList<int>(AllocatorHandle.Temp);
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
        var list = new RawList<int>(AllocatorHandle.Temp);
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
        var list = new RawList<int>(AllocatorHandle.Temp);
        list.Add(42);

        Assert.ThrowsException<ArgumentOutOfRangeException>(() => list.RemoveAt(-1));
    }

    [TestMethod]
    public void RemoveAt_IndexOutOfBounds_ThrowsArgumentOutOfRangeException()
    {
        var list = new RawList<int>(AllocatorHandle.Temp);
        list.Add(42);

        Assert.ThrowsException<ArgumentOutOfRangeException>(() => list.RemoveAt(1));
    }

    #endregion

    #region RemoveAtSwapBack Tests

    [TestMethod]
    public void RemoveAtSwapBack_ValidIndex_RemovesItemAndSwapsWithLast()
    {
        var list = new RawList<int>(AllocatorHandle.Temp);
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
        var list = new RawList<int>(AllocatorHandle.Temp);
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
        var list = new RawList<int>(AllocatorHandle.Temp);
        list.Add(42);

        Assert.ThrowsException<ArgumentOutOfRangeException>(() => list.RemoveAtSwapBack(-1));
    }

    [TestMethod]
    public void RemoveAtSwapBack_IndexOutOfBounds_ThrowsArgumentOutOfRangeException()
    {
        var list = new RawList<int>(AllocatorHandle.Temp);
        list.Add(42);

        Assert.ThrowsException<ArgumentOutOfRangeException>(() => list.RemoveAtSwapBack(1));
    }

    #endregion

    #region Resize Tests

    [TestMethod]
    public void Resize_ToLargerSize_IncreasesCount()
    {
        var list = new RawList<int>(AllocatorHandle.Temp);
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
        var list = new RawList<int>(AllocatorHandle.Temp);
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
        var list = new RawList<int>(AllocatorHandle.Temp);
        list.Add(10);
        list.Add(20);

        list.Resize(0);

        Assert.AreEqual(0, list.Count);
        Assert.IsTrue(list.IsEmpty);
    }

    [TestMethod]
    public void Resize_WithNegativeSize_ThrowsArgumentOutOfRangeException()
    {
        var list = new RawList<int>(AllocatorHandle.Temp);

        Assert.ThrowsException<ArgumentOutOfRangeException>(() => list.Resize(-1));
    }

    [TestMethod]
    public void Resize_WithUninitializedMemory_DoesNotClearNewElements()
    {
        var list = new RawList<int>(AllocatorHandle.Temp);
        list.Add(10);

        list.Resize(3, NativeArrayOptions.UninitializedMemory);

        Assert.AreEqual(3, list.Count);
        Assert.AreEqual(10, list[0]);
        // Elements at indices 1 and 2 may contain uninitialized data
    }

    #endregion

    #region Truncate Tests

    [TestMethod]
    public void Truncate_ValidIndex_ReducesCountToIndex()
    {
        var list = new RawList<int>(AllocatorHandle.Temp);
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
        var list = new RawList<int>(AllocatorHandle.Temp);
        list.Add(10);
        list.Add(20);

        list.Truncate(0);

        Assert.AreEqual(0, list.Count);
        Assert.IsTrue(list.IsEmpty);
    }

    [TestMethod]
    public void Truncate_BeyondCount_DoesNotChangeList()
    {
        var list = new RawList<int>(AllocatorHandle.Temp);
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
        var list = new RawList<int>(AllocatorHandle.Temp);

        Assert.ThrowsException<ArgumentOutOfRangeException>(() => list.Truncate(-1));
    }

    #endregion

    #region Reserve Tests

    [TestMethod]
    public void Reserve_ValidCapacity_IncreasesCapacity()
    {
        var list = new RawList<int>(AllocatorHandle.Temp);
        var originalCapacity = list.Capacity;

        list.Reserve(100);

        Assert.IsTrue(list.Capacity >= 100);
        Assert.IsTrue(list.Capacity >= originalCapacity);
    }

    [TestMethod]
    public void Reserve_SmallerThanCurrentCapacity_DoesNotChangeCapacity()
    {
        var list = new RawList<int>(10, AllocatorHandle.Temp);
        var originalCapacity = list.Capacity;

        list.Reserve(5);

        Assert.AreEqual(originalCapacity, list.Capacity);
    }

    [TestMethod]
    public void Reserve_NegativeCapacity_ThrowsArgumentOutOfRangeException()
    {
        var list = new RawList<int>(AllocatorHandle.Temp);

        Assert.ThrowsException<ArgumentOutOfRangeException>(() => list.Reserve(-1));
    }

    [TestMethod]
    public void Reserve_PreservesExistingData()
    {
        var list = new RawList<int>(AllocatorHandle.Temp);
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
        var list = new RawList<int>(AllocatorHandle.Temp);

        var span = list.Span;

        Assert.AreEqual(0, span.Length);
    }

    [TestMethod]
    public void Span_NonEmptyList_ReturnsCorrectSpan()
    {
        var list = new RawList<int>(AllocatorHandle.Temp);
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
        var list = new RawList<int>(AllocatorHandle.Temp);
        list.Add(10);
        list.Add(20);

        var span = list.Span;
        span[0] = 99;
        span[1] = 88;

        Assert.AreEqual(99, list[0]);
        Assert.AreEqual(88, list[1]);
    }

    #endregion


    #region Ptr Tests

    [TestMethod]
    public void Ptr_EmptyList_ReturnsValidPointer()
    {
        var list = new RawList<int>(AllocatorHandle.Temp);

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
        var list = new RawList<int>(AllocatorHandle.Temp);
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
        var list = new RawList<int>(AllocatorHandle.Temp);

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
        var list = new RawList<int>(AllocatorHandle.Temp);

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
