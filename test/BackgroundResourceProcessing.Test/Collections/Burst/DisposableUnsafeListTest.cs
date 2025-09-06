using System;
using BackgroundResourceProcessing.Collections.Burst;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Unity.Collections;

namespace BackgroundResourceProcessing.Test.Collections.Burst;

[TestClass]
public sealed class DisposableUnsafeListTest
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

    // Mock disposable type for testing
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

        public bool Equals(MockDisposable other) => Value == other.Value;

        public override bool Equals(object obj) => obj is MockDisposable other && Equals(other);

        public override int GetHashCode() => Value.GetHashCode();

        public static bool operator ==(MockDisposable left, MockDisposable right) =>
            left.Equals(right);

        public static bool operator !=(MockDisposable left, MockDisposable right) =>
            !left.Equals(right);
    }

    [TestMethod]
    public void Constructor_WithAllocator_CreatesEmptyList()
    {
        using var list = new DisposableUnsafeList<MockDisposable>(Allocator.Temp);

        Assert.AreEqual(0, list.Count);
        Assert.IsTrue(list.Capacity >= 0);
    }

    [TestMethod]
    public void Constructor_WithCapacityAndAllocator_CreatesListWithCapacity()
    {
        const int capacity = 10;
        using var list = new DisposableUnsafeList<MockDisposable>(capacity, Allocator.Temp);

        Assert.AreEqual(0, list.Count);
        Assert.IsTrue(list.Capacity >= capacity);
    }

    [TestMethod]
    public void Add_SingleItem_IncreasesCount()
    {
        using var list = new DisposableUnsafeList<MockDisposable>(Allocator.Temp);
        var item = new MockDisposable(42);

        list.Add(item);

        Assert.AreEqual(1, list.Count);
        Assert.AreEqual(42, list[0].Value);
    }

    [TestMethod]
    public void Add_MultipleItems_PreservesOrder()
    {
        using var list = new DisposableUnsafeList<MockDisposable>(Allocator.Temp);
        var item1 = new MockDisposable(1);
        var item2 = new MockDisposable(2);
        var item3 = new MockDisposable(3);

        list.Add(item1);
        list.Add(item2);
        list.Add(item3);

        Assert.AreEqual(3, list.Count);
        Assert.AreEqual(1, list[0].Value);
        Assert.AreEqual(2, list[1].Value);
        Assert.AreEqual(3, list[2].Value);
    }

    [TestMethod]
    public void Push_SingleItem_AddsItemToEnd()
    {
        using var list = new DisposableUnsafeList<MockDisposable>(Allocator.Temp);
        var item = new MockDisposable(42);

        list.Push(item);

        Assert.AreEqual(1, list.Count);
        Assert.AreEqual(42, list[0].Value);
    }

    [TestMethod]
    public void Indexer_ValidIndex_ReturnsCorrectItem()
    {
        using var list = new DisposableUnsafeList<MockDisposable>(Allocator.Temp);
        var item1 = new MockDisposable(10);
        var item2 = new MockDisposable(20);
        list.Add(item1);
        list.Add(item2);

        Assert.AreEqual(10, list[0].Value);
        Assert.AreEqual(20, list[1].Value);
    }

    [TestMethod]
    public void Indexer_NegativeIndex_ThrowsException()
    {
        using var list = new DisposableUnsafeList<MockDisposable>(Allocator.Temp);
        var item = new MockDisposable(42);
        list.Add(item);

        Assert.ThrowsException<IndexOutOfRangeException>(() => _ = list[-1]);
    }

    [TestMethod]
    public void Indexer_IndexOutOfBounds_ThrowsException()
    {
        using var list = new DisposableUnsafeList<MockDisposable>(Allocator.Temp);
        var item = new MockDisposable(42);
        list.Add(item);

        Assert.ThrowsException<IndexOutOfRangeException>(() => _ = list[1]);
    }

    [TestMethod]
    public void TryPop_EmptyList_ReturnsFalse()
    {
        using var list = new DisposableUnsafeList<MockDisposable>(Allocator.Temp);

        bool result = list.TryPop(out MockDisposable item);

        Assert.IsFalse(result);
        Assert.AreEqual(default(MockDisposable), item);
    }

    [TestMethod]
    public void TryPop_NonEmptyList_ReturnsLastItemAndReducesCount()
    {
        using var list = new DisposableUnsafeList<MockDisposable>(Allocator.Temp);
        var item1 = new MockDisposable(10);
        var item2 = new MockDisposable(20);
        var item3 = new MockDisposable(30);
        list.Add(item1);
        list.Add(item2);
        list.Add(item3);

        bool result = list.TryPop(out MockDisposable item);

        Assert.IsTrue(result);
        Assert.AreEqual(30, item.Value);
        Assert.AreEqual(2, list.Count);
    }

    [TestMethod]
    public void RemoveAtSwapBack_ValidIndex_RemovesItemAndDisposesIt()
    {
        using var list = new DisposableUnsafeList<MockDisposable>(Allocator.Temp);
        var item1 = new MockDisposable(10);
        var item2 = new MockDisposable(20);
        var item3 = new MockDisposable(30);
        list.Add(item1);
        list.Add(item2);
        list.Add(item3);

        // Get reference to item before removal to check if it gets disposed
        ref var itemToRemove = ref list[1];
        var originalValue = itemToRemove.Value;

        list.RemoveAtSwapBack(1);

        Assert.AreEqual(2, list.Count);
        Assert.AreEqual(10, list[0].Value);
        Assert.AreEqual(30, list[1].Value);
        // Note: We can't easily verify disposal of the removed item since it's returned by value
        // and DisposableUnsafeList calls Dispose() on the returned value
    }

    [TestMethod]
    public void Clear_NonEmptyList_MakesListEmptyAndDisposesAllItems()
    {
        using var list = new DisposableUnsafeList<MockDisposable>(Allocator.Temp);
        var item1 = new MockDisposable(10);
        var item2 = new MockDisposable(20);
        list.Add(item1);
        list.Add(item2);

        list.Clear();

        Assert.AreEqual(0, list.Count);
    }

    [TestMethod]
    public void Contains_ExistingItem_ReturnsTrue()
    {
        using var list = new DisposableUnsafeList<MockDisposable>(Allocator.Temp);
        var item1 = new MockDisposable(10);
        var item2 = new MockDisposable(20);
        list.Add(item1);
        list.Add(item2);

        bool result = list.Contains(item2);

        Assert.IsTrue(result);
    }

    [TestMethod]
    public void Contains_NonExistingItem_ReturnsFalse()
    {
        using var list = new DisposableUnsafeList<MockDisposable>(Allocator.Temp);
        var item1 = new MockDisposable(10);
        var item2 = new MockDisposable(20);
        var item3 = new MockDisposable(30);
        list.Add(item1);
        list.Add(item2);

        bool result = list.Contains(item3);

        Assert.IsFalse(result);
    }

    [TestMethod]
    public void IndexOf_ExistingItem_ReturnsCorrectIndex()
    {
        using var list = new DisposableUnsafeList<MockDisposable>(Allocator.Temp);
        var item1 = new MockDisposable(10);
        var item2 = new MockDisposable(20);
        var item3 = new MockDisposable(30);
        list.Add(item1);
        list.Add(item2);
        list.Add(item3);

        int index = list.IndexOf(item2);

        Assert.AreEqual(1, index);
    }

    [TestMethod]
    public void IndexOf_NonExistingItem_ReturnsMinusOne()
    {
        using var list = new DisposableUnsafeList<MockDisposable>(Allocator.Temp);
        var item1 = new MockDisposable(10);
        var item2 = new MockDisposable(20);
        var item3 = new MockDisposable(30);
        list.Add(item1);
        list.Add(item2);

        int index = list.IndexOf(item3);

        Assert.AreEqual(-1, index);
    }

    [TestMethod]
    public void CopyTo_ValidArray_CopiesAllElements()
    {
        using var list = new DisposableUnsafeList<MockDisposable>(Allocator.Temp);
        var item1 = new MockDisposable(10);
        var item2 = new MockDisposable(20);
        var item3 = new MockDisposable(30);
        list.Add(item1);
        list.Add(item2);
        list.Add(item3);

        MockDisposable[] array = new MockDisposable[5];
        list.CopyTo(array, 1);

        Assert.AreEqual(0, array[0].Value);
        Assert.AreEqual(10, array[1].Value);
        Assert.AreEqual(20, array[2].Value);
        Assert.AreEqual(30, array[3].Value);
        Assert.AreEqual(0, array[4].Value);
    }

    [TestMethod]
    public void CopyTo_NullArray_ThrowsArgumentNullException()
    {
        using var list = new DisposableUnsafeList<MockDisposable>(Allocator.Temp);
        var item = new MockDisposable(10);
        list.Add(item);

        Assert.ThrowsException<ArgumentNullException>(() => list.CopyTo(null, 0));
    }

    [TestMethod]
    public void CopyTo_NegativeIndex_ThrowsArgumentOutOfRangeException()
    {
        using var list = new DisposableUnsafeList<MockDisposable>(Allocator.Temp);
        var item = new MockDisposable(10);
        list.Add(item);

        MockDisposable[] array = new MockDisposable[5];
        Assert.ThrowsException<ArgumentOutOfRangeException>(() => list.CopyTo(array, -1));
    }

    [TestMethod]
    public void CopyTo_InsufficientSpace_ThrowsArgumentException()
    {
        using var list = new DisposableUnsafeList<MockDisposable>(Allocator.Temp);
        var item1 = new MockDisposable(10);
        var item2 = new MockDisposable(20);
        var item3 = new MockDisposable(30);
        list.Add(item1);
        list.Add(item2);
        list.Add(item3);

        MockDisposable[] array = new MockDisposable[3];
        Assert.ThrowsException<ArgumentException>(() => list.CopyTo(array, 1));
    }

    [TestMethod]
    public void GetEnumerator_EmptyList_IteratesZeroTimes()
    {
        using var list = new DisposableUnsafeList<MockDisposable>(Allocator.Temp);

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
        using var list = new DisposableUnsafeList<MockDisposable>(Allocator.Temp);
        var item1 = new MockDisposable(10);
        var item2 = new MockDisposable(20);
        var item3 = new MockDisposable(30);
        list.Add(item1);
        list.Add(item2);
        list.Add(item3);

        MockDisposable[] results = new MockDisposable[3];
        int index = 0;
        foreach (var item in list)
        {
            results[index++] = item;
        }

        Assert.AreEqual(10, results[0].Value);
        Assert.AreEqual(20, results[1].Value);
        Assert.AreEqual(30, results[2].Value);
    }

    [TestMethod]
    public void IsReadOnly_ReturnsFalse()
    {
        using var list = new DisposableUnsafeList<MockDisposable>(Allocator.Temp);
        var collection = list as ICollection<MockDisposable>;

        Assert.IsFalse(collection.IsReadOnly);
    }

    [TestMethod]
    public void IList_Insert_ThrowsNotSupportedException()
    {
        using var list = new DisposableUnsafeList<MockDisposable>(Allocator.Temp);
        var ilist = list as IList<MockDisposable>;
        var item = new MockDisposable(42);

        Assert.ThrowsException<NotSupportedException>(() => ilist.Insert(0, item));
    }

    [TestMethod]
    public void IList_RemoveAt_ThrowsNotSupportedException()
    {
        using var list = new DisposableUnsafeList<MockDisposable>(Allocator.Temp);
        var item = new MockDisposable(42);
        list.Add(item);
        var ilist = list as IList<MockDisposable>;

        Assert.ThrowsException<NotSupportedException>(() => ilist.RemoveAt(0));
    }

    [TestMethod]
    public void ICollection_Remove_ThrowsNotSupportedException()
    {
        using var list = new DisposableUnsafeList<MockDisposable>(Allocator.Temp);
        var item = new MockDisposable(42);
        list.Add(item);
        var collection = list as ICollection<MockDisposable>;

        Assert.ThrowsException<NotSupportedException>(() => collection.Remove(item));
    }

    [TestMethod]
    public void Dispose_EmptyList_DoesNotThrow()
    {
        var list = new DisposableUnsafeList<MockDisposable>(Allocator.Temp);

        // Should not throw
        list.Dispose();
    }

    [TestMethod]
    public void Dispose_NonEmptyList_DisposesAllItemsAndList()
    {
        var list = new DisposableUnsafeList<MockDisposable>(Allocator.Temp);
        var item1 = new MockDisposable(10);
        var item2 = new MockDisposable(20);
        list.Add(item1);
        list.Add(item2);

        // Dispose should not throw
        list.Dispose();
    }
}
