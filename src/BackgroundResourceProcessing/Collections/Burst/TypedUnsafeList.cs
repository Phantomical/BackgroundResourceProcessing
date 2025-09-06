using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Burst.CompilerServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

#pragma warning disable CS8500 // This takes the address of, gets the size of, or declares a pointer to a managed type

namespace BackgroundResourceProcessing.Collections.Burst;

/// <summary>
/// An unmanaged, resizable list.
/// </summary>
/// <typeparam name="T"></typeparam>
///
/// <remarks>
/// This is a thin wrapper around <see cref="UnsafeList"/> which ensures that
/// the internal type is consistent. As such, it can be stored within other
/// lists.
/// </remarks>
internal struct TypedUnsafeList<T>(RawList<T> list) : IList<T>, IEnumerable<T>, IDisposable
    where T : struct, IEquatable<T>
{
    private RawList<T> list = list;

    public readonly int Count => list.Count;
    public readonly int Capacity => list.Capacity;
    public readonly Allocator Allocator => list.Allocator;

    public readonly unsafe Span<T> Span => list.Span;

    public readonly ref T this[int index]
    {
        get
        {
            if (index < 0 || index >= Count)
                ThrowIndexOutOfRange();

            unsafe
            {
                return ref list[index];
            }
        }
    }

    public TypedUnsafeList(Allocator allocator)
        : this(new RawList<T>(allocator)) { }

    public unsafe TypedUnsafeList(int capacity, Allocator allocator)
        : this(new RawList<T>(capacity, allocator)) { }

    [IgnoreWarning(1310)]
    private static void ThrowIndexOutOfRange() =>
        throw new IndexOutOfRangeException("list index was out of range");

    public static implicit operator Span<T>(TypedUnsafeList<T> list) => list.Span;

    public void Push(T item) => Add(item);

    public bool TryPop(out T item)
    {
        item = default;
        if (Count == 0)
            return false;

        item = RemoveAtSwapBack(Count - 1);
        return true;
    }

    public T RemoveAtSwapBack(int index)
    {
        T item = this[index];
        list.RemoveAtSwapBack(index);
        return item;
    }

    public void Resize(int newSize, NativeArrayOptions options = NativeArrayOptions.ClearMemory)
    {
        list.Resize(newSize, options);
    }

    public readonly TypedUnsafeList<T> Clone()
    {
        TypedUnsafeList<T> clone = new(Count, list.Allocator);
        clone.list.AddRange(list.Span);
        return clone;
    }

    public TypedUnsafeList<T> Take()
    {
        var copy = this;
        list = new(Allocator);
        return copy;
    }

    #region IDisposable
    public void Dispose() => list.Dispose();
    #endregion

    #region ICollection<T>
    readonly bool ICollection<T>.IsReadOnly => false;

    public void Add(T item) => list.Add(item);

    public bool Remove(T item)
    {
        int index = list.IndexOf(item);
        if (index == -1)
            return false;

        list.RemoveAt(index);
        return true;
    }

    public void Clear() => list.Clear();

    public readonly bool Contains(T item) => list.Contains(item);

    public readonly void CopyTo(T[] array, int index)
    {
        if (array is null)
            throw new ArgumentNullException(nameof(array));
        if (index < 0)
            throw new ArgumentOutOfRangeException(nameof(index));
        if (Count + index > array.Length)
            throw new ArgumentException("not enough room in output array");

        var src = list.Span;
        var dst = new Span<T>(array).Slice(index, src.Length);
        src.CopyTo(dst);
    }
    #endregion

    #region IList<T>
    T IList<T>.this[int index]
    {
        readonly get => this[index];
        set => this[index] = value;
    }

    public int IndexOf(T item) => list.IndexOf(item);

    void IList<T>.Insert(int index, T item) => throw new NotSupportedException();

    void IList<T>.RemoveAt(int index) => throw new NotSupportedException();
    #endregion

    #region IEnumerator<T>
    public readonly Enumerator GetEnumerator() => new(this);

    readonly IEnumerator<T> IEnumerable<T>.GetEnumerator() => GetEnumerator();

    readonly IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public unsafe struct Enumerator(TypedUnsafeList<T> list) : IEnumerator<T>
    {
        T* ptr = list.list.Ptr - 1;
        readonly T* end = list.list.Ptr + list.Count;

        public ref T Current => ref *ptr;
        T IEnumerator<T>.Current => Current;
        object IEnumerator.Current => Current;

        public bool MoveNext()
        {
            ptr += 1;
            return ptr < end;
        }

        void IEnumerator.Reset() => throw new NotSupportedException();

        public readonly void Dispose() { }
    }
    #endregion
}
