using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace BackgroundResourceProcessing.Collections;

/// <summary>
/// A read only view on a <c>List&lt;T&gt;</c>. This is similar to <c>ReadOnlyCollection</c>
/// except that it doesn't require an allocation and has more aggressive inlining attributes.
/// </summary>
/// <typeparam name="T"></typeparam>
[DebuggerDisplay("Count = {Count}")]
public readonly struct ReadOnlyList<T>(List<T> list) : IList<T>, ICollection<T>, IEnumerable<T>
{
    readonly List<T> list = list ?? throw new ArgumentNullException(nameof(list));

    /// <summary>
    /// Gets the element at the specified index.
    /// </summary>
    /// <param name="index"></param>
    /// <returns></returns>
    public readonly T this[int index]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get { return list[index]; }
        set { throw new NotImplementedException(); }
    }

    /// <summary>
    /// Gets the number of elements contained in this list.
    /// </summary>
    public readonly int Count => list.Count;

    /// <summary>
    /// Indicates that this collection is read-only. Always returns <c>true</c>.
    /// </summary>
    public readonly bool IsReadOnly => true;

    /// <summary>
    /// Determines whether an element is in the list.
    /// </summary>
    /// <param name="value">The object to locate in the list</param>
    /// <returns>true if the value is found, false otherwise</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly bool Contains(T value)
    {
        return list.Contains(value);
    }

    /// <summary>
    /// Copies the entire list to the provided array, starting at the specified
    /// index of the target array.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly void CopyTo(T[] array, int index)
    {
        list.CopyTo(array, index);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public IEnumerator<T> GetEnumerator()
    {
        return list.GetEnumerator();
    }

    /// <summary>
    /// Searches for the specified object and returns the zero-backed index of
    /// the first occurrence within this list.
    /// </summary>
    /// <param name="value"></param>
    /// <returns></returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int IndexOf(T value)
    {
        return list.IndexOf(value);
    }

    public readonly ReadOnlyCollection<T> AsReadOnlyCollection()
    {
        return list.AsReadOnly();
    }

    public override bool Equals(object obj)
    {
        if (obj is ReadOnlyList<T> rolist)
            return list.Equals(rolist.list);
        return list.Equals(obj);
    }

    public override int GetHashCode()
    {
        return list.GetHashCode();
    }

    public override string ToString()
    {
        return list.ToString();
    }

    #region IList<T>
    void IList<T>.Insert(int index, T item)
    {
        throw new NotImplementedException();
    }

    void IList<T>.RemoveAt(int index)
    {
        throw new NotImplementedException();
    }
    #endregion

    #region ICollection<T>
    void ICollection<T>.Add(T item)
    {
        throw new NotImplementedException();
    }

    void ICollection<T>.Clear()
    {
        throw new NotImplementedException();
    }

    bool ICollection<T>.Remove(T item)
    {
        throw new NotImplementedException();
    }
    #endregion

    #region IEnumerable
    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }
    #endregion

    #region Debugger Visualizer
    private sealed class DebugView(ReadOnlyList<T> list)
    {
        [DebuggerBrowsable(DebuggerBrowsableState.RootHidden)]
        public T[] Items => [.. list];
    }
    #endregion
}
