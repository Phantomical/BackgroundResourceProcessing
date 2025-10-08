using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace BackgroundResourceProcessing.Collections;

/// <summary>
/// A list whose elements remain at stable indices even once they are removed.
/// </summary>
/// <typeparam name="T"></typeparam>
///
/// <remarks>
/// This allows elements to be removed even while preserving the correct set
/// of indices.
/// </remarks>
internal struct StableList<T> : IList<T>, ICollection<T>, IEnumerable<T>
    where T : class
{
    readonly List<T> list;
    int tail = 0;

    public readonly int Count
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => list.Count;
    }
    public readonly int Capacity
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => list.Capacity;
    }

    public readonly StableListView<T>.EntryEnumerable Entries
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => new(list);
    }

    public readonly bool IsReadOnly => false;

    public T this[int index]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        readonly get => list[index];
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set
        {
            list[index] = value;
            if (value is null)
                tail = Math.Min(tail, index);
        }
    }

    public StableList()
        : this([]) { }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public StableList(List<T> list) => this.list = list;

    public StableList(int capacity)
        : this(new List<T>(capacity)) { }

    public int Add(T item)
    {
        var count = Count;
        for (; tail < count; ++tail)
        {
            if (list[tail] is not null)
                continue;
            list[tail] = item;
            return tail++;
        }

        list.Add(item);
        tail = count + 1;
        return count;
    }

    public void RemoveAt(int index)
    {
        if (index < 0 || index >= Count)
            throw new IndexOutOfRangeException("RemoveAt index was out of range");

        if (index == Count - 1)
        {
            if (tail > index)
            {
                tail = index;
                list.RemoveAt(index);
                return;
            }

            int i;
            for (i = index - 1; i >= tail; ++i)
            {
                if (list[i] is not null)
                    break;
            }

            list.RemoveRange(i, Count - i);
            return;
        }
        else
        {
            this[index] = null;
        }
    }

    public void Insert(int index, T item)
    {
        if (index < 0 || index > Count)
            throw new ArgumentOutOfRangeException(nameof(index));
        if (index == Count)
        {
            var newcount = index + 1;
            list.AddRange(Enumerable.Repeat<T>(null, newcount - Count));
        }

        this[index] = item;
    }

    public void Clear()
    {
        list.Clear();
        tail = 0;
    }

    public void Reserve(int capacity)
    {
        if (capacity < 0)
            throw new ArgumentOutOfRangeException(nameof(capacity));
        if (capacity < Count)
            return;
        list.Capacity = capacity;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly StableListView<T> AsView() => new(list);

    #region IList<T>
    public readonly int IndexOf(T item) => list.IndexOf(item);
    #endregion

    #region ICollection<T>
    void ICollection<T>.Add(T item) => Add(item);

    public bool Remove(T item)
    {
        var index = list.IndexOf(item);
        if (index < 0)
            return false;
        RemoveAt(index);
        return true;
    }

    public readonly bool Contains(T item) => list.IndexOf(item) > 0;

    public readonly void CopyTo(T[] array, int arrayIndex) => list.CopyTo(array, arrayIndex);
    #endregion

    #region Enumerator
    public readonly StableListView<T>.Enumerator GetEnumerator() => new(list);

    readonly IEnumerator<T> IEnumerable<T>.GetEnumerator() => GetEnumerator();

    readonly IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    #endregion
}

/// <summary>
/// A read only list that automatically skips over null values when iterating.
/// </summary>
/// <typeparam name="T"></typeparam>
/// <remarks>
/// This is used by <see cref="BackgroundResourceProcessor"/> as a view into
/// its stored inventories and converters. It guarantees that the indices of
/// converters and inventories will remain stable even if they are added or
/// removed.
/// </remarks>
public readonly struct StableListView<T> : IEnumerable<T>, ICollection<T>, IList<T>
    where T : class
{
    static readonly List<T> Empty = [];

    readonly List<T> list;

    public int Count
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => list.Count;
    }
    public int Capacity
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => list.Capacity;
    }

    public T this[int index]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => list[index];
    }

    public StableListView()
        : this(Empty) { }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal StableListView(List<T> list) => this.list = list;

    /// <summary>
    /// Determines whether an element is in the list.
    /// </summary>
    /// <param name="value">The object to locate in the list</param>
    /// <returns>true if the value is found, false otherwise</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly bool Contains(T value) => list.Contains(value);

    /// <summary>
    /// Copies the entire list to the provided array, starting at the specified
    /// index of the target array.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly void CopyTo(T[] array, int index) => list.CopyTo(array, index);

    /// <summary>
    /// Searches for the specified object and returns the zero-backed index of
    /// the first occurrence within this list.
    /// </summary>
    /// <param name="value"></param>
    /// <returns></returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int IndexOf(T value) => list.IndexOf(value);

    #region Enumerator
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly Enumerator GetEnumerator() => new(list);

    readonly IEnumerator<T> IEnumerable<T>.GetEnumerator() => GetEnumerator();

    readonly IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public struct Enumerator : IEnumerator<T>
    {
        List<T>.Enumerator enumerator;

        public T Current
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => enumerator.Current;
        }
        object IEnumerator.Current => Current;

        public Enumerator()
            : this(Empty) { }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal Enumerator(List<T> list) => enumerator = list.GetEnumerator();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool MoveNext()
        {
            while (true)
            {
                if (!enumerator.MoveNext())
                    return false;

                if (Current is null)
                    continue;

                return true;
            }
        }

        void IEnumerator.Reset() => throw new NotSupportedException();

        public void Dispose() => enumerator.Dispose();
    }
    #endregion

    #region Entries
    public readonly struct EntryEnumerable : IEnumerable<KeyValuePair<int, T>>
    {
        readonly List<T> list;

        public EntryEnumerable()
            : this(Empty) { }

        internal EntryEnumerable(List<T> list) => this.list = list;

        public readonly EntryEnumerator GetEnumerator() => new(list);

        readonly IEnumerator<KeyValuePair<int, T>> IEnumerable<
            KeyValuePair<int, T>
        >.GetEnumerator() => GetEnumerator();

        readonly IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    public struct EntryEnumerator : IEnumerator<KeyValuePair<int, T>>
    {
        List<T>.Enumerator enumerator;
        int index = -1;

        public EntryEnumerator() => enumerator = Empty.GetEnumerator();

        internal EntryEnumerator(List<T> list) => enumerator = list.GetEnumerator();

        public KeyValuePair<int, T> Current
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => new(index, enumerator.Current);
        }
        object IEnumerator.Current => Current;

        public bool MoveNext()
        {
            while (true)
            {
                if (!enumerator.MoveNext())
                    return false;
                index += 1;

                if (enumerator.Current is null)
                    continue;

                return true;
            }
        }

        void IEnumerator.Reset() => throw new NotSupportedException();

        public void Dispose() => enumerator.Dispose();
    }
    #endregion

    #region IList<T>
    T IList<T>.this[int index]
    {
        get => this[index];
        set => throw new NotSupportedException();
    }

    void IList<T>.Insert(int index, T item) => throw new NotSupportedException();

    void IList<T>.RemoveAt(int index) => throw new NotSupportedException();
    #endregion

    #region ICollection<T>
    bool ICollection<T>.IsReadOnly => true;

    void ICollection<T>.Add(T item) => throw new NotSupportedException();

    bool ICollection<T>.Remove(T item) => throw new NotSupportedException();

    void ICollection<T>.Clear() => throw new NotSupportedException();
    #endregion
}
