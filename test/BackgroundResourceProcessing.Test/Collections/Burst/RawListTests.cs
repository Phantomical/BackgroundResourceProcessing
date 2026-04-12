using System;
using BackgroundResourceProcessing.Collections.Burst;
using KSP.Testing;
using Unity.Collections;

namespace BackgroundResourceProcessing.Test.Collections.Burst;

public sealed class RawListTest : BRPTestBase
{
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

    [TestInfo("RawListTest_Constructor_WithAllocator_CreatesEmptyList")]
    public void Constructor_WithAllocator_CreatesEmptyList()
    {
        var list = new RawList<int>(AllocatorHandle.Temp);

        Assert.AreEqual(0, list.Count);
        Assert.IsTrue(list.Capacity >= 0);
        Assert.IsTrue(list.IsEmpty);
    }

    [TestInfo("RawListTest_Constructor_WithCapacityAndAllocator_CreatesListWithCapacity")]
    public void Constructor_WithCapacityAndAllocator_CreatesListWithCapacity()
    {
        const int capacity = 10;
        var list = new RawList<int>(capacity, AllocatorHandle.Temp);

        Assert.AreEqual(0, list.Count);
        Assert.IsTrue(list.Capacity >= capacity);
        Assert.IsTrue(list.IsEmpty);
    }

    [TestInfo("RawListTest_Constructor_WithNegativeCapacity_ThrowsArgumentOutOfRangeException")]
    public void Constructor_WithNegativeCapacity_ThrowsArgumentOutOfRangeException()
    {
        Assert.ThrowsException<ArgumentOutOfRangeException>(() =>
        {
            var list = new RawList<int>(-1, AllocatorHandle.Temp);
        });
    }

    #endregion

    #region Add Tests

    [TestInfo("RawListTest_Add_SingleItem_IncreasesCount")]
    public void Add_SingleItem_IncreasesCount()
    {
        var list = new RawList<int>(AllocatorHandle.Temp);

        list.Add(42);

        Assert.AreEqual(1, list.Count);
        Assert.AreEqual(42, list[0]);
        Assert.IsFalse(list.IsEmpty);
    }

    [TestInfo("RawListTest_Add_MultipleItems_PreservesOrder")]
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

    [TestInfo("RawListTest_Add_ExceedsCapacity_ExpandsAutomatically")]
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

    [TestInfo("RawListTest_AddRange_EmptySpan_DoesNotChangeList")]
    public void AddRange_EmptySpan_DoesNotChangeList()
    {
        var list = new RawList<int>(AllocatorHandle.Temp);

        list.AddRange(new int[] { });

        Assert.AreEqual(0, list.Count);
        Assert.IsTrue(list.IsEmpty);
    }

    [TestInfo("RawListTest_AddRange_ValidSpan_AddsAllElements")]
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

    [TestInfo("RawListTest_AddRange_ExceedsCapacity_ExpandsAutomatically")]
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

    [TestInfo("RawListTest_Push_SingleItem_AddsItemToEnd")]
    public void Push_SingleItem_AddsItemToEnd()
    {
        var list = new RawList<int>(AllocatorHandle.Temp);

        list.Push(42);

        Assert.AreEqual(1, list.Count);
        Assert.AreEqual(42, list[0]);
    }

    [TestInfo("RawListTest_Pop_EmptyList_ThrowsInvalidOperationException")]
    public void Pop_EmptyList_ThrowsInvalidOperationException()
    {
        var list = new RawList<int>(AllocatorHandle.Temp);

        Assert.ThrowsException<InvalidOperationException>(() => list.Pop());
    }

    [TestInfo("RawListTest_Pop_NonEmptyList_ReturnsLastItemAndReducesCount")]
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

    [TestInfo("RawListTest_TryPop_EmptyList_ReturnsFalse")]
    public void TryPop_EmptyList_ReturnsFalse()
    {
        var list = new RawList<int>(AllocatorHandle.Temp);

        bool result = list.TryPop(out int item);

        Assert.IsFalse(result);
        Assert.AreEqual(default(int), item);
    }

    [TestInfo("RawListTest_TryPop_NonEmptyList_ReturnsLastItemAndReducesCount")]
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

