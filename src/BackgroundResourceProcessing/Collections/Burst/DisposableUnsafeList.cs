using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using Unity.Collections;

namespace BackgroundResourceProcessing.Collections.Burst;

[DebuggerDisplay("Count = {Count}")]
[DebuggerTypeProxy(typeof(DisposableUnsafeList<>.DebugView))]
internal struct DisposableUnsafeList<T>(TypedUnsafeList<T> list)
    : IList<T>,
        IEnumerable<T>,
        IDisposable
    where T : struct, IEquatable<T>, IDisposable
{
    private TypedUnsafeList<T> list = list;

    public readonly int Count => list.Count;
    public readonly int Capacity => list.Capacity;

    public readonly ref T this[int index] => ref list[index];

    public DisposableUnsafeList(Allocator allocator)
        : this(new TypedUnsafeList<T>(allocator)) { }

    public DisposableUnsafeList(int capacity, Allocator allocator)
        : this(new TypedUnsafeList<T>(capacity, allocator)) { }

    public void Push(T item) => list.Push(item);

    public bool TryPop(out T item) => list.TryPop(out item);

    public void RemoveAtSwapBack(int index) => list.RemoveAtSwapBack(index).Dispose();

    #region IDisposable
    public void Dispose()
    {
        try
        {
            foreach (ref T item in list)
                item.Dispose();
        }
        finally
        {
            list.Dispose();
        }
    }
    #endregion

    #region ICollection<T>
    readonly bool ICollection<T>.IsReadOnly => false;

    public void Add(T item) => list.Add(item);

    public bool Remove(T item)
    {
        int index = list.IndexOf(item);
        if (index < 0)
            return false;

        list[index].Dispose();
        list.RemoveAt(index);
        return true;
    }

    public void Clear()
    {
        try
        {
            foreach (ref T item in list)
                item.Dispose();
        }
        finally
        {
            list.Clear();
        }
    }

    public readonly bool Contains(T item) => list.Contains(item);

    public readonly void CopyTo(T[] array, int index) => list.CopyTo(array, index);
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
    public readonly TypedUnsafeList<T>.Enumerator GetEnumerator() => list.GetEnumerator();

    readonly IEnumerator<T> IEnumerable<T>.GetEnumerator() => GetEnumerator();

    readonly IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    #endregion

    private sealed class DebugView(RawArray<T> array)
    {
        [DebuggerBrowsable(DebuggerBrowsableState.RootHidden)]
        public T[] Items { get; } = [.. array];
    }
}
