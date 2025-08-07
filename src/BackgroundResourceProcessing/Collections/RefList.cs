using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Unity.Collections.LowLevel.Unsafe;

namespace BackgroundResourceProcessing.Collections;

internal struct RefList<T> : IList<T>, ICollection<T>, IEnumerable<T>
{
    T[] data;
    int count = 0;

    public readonly int Count => count;
    public readonly int Capacity => data.Length;
    public readonly T[] RawData => data;

    public readonly ref T this[int index]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            if (index >= count)
                ThrowIndexOutOfRangeException();
            return ref data[index];
        }
    }

    readonly T IList<T>.this[int index]
    {
        get => this[index];
        set => this[index] = value;
    }

    private RefList(T[] data, int count)
    {
        if (count > data.Length)
            throw new ArgumentException(nameof(count), "count was out of range");

        this.data = data;
        this.count = count;
    }

    public RefList(int capacity)
    {
        data = new T[capacity];
    }

    public RefList(T[] data)
    {
        this.data = data;
        this.count = data.Length;
    }

    public RefList(IEnumerable<T> data)
        : this([.. data]) { }

    #region Exceptions
    static void ThrowIndexOutOfRangeException() => throw new IndexOutOfRangeException();

    static void ThrowArgumentOutOfRangeException(string arg) =>
        throw new ArgumentOutOfRangeException(arg);

    #endregion


    #region Non-Interface Methods
    public void AddRange<I>(I enumerator)
        where I : IEnumerator<T>
    {
        while (enumerator.MoveNext())
        {
            if (Count == Capacity)
                Expand(1);

            data[count] = enumerator.Current;
            count += 1;
        }
    }

    public void AddRange(IEnumerable<T> collection)
    {
        if (collection is ICollection<T> list)
            Expand(list.Count);

        AddRange(collection.GetEnumerator());
    }

    public void Reserve(int capacity)
    {
        if (capacity <= Capacity)
            return;

        Expand(capacity - Capacity);
    }

    public void SetLength(int newcount)
    {
        if (newcount > Capacity)
            ThrowArgumentOutOfRangeException(nameof(newcount));

        count = newcount;
    }

    public readonly RefList<T> Clone()
    {
        return new((T[])data.Clone(), count);
    }
    #endregion

    #region IList<T>
    public readonly int IndexOf(T item)
    {
        return Array.IndexOf(data, item, 0, count);
    }

    public void Insert(int index, T item)
    {
        if (index < 0 || index > count)
            ThrowIndexOutOfRangeException();

        if (count == Capacity)
            Expand(1);

        if (index < count)
        {
            T prev = default;
            for (int i = index; i < count + 1; ++i)
                (prev, data[i]) = (data[i], prev);
        }

        data[index] = item;
        count += 1;
    }

    public void RemoveAt(int index)
    {
        if (index < 0 || index >= count)
            ThrowIndexOutOfRangeException();

        if (index < count - 1)
            Array.Copy(data, index + 1, data, index, count - index);

        count -= 1;
        data[count] = default;
    }
    #endregion

    #region ICollection<T>
    readonly bool ICollection<T>.IsReadOnly => false;

    public void Add(T item)
    {
        if (count == Capacity)
            Expand(1);

        data[count] = item;
        count += 1;
    }

    public void Clear()
    {
        if (!UnsafeUtility.IsUnmanaged<T>())
            Array.Clear(data, 0, count);
        count = 0;
    }

    public readonly bool Contains(T item)
    {
        return IndexOf(item) > 0;
    }

    public readonly void CopyTo(T[] array, int arrayIndex)
    {
        Array.Copy(data, 0, array, arrayIndex, count);
    }

    public readonly void CopyTo(int index, T[] array, int arrayIndex, int count)
    {
        if (count > this.count - index)
            ThrowIndexOutOfRangeException();

        Array.Copy(data, index, array, arrayIndex, count);
    }

    public bool Remove(T item)
    {
        int index = IndexOf(item);
        if (index >= 0)
            RemoveAt(index);
        return index >= 0;
    }
    #endregion

    #region Internals
    private void Expand(int additional)
    {
        if (additional < 0)
            throw new ArgumentException(nameof(additional), "additional was out of range");

        int newcap = Math.Max(data.Length * 2, data.Length + additional);
        Array.Resize(ref data, newcap);
    }
    #endregion

    #region IEnumerable
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly Enumerator GetEnumerator() => new(this);

    readonly IEnumerator<T> IEnumerable<T>.GetEnumerator() => GetEnumerator();

    readonly IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    [method: MethodImpl(MethodImplOptions.AggressiveInlining)]
    public struct Enumerator(RefList<T> list) : IEnumerator<T>, IEnumerable<T>
    {
        readonly RefList<T> list = list;
        int index = -1;

        public readonly T Current => list.data[index];
        readonly object IEnumerator.Current => Current;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool MoveNext()
        {
            index += 1;
            return index < list.count;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly void Dispose() { }

        public void Reset()
        {
            index = -1;
        }

        public readonly Enumerator GetEnumerator() => this;

        readonly IEnumerator<T> IEnumerable<T>.GetEnumerator() => GetEnumerator();

        readonly IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
    #endregion
}
