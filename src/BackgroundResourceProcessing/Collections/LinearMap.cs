using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using BackgroundResourceProcessing.Utils;

namespace BackgroundResourceProcessing.Collections;

[DebuggerTypeProxy(typeof(LinearMap<,>.DebugView))]
[DebuggerDisplay("Count = {Count}")]
internal class LinearMap<K, V> : IEnumerable<KeyValuePair<K, V>>, IEquatable<LinearMap<K, V>>
    where K : IEquatable<K>
{
    public struct Pair(K key, V value)
    {
        public readonly K Key = key;
        public V Value = value;

        public readonly void Deconstruct(out K key, out V value)
        {
            key = Key;
            value = Value;
        }
    }

    Pair[] entries;
    int count;

    public int Count => count;
    public int Capacity => entries.Length;

    public KeyEnumerator Keys => new(this);
    public ValueEnumerator Values => new(this);

    public Pair[] Entries => entries;

    public ref V this[K key]
    {
        get
        {
            if (!TryGetIndex(key, out var index))
                throw new KeyNotFoundException($"Key not found in the map. Key: {key}");

            return ref entries[index].Value;
        }
    }

    private LinearMap(Pair[] entries, int count)
    {
        this.entries = entries;
        this.count = count;
    }

    private LinearMap(Pair[] entries)
        : this(entries, entries.Length) { }

    public LinearMap()
        : this(16) { }

    public LinearMap(int capacity)
        : this(new Pair[capacity], 0) { }

    public LinearMap(IEnumerable<Pair> enumerable)
        : this([.. enumerable]) { }

    public LinearMap(IEnumerable<KeyValuePair<K, V>> enumerable)
        : this(enumerable.Select(pair => new Pair(pair.Key, pair.Value))) { }

    public bool ContainsKey(K key)
    {
        return TryGetIndex(key, out var _);
    }

    public bool TryGetValue(K key, out V value)
    {
        if (TryGetIndex(key, out var index))
        {
            value = entries[index].Value;
            return true;
        }

        value = default;
        return false;
    }

    public V GetValueOr(K key, V defaultValue)
    {
        if (TryGetValue(key, out var value))
            return value;
        return defaultValue;
    }

    public V GetValueOrDefault(K key) => GetValueOr(key, default);

    private bool TryGetIndex(K key, out int index)
    {
        for (int i = 0; i < entries.Length; ++i)
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

    public void Add(K key, V value)
    {
        if (ContainsKey(key))
            throw new ArgumentException(
                $"An item with the same key already exists in the map. Key: {key}"
            );

        AddUnchecked(key, value);
    }

    public void AddUnchecked(K key, V value)
    {
        if (count == entries.Length)
            Expand(1);

        entries[count] = new(key, value);
        count += 1;
    }

    public bool Remove(K key)
    {
        if (!TryGetIndex(key, out var index))
            return false;

        if (index != count - 1)
            entries[index] = entries[count - 1];
        entries[count - 1] = default;
        count -= 1;
        return true;
    }

    public void Clear()
    {
        Array.Clear(entries, 0, count);
        count = 0;
    }

    private void Expand(int additional)
    {
        var newcap = Math.Max(entries.Length * 2, entries.Length + additional);
        Array.Resize(ref entries, newcap);
    }

    public void Sort()
    {
        Array.Sort(entries, 0, count, KeyComparer.Instance);
    }

    public bool KeysEqual(LinearMap<K, V> map)
    {
        if (Count != map.Count)
            return false;

        foreach (var key in Keys)
        {
            if (!map.ContainsKey(key))
                return false;
        }

        return true;
    }

    public bool Equals(LinearMap<K, V> map)
    {
        if (Count != map.Count)
            return false;

        var comparer = EqualityComparer<V>.Default;
        foreach (var (key, value) in this)
        {
            if (!map.TryGetValue(key, out var other))
                return false;

            if (!comparer.Equals(value, other))
                return false;
        }

        return true;
    }

    public override bool Equals(object obj)
    {
        if (obj is not LinearMap<K, V> map)
            return false;

        return Equals(map);
    }

    public override int GetHashCode()
    {
        HashCode hasher = new();

        hasher.Add(Count);
        foreach (var (key, value) in this)
            hasher.Add(key, value);

        return hasher.GetHashCode();
    }

    public Enumerator GetEnumerator() => new(this);

    IEnumerator<KeyValuePair<K, V>> IEnumerable<KeyValuePair<K, V>>.GetEnumerator()
    {
        return GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return entries.GetEnumerator();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Entry GetEntry(K key)
    {
        if (TryGetIndex(key, out var index))
            return new(this, index);

        if (Count == Capacity)
            Expand(1);

        entries[count] = new(key, default);
        return new(this, count);
    }

    public readonly struct Entry
    {
        readonly LinearMap<K, V> map;
        readonly int index;

        public readonly bool HasValue => index < map.Count;

        public readonly ref Pair Pair => ref map.entries[index];
        public readonly K Key
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get { return map.entries[index].Key; }
        }
        public readonly ref V Value
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get { return ref map.entries[index].Value; }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal Entry(LinearMap<K, V> map, int index)
        {
            this.map = map;
            this.index = index;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Insert(V value)
        {
            map.entries[index] = new(Key, value);
            if (index == map.count)
                map.count += 1;
        }
    }

    [method: MethodImpl(MethodImplOptions.AggressiveInlining)]
    public struct Enumerator(LinearMap<K, V> map) : IEnumerator<KeyValuePair<K, V>>
    {
        readonly LinearMap<K, V> map = map;
        int index = -1;

        public readonly KeyValuePair<K, V> Current
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                var pair = map.entries[index];
                return new(pair.Key, pair.Value);
            }
        }

        readonly object IEnumerator.Current => Current;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool MoveNext()
        {
            index += 1;
            return index < map.Count;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly void Dispose() { }

        public void Reset()
        {
            index = -1;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly Enumerator GetEnumerator()
        {
            return this;
        }
    }

    [method: MethodImpl(MethodImplOptions.AggressiveInlining)]
    public struct KeyEnumerator(LinearMap<K, V> map) : IEnumerator<K>
    {
        Enumerator enumerator = new(map);

        public readonly K Current
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get { return enumerator.Current.Key; }
        }

        readonly object IEnumerator.Current => Current;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool MoveNext()
        {
            return enumerator.MoveNext();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly void Dispose() { }

        public void Reset()
        {
            enumerator.Reset();
        }

        public readonly KeyEnumerator GetEnumerator()
        {
            return this;
        }
    }

    [method: MethodImpl(MethodImplOptions.AggressiveInlining)]
    public struct ValueEnumerator(LinearMap<K, V> map) : IEnumerator<V>
    {
        Enumerator enumerator = new(map);

        public readonly V Current
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get { return enumerator.Current.Value; }
        }

        readonly object IEnumerator.Current => Current;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool MoveNext()
        {
            return enumerator.MoveNext();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly void Dispose() { }

        public void Reset()
        {
            enumerator.Reset();
        }

        public readonly ValueEnumerator GetEnumerator()
        {
            return this;
        }
    }

    private class KeyComparer : IComparer<Pair>
    {
        public static readonly KeyComparer Instance = new();

        public int Compare(Pair x, Pair y)
        {
            return Comparer<K>.Default.Compare(x.Key, y.Key);
        }
    }

    private class DebugView
    {
        readonly KeyValuePair<K, V>[] items;

        [DebuggerBrowsable(DebuggerBrowsableState.RootHidden)]
        public KeyValuePair<K, V>[] Items => items;

        public DebugView(LinearMap<K, V> map)
        {
            items = new KeyValuePair<K, V>[map.Count];
            Array.Copy(map.entries, items, map.Count);
        }
    }
}
