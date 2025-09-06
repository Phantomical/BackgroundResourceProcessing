using System;
using BackgroundResourceProcessing.BurstSolver;
using Unity.Burst.CompilerServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

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
internal unsafe struct RawList<T>(Allocator allocator) : IDisposable
    where T : struct
{
    T* data = null;
    uint count = 0;
    uint capacity = 0;
    readonly Allocator allocator = allocator;

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
    public readonly Allocator Allocator => allocator;
    public readonly Span<T> Span => new(data, Count);
    public readonly T* Ptr => data;

    public readonly ref T this[int index]
    {
        get
        {
            if (index < 0 || index >= count)
                ThrowIndexOutOfRange();

            return ref data[index];
        }
    }

    public RawList(int capacity, Allocator allocator)
        : this(allocator)
    {
        Reserve(capacity);
    }

    public void Dispose()
    {
        if (data is null)
            return;

        if (BurstUtil.UseTestAllocator)
            TestAllocator.Free(data);
        else
            UnityAllocator.Free(data, allocator);

        data = null;
        capacity = 0;
        count = 0;
    }

    public void Add(T elem)
    {
        if (count == capacity)
            Expand(16);

        data[count] = elem;
        count += 1;
    }

    public void AddRange(Span<T> values)
    {
        if (Count + values.Length > Capacity)
            Expand(Count + values.Length);

        if (BurstUtil.UseTestAllocator)
        {
            T* p = &data[count];
            for (int i = 0; i < values.Length; ++i)
                p[i] = values[i];
        }
        else
        {
            fixed (T* src = values)
            {
                UnityAllocator.Copy(&data[count], src, values.Length);
            }
        }

        count += (uint)values.Length;
    }

    public void Push(T elem) => Add(elem);

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

    [IgnoreWarning(1310)]
    public void RemoveAt(int index)
    {
        if (index < 0 || index >= count)
            throw new ArgumentOutOfRangeException(nameof(index));

        if (index == count - 1)
        {
            count -= 1;
            return;
        }

        if (BurstUtil.UseTestAllocator)
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
    }

    [IgnoreWarning(1310)]
    public void RemoveAtSwapBack(int index)
    {
        if (index < 0 || index >= count)
            throw new ArgumentOutOfRangeException(nameof(index));

        if (index == count - 1)
        {
            count -= 1;
            return;
        }

        this[index] = this[Count - 1];
        count -= 1;
    }

    [IgnoreWarning(1310)]
    public void Resize(int newsize, NativeArrayOptions options = NativeArrayOptions.ClearMemory)
    {
        if (newsize < 0)
            throw new ArgumentOutOfRangeException(nameof(newsize));
        if (newsize > Capacity)
            Expand(newsize);

        if (newsize <= Count || options == NativeArrayOptions.UninitializedMemory)
        {
            // do nothing
        }
        else if (BurstUtil.UseTestAllocator)
        {
            for (uint i = count; i < newsize; ++i)
                data[i] = default;
        }
        else
        {
            UnityAllocator.Clear(&data[count], newsize - Count);
        }

        count = (uint)newsize;
    }

    [IgnoreWarning(1310)]
    public void Truncate(int index)
    {
        if (index < 0)
            throw new ArgumentOutOfRangeException(nameof(index));
        if (index >= count)
            return;

        count = (uint)index;
    }

    [IgnoreWarning(1310)]
    public void Reserve(int capacity)
    {
        if (capacity < 0)
            throw new ArgumentOutOfRangeException(nameof(capacity));
        if (capacity <= this.capacity)
            return;

        if (BurstUtil.UseTestAllocator)
        {
            data = TestAllocator.Realloc(data, capacity);
        }
        else
        {
            T* prev = data;
            data = UnityAllocator.Alloc<T>(capacity, allocator);

            if (prev is not null)
            {
                UnityAllocator.Copy(data, prev, Count);
                UnityAllocator.Free(prev, allocator);
            }
        }

        this.capacity = (uint)capacity;
    }

    private void Expand(int newcap = 0)
    {
        Reserve(Math.Max(Capacity * 2, newcap));
    }

    [IgnoreWarning(1310)]
    static void ThrowIndexOutOfRange() =>
        throw new IndexOutOfRangeException("list index was out of range");
}

internal static class RawListExtensions
{
    [return: AssumeRange(-1, int.MaxValue)]
    internal static int IndexOf<T>(this RawList<T> list, T item)
        where T : struct, IEquatable<T>
    {
        for (int i = 0; i < list.Count; ++i)
            if (list[i].Equals(item))
                return i;
        return -1;
    }

    internal static bool Contains<T>(this RawList<T> list, T item)
        where T : struct, IEquatable<T> => list.IndexOf(item) != -1;
}
