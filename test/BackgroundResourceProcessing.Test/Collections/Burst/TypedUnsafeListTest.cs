using BackgroundResourceProcessing.Collections.Burst;
using Unity.Collections;

namespace BackgroundResourceProcessing.Test.Collections.Burst;

[TestClass]
public sealed class TypedUnsafeListTest
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

    [TestMethod]
    public void Constructor_WithAllocator_CreatesEmptyList()
    {
        using var list = new TypedUnsafeList<int>(Allocator.Temp);

        Assert.AreEqual(0, list.Count);
        Assert.AreEqual(Allocator.Temp, list.Allocator);
        Assert.IsTrue(list.Capacity >= 0);
    }

    [TestMethod]
    public void Constructor_WithCapacityAndAllocator_CreatesListWithCapacity()
    {
        const int capacity = 10;
        using var list = new TypedUnsafeList<int>(capacity, Allocator.Temp);

        Assert.AreEqual(0, list.Count);
        Assert.AreEqual(Allocator.Temp, list.Allocator);
        Assert.IsTrue(list.Capacity >= capacity);
    }

    [TestMethod]
    public void Add_SingleItem_IncreasesCount()
    {
        using var list = new TypedUnsafeList<int>(Allocator.Temp);

        list.Add(42);

        Assert.AreEqual(1, list.Count);
        Assert.AreEqual(42, list[0]);
    }

    [TestMethod]
    public void Add_MultipleItems_PreservesOrder()
    {
        using var list = new TypedUnsafeList<int>(Allocator.Temp);

        list.Add(1);
        list.Add(2);
        list.Add(3);

        Assert.AreEqual(3, list.Count);
        Assert.AreEqual(1, list[0]);
        Assert.AreEqual(2, list[1]);
        Assert.AreEqual(3, list[2]);
    }

    [TestMethod]
    public void Push_SingleItem_AddsItemToEnd()
    {
        using var list = new TypedUnsafeList<int>(Allocator.Temp);

        list.Push(42);

        Assert.AreEqual(1, list.Count);
        Assert.AreEqual(42, list[0]);
    }

    [TestMethod]
    public void Indexer_ValidIndex_ReturnsCorrectItem()
    {
        using var list = new TypedUnsafeList<int>(Allocator.Temp);
        list.Add(10);
        list.Add(20);

        Assert.AreEqual(10, list[0]);
        Assert.AreEqual(20, list[1]);
    }

    [TestMethod]
    public void Indexer_NegativeIndex_ThrowsException()
    {
        using var list = new TypedUnsafeList<int>(Allocator.Temp);
        list.Add(42);

        Assert.ThrowsException<IndexOutOfRangeException>(() => _ = list[-1]);
    }

    [TestMethod]
    public void Indexer_IndexOutOfBounds_ThrowsException()
    {
        using var list = new TypedUnsafeList<int>(Allocator.Temp);
        list.Add(42);

        Assert.ThrowsException<IndexOutOfRangeException>(() => _ = list[1]);
    }

    [TestMethod]
    public void TryPop_EmptyList_ReturnsFalse()
    {
        using var list = new TypedUnsafeList<int>(Allocator.Temp);

        bool result = list.TryPop(out int item);

        Assert.IsFalse(result);
        Assert.AreEqual(default(int), item);
    }

    [TestMethod]
    public void TryPop_NonEmptyList_ReturnsLastItemAndReducesCount()
    {
        using var list = new TypedUnsafeList<int>(Allocator.Temp);
        list.Add(10);
        list.Add(20);
        list.Add(30);

        bool result = list.TryPop(out int item);

        Assert.IsTrue(result);
        Assert.AreEqual(30, item);
        Assert.AreEqual(2, list.Count);
    }

    [TestMethod]
    public void RemoveAtSwapBack_ValidIndex_RemovesItemAndReturnsIt()
    {
        using var list = new TypedUnsafeList<int>(Allocator.Temp);
        list.Add(10);
        list.Add(20);
        list.Add(30);

        int removedItem = list.RemoveAtSwapBack(1);

        Assert.AreEqual(20, removedItem);
        Assert.AreEqual(2, list.Count);
        Assert.AreEqual(10, list[0]);
        Assert.AreEqual(30, list[1]);
    }

    [TestMethod]
    public void Remove_ExistingItem_RemovesFirstOccurrenceAndReturnsTrue()
    {
        using var list = new TypedUnsafeList<int>(Allocator.Temp);
        list.Add(10);
        list.Add(20);
        list.Add(10);

        bool result = list.Remove(10);

        Assert.IsTrue(result);
        Assert.AreEqual(2, list.Count);
        Assert.AreEqual(20, list[0]);
        Assert.AreEqual(10, list[1]);
    }

    [TestMethod]
    public void Remove_NonExistingItem_ReturnsFalse()
    {
        using var list = new TypedUnsafeList<int>(Allocator.Temp);
        list.Add(10);
        list.Add(20);

        bool result = list.Remove(30);

        Assert.IsFalse(result);
        Assert.AreEqual(2, list.Count);
    }

    [TestMethod]
    public void Clear_NonEmptyList_MakesListEmpty()
    {
        using var list = new TypedUnsafeList<int>(Allocator.Temp);
        list.Add(10);
        list.Add(20);

        list.Clear();

        Assert.AreEqual(0, list.Count);
    }

    [TestMethod]
    public void Contains_ExistingItem_ReturnsTrue()
    {
        using var list = new TypedUnsafeList<int>(Allocator.Temp);
        list.Add(10);
        list.Add(20);

        bool result = list.Contains(20);

        Assert.IsTrue(result);
    }

    [TestMethod]
    public void Contains_NonExistingItem_ReturnsFalse()
    {
        using var list = new TypedUnsafeList<int>(Allocator.Temp);
        list.Add(10);
        list.Add(20);

        bool result = list.Contains(30);

        Assert.IsFalse(result);
    }

    [TestMethod]
    public void IndexOf_ExistingItem_ReturnsCorrectIndex()
    {
        using var list = new TypedUnsafeList<int>(Allocator.Temp);
        list.Add(10);
        list.Add(20);
        list.Add(30);

        int index = list.IndexOf(20);

        Assert.AreEqual(1, index);
    }

    [TestMethod]
    public void IndexOf_NonExistingItem_ReturnsMinusOne()
    {
        using var list = new TypedUnsafeList<int>(Allocator.Temp);
        list.Add(10);
        list.Add(20);

        int index = list.IndexOf(30);

        Assert.AreEqual(-1, index);
    }

    [TestMethod]
    public void CopyTo_ValidArray_CopiesAllElements()
    {
        using var list = new TypedUnsafeList<int>(Allocator.Temp);
        list.Add(10);
        list.Add(20);
        list.Add(30);

        int[] array = new int[5];
        list.CopyTo(array, 1);

        Assert.AreEqual(0, array[0]);
        Assert.AreEqual(10, array[1]);
        Assert.AreEqual(20, array[2]);
        Assert.AreEqual(30, array[3]);
        Assert.AreEqual(0, array[4]);
    }

    [TestMethod]
    public void CopyTo_NullArray_ThrowsArgumentNullException()
    {
        using var list = new TypedUnsafeList<int>(Allocator.Temp);
        list.Add(10);

        Assert.ThrowsException<ArgumentNullException>(() => list.CopyTo(null, 0));
    }

    [TestMethod]
    public void CopyTo_NegativeIndex_ThrowsArgumentOutOfRangeException()
    {
        using var list = new TypedUnsafeList<int>(Allocator.Temp);
        list.Add(10);

        int[] array = new int[5];
        Assert.ThrowsException<ArgumentOutOfRangeException>(() => list.CopyTo(array, -1));
    }

    [TestMethod]
    public void CopyTo_InsufficientSpace_ThrowsArgumentException()
    {
        using var list = new TypedUnsafeList<int>(Allocator.Temp);
        list.Add(10);
        list.Add(20);
        list.Add(30);

        int[] array = new int[3];
        Assert.ThrowsException<ArgumentException>(() => list.CopyTo(array, 1));
    }

    [TestMethod]
    public void Resize_IncreaseSize_ExpandsList()
    {
        using var list = new TypedUnsafeList<int>(Allocator.Temp);
        list.Add(10);
        list.Add(20);

        list.Resize(5);

        Assert.AreEqual(5, list.Count);
        Assert.AreEqual(10, list[0]);
        Assert.AreEqual(20, list[1]);
        Assert.AreEqual(0, list[2]);
        Assert.AreEqual(0, list[3]);
        Assert.AreEqual(0, list[4]);
    }

    [TestMethod]
    public void Resize_DecreaseSize_TruncatesList()
    {
        using var list = new TypedUnsafeList<int>(Allocator.Temp);
        list.Add(10);
        list.Add(20);
        list.Add(30);

        list.Resize(2);

        Assert.AreEqual(2, list.Count);
        Assert.AreEqual(10, list[0]);
        Assert.AreEqual(20, list[1]);
    }

    [TestMethod]
    public void Clone_CreatesIndependentCopy()
    {
        using var original = new TypedUnsafeList<int>(Allocator.Temp);
        original.Add(10);
        original.Add(20);

        using var clone = original.Clone();

        Assert.AreEqual(original.Count, clone.Count);
        Assert.AreEqual(original[0], clone[0]);
        Assert.AreEqual(original[1], clone[1]);

        clone.Add(30);
        Assert.AreEqual(2, original.Count);
        Assert.AreEqual(3, clone.Count);
    }

    [TestMethod]
    public void Take_TransfersOwnership()
    {
        using var original = new TypedUnsafeList<int>(Allocator.Temp);
        original.Add(10);
        original.Add(20);

        using var taken = original.Take();

        Assert.AreEqual(0, original.Count);
        Assert.AreEqual(2, taken.Count);
        Assert.AreEqual(10, taken[0]);
        Assert.AreEqual(20, taken[1]);
    }

    [TestMethod]
    public void Span_Property_ReturnsCorrectSpan()
    {
        using var list = new TypedUnsafeList<int>(Allocator.Temp);
        list.Add(10);
        list.Add(20);

        var span = list.Span;

        Assert.AreEqual(2, span.Length);
        Assert.AreEqual(10, span[0]);
        Assert.AreEqual(20, span[1]);
    }

    [TestMethod]
    public void GetEnumerator_EmptyList_IteratesZeroTimes()
    {
        using var list = new TypedUnsafeList<int>(Allocator.Temp);

        int count = 0;
        foreach (var item in list)
        {
            count++;
        }

        Assert.AreEqual(0, count);
    }

    [TestMethod]
    public void GetEnumerator_NonEmptyList_IteratesOverAllItems()
    {
        using var list = new TypedUnsafeList<int>(Allocator.Temp);
        list.Add(10);
        list.Add(20);
        list.Add(30);

        int[] results = new int[3];
        int index = 0;
        foreach (var item in list)
        {
            results[index++] = item;
        }

        Assert.AreEqual(10, results[0]);
        Assert.AreEqual(20, results[1]);
        Assert.AreEqual(30, results[2]);
    }

    [TestMethod]
    public void IsReadOnly_ReturnsFalse()
    {
        using var list = new TypedUnsafeList<int>(Allocator.Temp);
        var collection = list as ICollection<int>;

        Assert.IsFalse(collection.IsReadOnly);
    }

    [TestMethod]
    public void IList_Insert_ThrowsNotSupportedException()
    {
        using var list = new TypedUnsafeList<int>(Allocator.Temp);
        var ilist = list as IList<int>;

        Assert.ThrowsException<NotSupportedException>(() => ilist.Insert(0, 42));
    }

    [TestMethod]
    public void IList_RemoveAt_ThrowsNotSupportedException()
    {
        using var list = new TypedUnsafeList<int>(Allocator.Temp);
        list.Add(42);
        var ilist = list as IList<int>;

        Assert.ThrowsException<NotSupportedException>(() => ilist.RemoveAt(0));
    }

    [TestMethod]
    public void IList_Indexer_GetAndSet_WorksCorrectly()
    {
        using var list = new TypedUnsafeList<int>(Allocator.Temp);
        list.Add(42);
        var ilist = list as IList<int>;

        Assert.AreEqual(42, ilist[0]);

        ilist[0] = 100;
        Assert.AreEqual(100, ilist[0]);
        Assert.AreEqual(100, list[0]);
    }
}
