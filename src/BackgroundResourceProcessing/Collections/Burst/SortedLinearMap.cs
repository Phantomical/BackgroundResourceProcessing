using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using Unity.Collections;

namespace BackgroundResourceProcessing.Collections.Burst;

/// <summary>
/// Like <see cref="LinearMap{K, V}"/> except that it can assume that its keys
/// are sorted for some methods.
/// </summary>
/// <typeparam name="K"></typeparam>
/// <typeparam name="V"></typeparam>
[DebuggerDisplay("Count = {Count}")]
[DebuggerTypeProxy(typeof(DictionaryDebugView<,>))]
internal struct SortedLinearMap<K, V>(LinearMap<K, V> map) : IEnumerable<KeyValuePair<K, V>>
    where K : unmanaged, IEquatable<K>, IComparable<K>
    where V : unmanaged
{
    LinearMap<K, V> map = map;

    public readonly int Count => map.Count;
    public readonly int Capacity => map.Capacity;
    public readonly AllocatorHandle Allocator => map.Allocator;
    public readonly bool IsEmpty => Count == 0;

    public readonly ref V this[K key] => ref map[key];

    public readonly LinearMap<K, V>.KeyEnumerable Keys => map.Keys;
    public readonly LinearMap<K, V>.ValueEnumerable Values => map.Values;
    public readonly MemorySpan<LinearMap<K, V>.Entry> Entries => map.Entries;

    public SortedLinearMap(int capacity, AllocatorHandle allocator)
        : this(new LinearMap<K, V>(capacity, allocator)) { }

    public readonly bool ContainsKey(K key) => map.ContainsKey(key);

    public readonly ref LinearMap<K, V>.Entry GetEntryAtIndex(int index) =>
        ref map.GetEntryAtIndex(index);

    public void AddUnchecked(K key, V value) => map.AddUnchecked(key, value);

    public void Reserve(int capacity) => map.Reserve(capacity);

    public readonly void Sort() => map.Entries.Sort(new KeyComparer());

    public readonly bool KeysEqual(SortedLinearMap<K, V> other)
    {
        if (Count != other.Count)
            return false;

        var lhs = map.Entries;
        var rhs = other.map.Entries;

        for (int i = 0; i < lhs.Length; ++i)
        {
            if (!lhs[i].Key.Equals(rhs[i].Key))
                return false;
        }

        return true;
    }

    private struct KeyComparer : IComparer<LinearMap<K, V>.Entry>
    {
        public readonly int Compare(LinearMap<K, V>.Entry x, LinearMap<K, V>.Entry y) =>
            x.Key.CompareTo(y.Key);
    }

    #region IEnumerable
    public readonly LinearMap<K, V>.Enumerator GetEnumerator() => map.GetEnumerator();

    readonly IEnumerator<KeyValuePair<K, V>> IEnumerable<KeyValuePair<K, V>>.GetEnumerator() =>
        GetEnumerator();

    readonly IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    #endregion
}
