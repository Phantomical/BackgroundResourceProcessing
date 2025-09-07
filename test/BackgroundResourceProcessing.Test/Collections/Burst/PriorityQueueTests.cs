using BackgroundResourceProcessing.Collections.Burst;
using Unity.Collections;

namespace BackgroundResourceProcessing.Test.Collections.Burst;

[TestClass]
public class PriorityQueueTests
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
    public void Constructor_WithAllocator_CreatesEmptyQueue()
    {
        using var queue = new PriorityQueue<int>(Allocator.Temp);

        Assert.AreEqual(0, queue.Count);
        Assert.IsTrue(queue.IsEmpty);
        Assert.IsTrue(queue.Capacity >= 0);
    }

    [TestMethod]
    public void Constructor_WithCapacityAndAllocator_CreatesQueueWithCapacity()
    {
        const int capacity = 10;
        using var queue = new PriorityQueue<int>(capacity, Allocator.Temp);

        Assert.AreEqual(0, queue.Count);
        Assert.IsTrue(queue.IsEmpty);
        Assert.IsTrue(queue.Capacity >= capacity);
    }

    [TestMethod]
    public void Constructor_WithArrayAndAllocator_CreatesHeapifiedQueue()
    {
        int[] values = { 5, 2, 8, 1, 9, 3 };
        using var queue = new PriorityQueue<int>(values, Allocator.Temp);

        Assert.AreEqual(6, queue.Count);
        Assert.IsFalse(queue.IsEmpty);

        // Verify it's a min-heap by dequeuing all elements in sorted order
        var result = new List<int>();
        while (!queue.IsEmpty)
        {
            result.Add(queue.Dequeue());
        }

        CollectionAssert.AreEqual(new[] { 1, 2, 3, 5, 8, 9 }, result);
    }

    [TestMethod]
    public void Enqueue_SingleItem_IncreasesCount()
    {
        using var queue = new PriorityQueue<int>(Allocator.Temp);

        queue.Enqueue(42);

        Assert.AreEqual(1, queue.Count);
        Assert.IsFalse(queue.IsEmpty);
    }

    [TestMethod]
    public void Enqueue_MultipleItems_MaintainsMinHeapProperty()
    {
        using var queue = new PriorityQueue<int>(Allocator.Temp);

        queue.Enqueue(5);
        queue.Enqueue(2);
        queue.Enqueue(8);
        queue.Enqueue(1);
        queue.Enqueue(9);
        queue.Enqueue(3);

        Assert.AreEqual(6, queue.Count);

        // Verify dequeue order maintains min-heap property
        var result = new List<int>();
        while (!queue.IsEmpty)
        {
            result.Add(queue.Dequeue());
        }

        CollectionAssert.AreEqual(new[] { 1, 2, 3, 5, 8, 9 }, result);
    }

    [TestMethod]
    public void Enqueue_ExceedsCapacity_ExpandsAutomatically()
    {
        using var queue = new PriorityQueue<int>(2, Allocator.Temp);
        var initialCapacity = queue.Capacity;

        queue.Enqueue(3);
        queue.Enqueue(1);
        queue.Enqueue(4); // Should trigger expansion
        queue.Enqueue(2);

        Assert.AreEqual(4, queue.Count);
        Assert.IsTrue(queue.Capacity >= initialCapacity);
        Assert.AreEqual(1, queue.Peek()); // Min element should be at root
    }

    [TestMethod]
    public void Dequeue_EmptyQueue_ThrowsInvalidOperationException()
    {
        using var queue = new PriorityQueue<int>(Allocator.Temp);

        Assert.ThrowsException<InvalidOperationException>(() => queue.Dequeue());
    }

    [TestMethod]
    public void Dequeue_NonEmptyQueue_ReturnsMinimumElementAndReducesCount()
    {
        using var queue = new PriorityQueue<int>(Allocator.Temp);
        queue.Enqueue(5);
        queue.Enqueue(2);
        queue.Enqueue(8);
        queue.Enqueue(1);

        var result = queue.Dequeue();

        Assert.AreEqual(1, result);
        Assert.AreEqual(3, queue.Count);
        Assert.AreEqual(2, queue.Peek()); // Next minimum should now be at root
    }

    [TestMethod]
    public void TryDequeue_EmptyQueue_ReturnsFalse()
    {
        using var queue = new PriorityQueue<int>(Allocator.Temp);

        var result = queue.TryDequeue(out var item);

        Assert.IsFalse(result);
        Assert.AreEqual(default(int), item);
    }

    [TestMethod]
    public void TryDequeue_NonEmptyQueue_ReturnsMinimumElementAndReducesCount()
    {
        using var queue = new PriorityQueue<int>(Allocator.Temp);
        queue.Enqueue(5);
        queue.Enqueue(2);
        queue.Enqueue(8);
        queue.Enqueue(1);

        var result = queue.TryDequeue(out var item);

        Assert.IsTrue(result);
        Assert.AreEqual(1, item);
        Assert.AreEqual(3, queue.Count);
    }

    [TestMethod]
    public void Peek_EmptyQueue_ThrowsInvalidOperationException()
    {
        using var queue = new PriorityQueue<int>(Allocator.Temp);

        Assert.ThrowsException<InvalidOperationException>(() => queue.Peek());
    }

    [TestMethod]
    public void Peek_NonEmptyQueue_ReturnsMinimumElementWithoutRemoval()
    {
        using var queue = new PriorityQueue<int>(Allocator.Temp);
        queue.Enqueue(5);
        queue.Enqueue(2);
        queue.Enqueue(8);
        queue.Enqueue(1);

        var result = queue.Peek();

        Assert.AreEqual(1, result);
        Assert.AreEqual(4, queue.Count); // Count should remain unchanged

        // Peek again to ensure it's still there
        Assert.AreEqual(1, queue.Peek());
    }

    [TestMethod]
    public void TryPeek_EmptyQueue_ReturnsFalse()
    {
        using var queue = new PriorityQueue<int>(Allocator.Temp);

        var result = queue.TryPeek(out var item);

        Assert.IsFalse(result);
        Assert.AreEqual(default(int), item);
    }

    [TestMethod]
    public void TryPeek_NonEmptyQueue_ReturnsMinimumElementWithoutRemoval()
    {
        using var queue = new PriorityQueue<int>(Allocator.Temp);
        queue.Enqueue(5);
        queue.Enqueue(2);
        queue.Enqueue(8);
        queue.Enqueue(1);

        var result = queue.TryPeek(out var item);

        Assert.IsTrue(result);
        Assert.AreEqual(1, item);
        Assert.AreEqual(4, queue.Count);
    }

    [TestMethod]
    public void Count_EmptyQueue_ReturnsZero()
    {
        using var queue = new PriorityQueue<int>(Allocator.Temp);

        Assert.AreEqual(0, queue.Count);
    }

    [TestMethod]
    public void Count_AfterEnqueueOperations_ReturnsCorrectCount()
    {
        using var queue = new PriorityQueue<int>(Allocator.Temp);

        Assert.AreEqual(0, queue.Count);

        queue.Enqueue(1);
        Assert.AreEqual(1, queue.Count);

        queue.Enqueue(2);
        Assert.AreEqual(2, queue.Count);

        queue.Enqueue(3);
        Assert.AreEqual(3, queue.Count);
    }

    [TestMethod]
    public void Count_AfterDequeueOperations_ReturnsCorrectCount()
    {
        using var queue = new PriorityQueue<int>(Allocator.Temp);
        queue.Enqueue(3);
        queue.Enqueue(1);
        queue.Enqueue(2);

        Assert.AreEqual(3, queue.Count);

        queue.Dequeue();
        Assert.AreEqual(2, queue.Count);

        queue.Dequeue();
        Assert.AreEqual(1, queue.Count);

        queue.Dequeue();
        Assert.AreEqual(0, queue.Count);
    }

    [TestMethod]
    public void IsEmpty_EmptyQueue_ReturnsTrue()
    {
        using var queue = new PriorityQueue<int>(Allocator.Temp);

        Assert.IsTrue(queue.IsEmpty);
    }

    [TestMethod]
    public void IsEmpty_NonEmptyQueue_ReturnsFalse()
    {
        using var queue = new PriorityQueue<int>(Allocator.Temp);
        queue.Enqueue(42);

        Assert.IsFalse(queue.IsEmpty);
    }

    [TestMethod]
    public void IsEmpty_AfterDequeueingAllElements_ReturnsTrue()
    {
        using var queue = new PriorityQueue<int>(Allocator.Temp);
        queue.Enqueue(1);
        queue.Enqueue(2);

        queue.Dequeue();
        queue.Dequeue();

        Assert.IsTrue(queue.IsEmpty);
    }

    [TestMethod]
    public void Capacity_InitialCapacity_IsNonNegative()
    {
        using var queue = new PriorityQueue<int>(Allocator.Temp);

        Assert.IsTrue(queue.Capacity >= 0);
    }

    [TestMethod]
    public void Capacity_WithSpecifiedCapacity_IsAtLeastSpecified()
    {
        const int requestedCapacity = 10;
        using var queue = new PriorityQueue<int>(requestedCapacity, Allocator.Temp);

        Assert.IsTrue(queue.Capacity >= requestedCapacity);
    }

    [TestMethod]
    public void Allocator_ReturnsCorrectAllocator()
    {
        using var queue = new PriorityQueue<int>(Allocator.Temp);

        Assert.AreEqual(Allocator.Temp, queue.Allocator);
    }

    [TestMethod]
    public void Span_EmptyQueue_ReturnsEmptySpan()
    {
        using var queue = new PriorityQueue<int>(Allocator.Temp);

        var span = queue.Span;

        Assert.AreEqual(0, span.Length);
    }

    [TestMethod]
    public void Span_NonEmptyQueue_ReturnsCorrectSpan()
    {
        using var queue = new PriorityQueue<int>(Allocator.Temp);
        queue.Enqueue(5);
        queue.Enqueue(2);
        queue.Enqueue(8);
        queue.Enqueue(1);

        var span = queue.Span;

        Assert.AreEqual(4, span.Length);
        Assert.AreEqual(1, span[0]); // Root should be minimum
    }

    [TestMethod]
    public void Span_CanModifyData()
    {
        using var queue = new PriorityQueue<int>(Allocator.Temp);
        queue.Enqueue(5);
        queue.Enqueue(2);

        var span = queue.Span;
        span[0] = 10; // Modify the root directly (this breaks heap property, but tests span access)

        Assert.AreEqual(10, span[0]);
    }

    [TestMethod]
    public void Dispose_EmptyQueue_DoesNotThrow()
    {
        var queue = new PriorityQueue<int>(Allocator.Temp);

        queue.Dispose(); // Should not throw
    }

    [TestMethod]
    public void Dispose_NonEmptyQueue_DisposesSuccessfully()
    {
        var queue = new PriorityQueue<int>(Allocator.Temp);
        queue.Enqueue(1);
        queue.Enqueue(2);
        queue.Enqueue(3);

        queue.Dispose(); // Should not throw
    }

    [TestMethod]
    public void Dispose_CalledTwice_DoesNotThrow()
    {
        var queue = new PriorityQueue<int>(Allocator.Temp);
        queue.Enqueue(42);

        queue.Dispose();
        queue.Dispose(); // Should not throw on second call
    }

    [TestMethod]
    public void PriorityOrdering_WithCustomComparableStruct_WorksCorrectly()
    {
        using var queue = new PriorityQueue<Priority>(Allocator.Temp);

        queue.Enqueue(new Priority { Value = 5 });
        queue.Enqueue(new Priority { Value = 1 });
        queue.Enqueue(new Priority { Value = 3 });
        queue.Enqueue(new Priority { Value = 2 });
        queue.Enqueue(new Priority { Value = 4 });

        var results = new List<int>();
        while (!queue.IsEmpty)
        {
            results.Add(queue.Dequeue().Value);
        }

        CollectionAssert.AreEqual(new[] { 1, 2, 3, 4, 5 }, results);
    }

    [TestMethod]
    public void LargeDataSet_MaintainsCorrectOrdering()
    {
        using var queue = new PriorityQueue<int>(Allocator.Temp);
        const int count = 1000;
        var random = new Random(42); // Fixed seed for reproducible test

        // Enqueue random values
        var expectedValues = new List<int>();
        for (int i = 0; i < count; i++)
        {
            var value = random.Next(0, count);
            expectedValues.Add(value);
            queue.Enqueue(value);
        }

        expectedValues.Sort();

        // Dequeue all values and verify they come out in sorted order
        var actualValues = new List<int>();
        while (!queue.IsEmpty)
        {
            actualValues.Add(queue.Dequeue());
        }

        CollectionAssert.AreEqual(expectedValues, actualValues);
    }

    [TestMethod]
    public void StressTest_MixedOperations_MaintainsConsistency()
    {
        using var queue = new PriorityQueue<int>(Allocator.Temp);
        var random = new Random(123);
        var referenceList = new List<int>();

        for (int i = 0; i < 500; i++)
        {
            if (random.NextDouble() < 0.7 || referenceList.Count == 0) // 70% enqueue
            {
                var value = random.Next(0, 100);
                queue.Enqueue(value);
                referenceList.Add(value);
                referenceList.Sort();
            }
            else // 30% dequeue
            {
                var expectedMin = referenceList[0];
                referenceList.RemoveAt(0);

                var actualMin = queue.Dequeue();
                Assert.AreEqual(expectedMin, actualMin);
            }

            Assert.AreEqual(referenceList.Count, queue.Count);
            Assert.AreEqual(referenceList.Count == 0, queue.IsEmpty);

            if (!queue.IsEmpty)
            {
                Assert.AreEqual(referenceList[0], queue.Peek());
            }
        }
    }

    [TestMethod]
    public void HeapProperty_AfterMultipleOperations_IsAlwaysMaintained()
    {
        using var queue = new PriorityQueue<int>(Allocator.Temp);
        var random = new Random(456);

        // Add initial elements
        for (int i = 0; i < 20; i++)
        {
            queue.Enqueue(random.Next(0, 100));
        }

        // Perform mixed operations and verify heap property after each
        for (int i = 0; i < 50; i++)
        {
            if (random.NextDouble() < 0.5)
            {
                queue.Enqueue(random.Next(0, 100));
            }
            else if (!queue.IsEmpty)
            {
                queue.Dequeue();
            }

            // Verify heap property is maintained
            if (!queue.IsEmpty)
            {
                VerifyMinHeapProperty(queue.Span.ToArray());
            }
        }
    }

    private static void VerifyMinHeapProperty(int[] heap)
    {
        for (int i = 0; i < heap.Length; i++)
        {
            int leftChild = 2 * i + 1;
            int rightChild = 2 * i + 2;

            if (leftChild < heap.Length)
            {
                Assert.IsTrue(
                    heap[i] <= heap[leftChild],
                    $"Heap property violated: parent {heap[i]} > left child {heap[leftChild]} at indices {i}, {leftChild}"
                );
            }

            if (rightChild < heap.Length)
            {
                Assert.IsTrue(
                    heap[i] <= heap[rightChild],
                    $"Heap property violated: parent {heap[i]} > right child {heap[rightChild]} at indices {i}, {rightChild}"
                );
            }
        }
    }

    private struct Priority : IComparable<Priority>
    {
        public int Value;

        public int CompareTo(Priority other) => Value.CompareTo(other.Value);
    }
}
