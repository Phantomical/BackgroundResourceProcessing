using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using CommNet.Network;
using Unity.Burst.CompilerServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace BackgroundResourceProcessing.Collections.Burst;

internal struct PriorityQueue<T>(RawList<T> items) : IDisposable
    where T : struct, IComparable<T>
{
    RawList<T> items = items;

    public readonly int Count => items.Count;
    public readonly bool IsEmpty => Count == 0;
    public readonly int Capacity => items.Capacity;
    public readonly Allocator Allocator => items.Allocator;
    public readonly Span<T> Span => items.Span;

    public PriorityQueue(Allocator allocator)
        : this(new RawList<T>(allocator)) { }

    public PriorityQueue(int capacity, Allocator allocator)
        : this(new RawList<T>(capacity, allocator)) { }

    public PriorityQueue(Span<T> items, Allocator allocator)
        : this(new RawList<T>(items, allocator))
    {
        // Heapify from the last non-leaf node down to the root
        for (int i = (Count - 2) / 2; i >= 0; --i)
            MoveDown(i);
    }

    public PriorityQueue(T[] items, Allocator allocator)
        : this(new Span<T>(items), allocator) { }

    public void Dispose()
    {
        items.Dispose();
    }

    public void Enqueue(T item)
    {
        items.Add(item);
        MoveUp(items.Count - 1);
    }

    [IgnoreWarning(1370)]
    public T Dequeue()
    {
        if (!TryDequeue(out var item))
            throw new InvalidOperationException("Attempted to call Dequeue() on an empty queue");

        return item;
    }

    public bool TryDequeue(out T item)
    {
        if (IsEmpty)
        {
            item = default;
            return false;
        }

        item = items[0];
        RemoveRootElement();
        return true;
    }

    [IgnoreWarning(1370)]
    public readonly T Peek()
    {
        if (!TryPeek(out var item))
            throw new InvalidOperationException("Attempted to call Peek() on an empty queue");
        return item;
    }

    public readonly bool TryPeek(out T item)
    {
        if (IsEmpty)
        {
            item = default;
            return false;
        }

        item = items[0];
        return true;
    }

    public void Clear() => items.Clear();

    private void MoveUp(int index)
    {
        var item = items[index];
        while (index != 0)
        {
            int parentIndex = GetParentindex(index);
            var parent = items[parentIndex];

            if (item.CompareTo(parent) < 0)
            {
                items[index] = parent;
                index = parentIndex;
            }
            else
            {
                break;
            }
        }

        items[index] = item;
    }

    private void MoveDown(int index)
    {
        var item = items[index];

        while (true)
        {
            var leftIndex = GetLeftChildIndex(index);
            var rightIndex = GetRightChildIndex(index);

            if (leftIndex >= Count)
                break;

            ref var left = ref items[leftIndex];
            var minIndex = leftIndex;
            ref var min = ref left;

            if (rightIndex < Count)
            {
                ref var right = ref items[rightIndex];

                if (right.CompareTo(left) < 0)
                {
                    minIndex = rightIndex;
                    min = ref right;
                }
            }

            if (item.CompareTo(min) < 0)
            {
                // Heap property is satisfied. We can insert the node here.
                break;
            }

            items[index] = min;
            index = minIndex;
        }

        items[index] = item;
    }

    private void RemoveRootElement() => RemoveAtIndex(0);

    [IgnoreWarning(1370)]
    private void RemoveAtIndex(int index)
    {
        using var guard = ItemDisposer.Guard(items[index]);
        var last = items.Pop();
        if (index >= items.Count)
            return;

        items[index] = last;
        MoveDown(index);
    }

    static int GetParentindex(int index) => (index - 1) / 2;

    static int GetLeftChildIndex(int index) => index * 2 + 1;

    static int GetRightChildIndex(int index) => index * 2 + 2;

    [IgnoreWarning(1370)]
    static void ThrowPriorityQueueEmpty() =>
        new InvalidOperationException(
            "cannot access priority queue element because the queue is empty"
        );
}