    [TestInfo("RawListTest_Indexer_ValidIndex_ReturnsCorrectItem")]
    public void Indexer_ValidIndex_ReturnsCorrectItem()
    {
        var list = new RawList<int>(AllocatorHandle.Temp);
        list.Add(10);
        list.Add(20);

        Assert.AreEqual(10, list[0]);
        Assert.AreEqual(20, list[1]);
    }

    [TestInfo("RawListTest_Indexer_NegativeIndex_ThrowsIndexOutOfRangeException")]
    public void Indexer_NegativeIndex_ThrowsIndexOutOfRangeException()
    {
        var list = new RawList<int>(AllocatorHandle.Temp);
        list.Add(42);

        Assert.ThrowsException<IndexOutOfRangeException>(() => _ = list[-1]);
    }

    [TestInfo("RawListTest_Indexer_IndexOutOfBounds_ThrowsIndexOutOfRangeException")]
    public void Indexer_IndexOutOfBounds_ThrowsIndexOutOfRangeException()
    {
        var list = new RawList<int>(AllocatorHandle.Temp);
        list.Add(42);

        Assert.ThrowsException<IndexOutOfRangeException>(() => _ = list[1]);
    }

    [TestInfo("RawListTest_Indexer_CanModifyExistingItem")]
    public void Indexer_CanModifyExistingItem()
    {
        var list = new RawList<int>(AllocatorHandle.Temp);
        list.Add(10);

        list[0] = 99;

        Assert.AreEqual(99, list[0]);
    }

    #endregion

    #region Clear Tests

    [TestInfo("RawListTest_Clear_EmptyList_RemainsEmpty")]
    public void Clear_EmptyList_RemainsEmpty()
    {
        var list = new RawList<int>(AllocatorHandle.Temp);

        list.Clear();

        Assert.AreEqual(0, list.Count);
        Assert.IsTrue(list.IsEmpty);
    }

    [TestInfo("RawListTest_Clear_NonEmptyList_MakesListEmpty")]
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

    [TestInfo("RawListTest_RemoveAt_ValidIndex_RemovesItemAndShiftsElements")]
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

    [TestInfo("RawListTest_RemoveAt_LastIndex_RemovesItemWithoutShifting")]
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

    [TestInfo("RawListTest_RemoveAt_NegativeIndex_ThrowsIndexOutOfRangeException")]
    public void RemoveAt_NegativeIndex_ThrowsIndexOutOfRangeException()
    {
        var list = new RawList<int>(AllocatorHandle.Temp);
        list.Add(42);

        Assert.ThrowsException<IndexOutOfRangeException>(() => list.RemoveAt(-1));
    }

    [TestInfo("RawListTest_RemoveAt_IndexOutOfBounds_ThrowsIndexOutOfRangeException")]
    public void RemoveAt_IndexOutOfBounds_ThrowsIndexOutOfRangeException()
    {
        var list = new RawList<int>(AllocatorHandle.Temp);
        list.Add(42);

        Assert.ThrowsException<IndexOutOfRangeException>(() => list.RemoveAt(1));
    }

    #endregion

    #region RemoveAtSwapBack Tests

    [TestInfo("RawListTest_RemoveAtSwapBack_ValidIndex_RemovesItemAndSwapsWithLast")]
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

    [TestInfo("RawListTest_RemoveAtSwapBack_LastIndex_RemovesItemWithoutSwapping")]
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

    [TestInfo("RawListTest_RemoveAtSwapBack_NegativeIndex_ThrowsArgumentOutOfRangeException")]
    public void RemoveAtSwapBack_NegativeIndex_ThrowsArgumentOutOfRangeException()
    {
        var list = new RawList<int>(AllocatorHandle.Temp);
        list.Add(42);

        Assert.ThrowsException<IndexOutOfRangeException>(() => list.RemoveAtSwapBack(-1));
    }

    [TestInfo("RawListTest_RemoveAtSwapBack_IndexOutOfBounds_ThrowsArgumentOutOfRangeException")]
    public void RemoveAtSwapBack_IndexOutOfBounds_ThrowsArgumentOutOfRangeException()
    {
        var list = new RawList<int>(AllocatorHandle.Temp);
        list.Add(42);

        Assert.ThrowsException<IndexOutOfRangeException>(() => list.RemoveAtSwapBack(1));
    }

    #endregion

    #region Resize Tests

    [TestInfo("RawListTest_Resize_ToLargerSize_IncreasesCount")]
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

