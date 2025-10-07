using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using BackgroundResourceProcessing.BurstSolver;
using Unity.Burst.CompilerServices;
using Unity.Collections;

namespace BackgroundResourceProcessing.Collections.Burst;

#pragma warning disable CS8500 // This takes the address of, gets the size of, or declares a pointer to a managed type

/// <summary>
/// The base raw list type that others build on top of.
/// </summary>
/// <typeparam name="T"></typeparam>
///
/// <remarks>
/// In order to work when running within the test suite we can't call into any
/// of the methods that unity implements <i>when running in the test suite</i>.
/// As such, this specializes based on <see cref="BurstUtil.UseTestAllocator"/>
/// to use alternate implementations of several key methods that can be used
/// outside of unity.
/// </remarks>
[DebuggerDisplay("Count = {Count}")]
[DebuggerTypeProxy(typeof(SpanDebugView<>))]
internal unsafe struct RawList<T>(AllocatorHandle allocator) : IEnumerable<T>
    where T : unmanaged
{
    T* data = null;
    uint count = 0;
    uint capacity = 0;
    AllocatorHandle allocator = allocator;

    public readonly int Count
    {
        [return: AssumeRange(0, int.MaxValue)]
        get => (int)count;
    }
    public readonly int Capacity
    {
        [return: AssumeRange(0, int.MaxValue)]
        get => (int)capacity;
    }
    public readonly AllocatorHandle Allocator => allocator;
    public readonly bool IsEmpty => Count == 0;
    public readonly MemorySpan<T> Span => new(data, Count);
    public readonly T* Ptr => data;

    public readonly ref T this[int index]
    {
        get
        {
            if (index < 0 || index >= count)
                BurstCrashHandler.Crash(Error.RawList_IndexOutOfRange, index);

            return ref data[index];
        }
    }

    public RawList(int capacity, AllocatorHandle allocator)
        : this(allocator)
    {
        Reserve(capacity);
    }

    public RawList(MemorySpan<T> data, AllocatorHandle allocator)
        : this(data.Length, allocator)
    {
        AddRange(data);
    }

    public RawList(Span<T> data, AllocatorHandle allocator)
        : this(data.Length, allocator)
    {
        AddRange(data);
    }

    public RawList(T[] data, AllocatorHandle allocator)
        : this((Span<T>)data, allocator) { }

    public RawList(RawList<T> list)
        : this(list.Span, list.Allocator) { }

    public RawList(RawArray<T> array)
        : this(array.Span, array.Allocator) { }

    public readonly RawList<T> Clone() => new(this);

    public void Add(T elem)
    {
        if (Hint.Unlikely(count == capacity))
            Expand(16);

        data[count] = elem;
        count += 1;
    }

    public void AddRange(MemorySpan<T> values)
    {
        if (Hint.Unlikely(Count + values.Length > Capacity))
            Expand(Count + values.Length);

        values.CopyTo(new MemorySpan<T>(data + Count, Capacity - Count));
        count += (uint)values.Length;
    }

    public void AddRange(Span<T> values)
    {
        fixed (T* ptr = values)
        {
            AddRange(new MemorySpan<T>(ptr, values.Length));
        }
    }

    public void Push(T elem) => Add(elem);

    [IgnoreWarning(1370)]
    public T Pop()
    {
        if (IsEmpty)
            BurstCrashHandler.Crash(Error.RawList_PopEmpty);

        count -= 1;
        return data[count];
    }

    public bool TryPop(out T elem)
    {
        if (count == 0)
        {
            elem = default;
            return false;
        }
        else
        {
            count -= 1;
            elem = data[count];
            return true;
        }
    }

    public void Clear()
    {
        count = 0;
    }

    public T RemoveAt(int index)
    {
        if (index < 0 || index >= count)
            BurstCrashHandler.Crash(Error.RawList_IndexOutOfRange, index);

        var item = this[index];

        if (index == count - 1) { }
        else if (BurstUtil.UseTestAllocator)
        {
            var dst = Span.Slice(index, Count - index - 1);
            var src = Span.Slice(index + 1);

            src.CopyTo(dst);
        }
        else
        {
            T* dst = &data[index];
            T* src = &data[index + 1];

            UnityAllocator.Copy(dst, src, Count - index - 1);
        }

        count -= 1;
        return item;
    }

    public T RemoveAtSwapBack(int index)
    {
        if (index < 0 || index >= count)
            BurstCrashHandler.Crash(Error.RawList_IndexOutOfRange, index);

        var item = this[index];

        if (index <= count)
            this[index] = this[Count - 1];

        count -= 1;
        return item;
    }

    public void Resize(int newsize, NativeArrayOptions options = NativeArrayOptions.ClearMemory)
    {
        if (newsize < 0)
            BurstCrashHandler.Crash(Error.RawList_SizeIsNegative);
        if (Hint.Unlikely(newsize > Capacity))
            Expand(newsize);

        try
        {
            if (newsize <= Count) { }
            else if (options == NativeArrayOptions.UninitializedMemory) { }
            else if (BurstUtil.UseTestAllocator)
            {
                for (uint i = count; i < newsize; ++i)
                    data[i] = default;
            }
            else
            {
                UnityAllocator.Clear(&data[count], newsize - Count);
            }
        }
        finally
        {
            count = (uint)newsize;
        }
    }

    public void Truncate(int index)
    {
        if (index < 0)
            BurstCrashHandler.Crash(Error.RawList_Truncate_IndexIsNegative);
        if (index >= count)
            return;

        count = (uint)index;
    }

    public void Reserve(int capacity)
    {
        if (capacity < 0)
            BurstCrashHandler.Crash(Error.RawList_CapacityIsNegative);
        if (Hint.Likely(capacity <= this.capacity))
            return;

        Grow(capacity);
    }

    private void Grow(int capacity)
    {
        if (capacity <= Capacity)
            BurstCrashHandler.Crash(Error.RawList_CapacityIsNegative);

        T* prev = data;
        data = Allocator.Allocate<T>(capacity);

        if (prev is not null)
        {
            if (Count > 0)
            {
                var src = new MemorySpan<T>(prev, Count);
                var dst = new MemorySpan<T>(data, Count);

                src.CopyTo(dst);
            }

            Allocator.Free(prev);
        }

        this.capacity = (uint)capacity;
    }

    private void Expand(int newcap = 0)
    {
        Reserve(Math.Max(Capacity * 2, newcap));
    }

    private sealed class DebugView(RawList<T> list)
    {
        [DebuggerBrowsable(DebuggerBrowsableState.RootHidden)]
        public T[] Items { get; } = [.. list];
    }

    #region IEnumerable<T>
    public readonly Enumerator GetEnumerator() => new(this);

    readonly IEnumerator<T> IEnumerable<T>.GetEnumerator() => GetEnumerator();

    readonly IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public unsafe struct Enumerator(RawList<T> list) : IEnumerator<T>
    {
        T* ptr = list.Ptr - 1;
        readonly T* end = list.Ptr + list.Count;

        public ref T Current => ref *ptr;
        T IEnumerator<T>.Current => Current;
        object IEnumerator.Current => Current;

        public bool MoveNext()
        {
            ptr += 1;
            return ptr < end;
        }

        void IEnumerator.Reset() => throw new NotSupportedException();

        public void Dispose() { }
    }

    #endregion
}

internal static class RawListExtensions
{
    [return: AssumeRange(-1, int.MaxValue)]
    internal static int IndexOf<T>(this RawList<T> list, T item)
        where T : unmanaged, IEquatable<T>
    {
        for (int i = 0; i < list.Count; ++i)
            if (list[i].Equals(item))
                return i;
        return -1;
    }

    internal static bool Contains<T>(this RawList<T> list, T item)
        where T : unmanaged, IEquatable<T>
    {
        return list.IndexOf(item) != -1;
    }
}
