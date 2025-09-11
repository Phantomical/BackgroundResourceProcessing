using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using Unity.Burst.CompilerServices;

namespace BackgroundResourceProcessing.Collections.Burst;

[DebuggerDisplay("Count = {Count}")]
[DebuggerTypeProxy(typeof(DictionaryDebugView<,>))]
internal struct LinearMap<K, V> : IEnumerable<KeyValuePair<K, V>>
    where K : unmanaged, IEquatable<K>
    where V : unmanaged
{
    public struct Entry(K key, V value)
    {
        public K Key = key;
        public V Value = value;

        public Entry(KeyValuePair<K, V> pair)
            : this(pair.Key, pair.Value) { }

        public static implicit operator KeyValuePair<K, V>(Entry entry) =>
            new(entry.Key, entry.Value);

        public readonly void Deconstruct(out K key, out V value)
        {
            key = Key;
            value = Value;
        }

        public override readonly string ToString()
        {
            return $"[{Key}, {Value}]";
        }
    }

    RawList<Entry> entries;

    public readonly int Count => entries.Count;
    public readonly int Capacity => entries.Capacity;
    public readonly MemorySpan<Entry> Entries => entries.Span;

    public readonly AllocatorHandle Allocator => entries.Allocator;
    public readonly bool IsEmpty => Count == 0;

    public readonly KeyEnumerable Keys => new(this);
    public readonly ValueEnumerable Values => new(this);

    public readonly ref V this[K key]
    {
        [IgnoreWarning(1370)]
        get
        {
            if (!TryGetIndex(key, out var index))
                throw new KeyNotFoundException("key not found in the linear map");

            return ref entries[index].Value;
        }
    }

    public readonly ref Entry GetEntryAtIndex(int index) => ref entries[index];

    public readonly ref V GetAtIndex(int index) => ref GetEntryAtIndex(index).Value;

    LinearMap(RawList<Entry> entries)
    {
        this.entries = entries;
    }

    public LinearMap(int capacity, AllocatorHandle allocator)
        : this(new RawList<Entry>(capacity, allocator)) { }

    public readonly bool ContainsKey(K key) => TryGetIndex(key, out var _);

    public readonly bool TryGetValue(K key, out V value)
    {
        if (TryGetIndex(key, out int index))
        {
            value = entries[index].Value;
            return true;
        }
        else
        {
            value = default;
            return false;
        }
    }

    public readonly bool TryGetIndex(K key, out int index)
    {
        for (int i = 0; i < entries.Count; ++i)
        {
            if (entries[i].Key.Equals(key))
            {
                index = i;
                return true;
            }
        }

        index = -1;
        return false;
    }

    public readonly V GetValueOr(K key, V defaultValue) =>
        TryGetValue(key, out var value) ? value : defaultValue;

    public readonly V GetValueOrDefault(K key) => GetValueOr(key, default);

    public void Add(K key, V value)
    {
        if (ContainsKey(key))
            throw new ArgumentException("an item with the same key already exists in the map");
        AddUnchecked(key, value);
    }

    public void AddUnchecked(K key, V value) => entries.Push(new(key, value));

    public bool Remove(K key)
    {
        if (!TryGetIndex(key, out var index))
            return false;

        entries.RemoveAtSwapBack(index);
        return true;
    }

    public void Clear() => entries.Clear();

    public void Reserve(int capacity) => entries.Reserve(capacity);

    public readonly bool KeysEqual(LinearMap<K, V> map)
    {
        if (Count != map.Count)
            return false;

        foreach (ref var entry in entries)
        {
            if (!map.ContainsKey(entry.Key))
                return false;
        }

        return true;
    }

    #region Enumerator
    public readonly Enumerator GetEnumerator() => new(this);

    readonly IEnumerator<KeyValuePair<K, V>> IEnumerable<KeyValuePair<K, V>>.GetEnumerator() =>
        GetEnumerator();

    readonly IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public struct Enumerator(LinearMap<K, V> map) : IEnumerator<KeyValuePair<K, V>>
    {
        RawList<Entry>.Enumerator inner = map.entries.GetEnumerator();

        public readonly KeyValuePair<K, V> Current => (KeyValuePair<K, V>)inner.Current;
        readonly object IEnumerator.Current => Current;

        public bool MoveNext() => inner.MoveNext();

        void IEnumerator.Reset() => throw new NotSupportedException();

        public void Dispose() => inner.Dispose();
    }

    public struct KeyEnumerator(LinearMap<K, V> map) : IEnumerator<K>
    {
        RawList<Entry>.Enumerator inner = map.entries.GetEnumerator();

        public readonly ref K Current => ref inner.Current.Key;
        readonly K IEnumerator<K>.Current => Current;
        readonly object IEnumerator.Current => Current;

        public bool MoveNext() => inner.MoveNext();

        void IEnumerator.Reset() => throw new NotSupportedException();

        public void Dispose() => inner.Dispose();
    }

    public struct ValueEnumerator(LinearMap<K, V> map) : IEnumerator<V>
    {
        RawList<Entry>.Enumerator inner = map.entries.GetEnumerator();

        public readonly ref V Current => ref inner.Current.Value;
        readonly V IEnumerator<V>.Current => Current;
        readonly object IEnumerator.Current => Current;

        public bool MoveNext() => inner.MoveNext();

        void IEnumerator.Reset() => throw new NotSupportedException();

        public void Dispose() => inner.Dispose();
    }

    public readonly struct KeyEnumerable(LinearMap<K, V> map) : IEnumerable<K>
    {
        public KeyEnumerator GetEnumerator() => new(map);

        IEnumerator<K> IEnumerable<K>.GetEnumerator() => GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    public readonly struct ValueEnumerable(LinearMap<K, V> map) : IEnumerable<V>
    {
        public ValueEnumerator GetEnumerator() => new(map);

        IEnumerator<V> IEnumerable<V>.GetEnumerator() => GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
    #endregion
}

internal static class LinearMapExtensions
{
    internal static LinearMap<K, V> Create<K, V>(SortedMap<K, V> map, AllocatorHandle allocator)
        where K : unmanaged, IEquatable<K>, IComparable<K>
        where V : unmanaged
    {
        var res = new LinearMap<K, V>(map.Count, allocator);
        foreach (var (key, value) in map)
            res.AddUnchecked(key, value);
        return res;
    }

    internal static bool Equals<K, V>(in this LinearMap<K, V> a, in LinearMap<K, V> b)
        where K : unmanaged, IEquatable<K>
        where V : unmanaged, IEquatable<V>
    {
        if (a.Count != b.Count)
            return false;

        for (int i = 0; i < a.Count; ++i)
        {
            ref var ae = ref a.GetEntryAtIndex(i);
            if (!b.TryGetIndex(ae.Key, out var j))
                return false;

            ref var be = ref b.GetEntryAtIndex(j);

            if (!ae.Value.Equals(be.Value))
                return false;
        }

        return true;
    }
}