    [TestInfo("RawListTest_Resize_ToSmallerSize_DecreasesCount")]
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

    [TestInfo("RawListTest_Resize_ToZero_MakesListEmpty")]
    public void Resize_ToZero_MakesListEmpty()
    {
        var list = new RawList<int>(AllocatorHandle.Temp);
        list.Add(10);
        list.Add(20);

        list.Resize(0);

        Assert.AreEqual(0, list.Count);
        Assert.IsTrue(list.IsEmpty);
    }

    [TestInfo("RawListTest_Resize_WithNegativeSize_ThrowsArgumentOutOfRangeException")]
    public void Resize_WithNegativeSize_ThrowsArgumentOutOfRangeException()
    {
        var list = new RawList<int>(AllocatorHandle.Temp);

        Assert.ThrowsException<ArgumentOutOfRangeException>(() => list.Resize(-1));
    }

    [TestInfo("RawListTest_Resize_WithUninitializedMemory_DoesNotClearNewElements")]
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

    [TestInfo("RawListTest_Truncate_ValidIndex_ReducesCountToIndex")]
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

    [TestInfo("RawListTest_Truncate_ToZero_MakesListEmpty")]
    public void Truncate_ToZero_MakesListEmpty()
    {
        var list = new RawList<int>(AllocatorHandle.Temp);
        list.Add(10);
        list.Add(20);

        list.Truncate(0);

        Assert.AreEqual(0, list.Count);
        Assert.IsTrue(list.IsEmpty);
    }

    [TestInfo("RawListTest_Truncate_BeyondCount_DoesNotChangeList")]
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

    [TestInfo("RawListTest_Truncate_NegativeIndex_ThrowsArgumentOutOfRangeException")]
    public void Truncate_NegativeIndex_ThrowsArgumentOutOfRangeException()
    {
        var list = new RawList<int>(AllocatorHandle.Temp);

        Assert.ThrowsException<ArgumentOutOfRangeException>(() => list.Truncate(-1));
    }

    #endregion

    #region Reserve Tests

    [TestInfo("RawListTest_Reserve_ValidCapacity_IncreasesCapacity")]
    public void Reserve_ValidCapacity_IncreasesCapacity()
    {
        var list = new RawList<int>(AllocatorHandle.Temp);
        var originalCapacity = list.Capacity;

        list.Reserve(100);

        Assert.IsTrue(list.Capacity >= 100);
        Assert.IsTrue(list.Capacity >= originalCapacity);
    }

    [TestInfo("RawListTest_Reserve_SmallerThanCurrentCapacity_DoesNotChangeCapacity")]
    public void Reserve_SmallerThanCurrentCapacity_DoesNotChangeCapacity()
    {
        var list = new RawList<int>(10, AllocatorHandle.Temp);
        var originalCapacity = list.Capacity;

        list.Reserve(5);

        Assert.AreEqual(originalCapacity, list.Capacity);
    }

    [TestInfo("RawListTest_Reserve_NegativeCapacity_ThrowsArgumentOutOfRangeException")]
    public void Reserve_NegativeCapacity_ThrowsArgumentOutOfRangeException()
    {
        var list = new RawList<int>(AllocatorHandle.Temp);

        Assert.ThrowsException<ArgumentOutOfRangeException>(() => list.Reserve(-1));
    }

    [TestInfo("RawListTest_Reserve_PreservesExistingData")]
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

    [TestInfo("RawListTest_Span_EmptyList_ReturnsEmptySpan")]
    public void Span_EmptyList_ReturnsEmptySpan()
    {
        var list = new RawList<int>(AllocatorHandle.Temp);

        var span = list.Span;

        Assert.AreEqual(0, span.Length);
    }

    [TestInfo("RawListTest_Span_NonEmptyList_ReturnsCorrectSpan")]
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

    [TestInfo("RawListTest_Span_CanModifyData")]
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

    [TestInfo("RawListTest_Ptr_EmptyList_ReturnsValidPointer")]
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

    [TestInfo("RawListTest_Ptr_NonEmptyList_ReturnsValidPointer")]
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

    [TestInfo("RawListTest_StressTest_ManyOperations_MaintainsConsistency")]
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

    [TestInfo("RawListTest_LargeCapacityReservation_WorksCorrectly")]
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
