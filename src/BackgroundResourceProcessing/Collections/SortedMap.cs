using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using BackgroundResourceProcessing.Utils;

namespace BackgroundResourceProcessing.Collections;

/// <summary>
/// A dictionary data structure that stores the underlying entries in a sorted
/// list.
/// </summary>
/// <typeparam name="K"></typeparam>
/// <typeparam name="V"></typeparam>
[DebuggerVisualizer(typeof(SortedMap<,>.DebugView))]
public class SortedMap<K, V> : IEnumerable<KeyValuePair<K, V>>, IEquatable<SortedMap<K, V>>
    where K : IEquatable<K>, IComparable<K>
{
    KeyValuePair<K, V>[] entries;
    int count;

    public int Count => count;
    public int Capacity => entries.Length;

    public KeyEnumerator Keys => new(this);
    public ValueEnumerator Values => new(this);

    public KeyValuePair<K, V>[] Entries => entries;

    public V this[K key]
    {
        get
        {
            if (!TryGetIndex(key, out var index))
                throw new KeyNotFoundException($"Key not found in the map. Key: {key}");

            return entries[index].Value;
        }
    }

    #region Constructors
    private SortedMap(KeyValuePair<K, V>[] entries, int count)
    {
        this.entries = entries;
        this.count = count;

        if (count != 0)
            Array.Sort(entries, 0, count, KeyComparer.Instance);
    }

    public SortedMap()
        : this(16) { }

    public SortedMap(int capacity)
        : this(new KeyValuePair<K, V>[capacity], 0) { }

    public SortedMap(KeyValuePair<K, V>[] entries)
        : this(entries, entries.Length) { }

    public SortedMap(IEnumerable<KeyValuePair<K, V>> enumerable)
        : this([.. enumerable]) { }
    #endregion

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

    private bool TryGetIndex(K key, out int index)
    {
        index = Array.BinarySearch(entries, 0, count, new(key, default), KeyComparer.Instance);
        return index >= 0;
    }

    public void AddAll(IEnumerable<KeyValuePair<K, V>> enumerable)
    {
        AddAll(enumerable.GetEnumerator());
    }

    public void AddAll<T>(T enumerator)
        where T : IEnumerator<KeyValuePair<K, V>>
    {
        using var builder = CreateBuilder();
        builder.AddAll(enumerator);
    }

    public void Clear()
    {
        Array.Clear(entries, 0, count);
        count = 0;
    }

    public SortedMap<K, V> Clone()
    {
        return new((KeyValuePair<K, V>[])entries.Clone(), count);
    }

    private void Expand(int additional)
    {
        var newcap = Math.Max(entries.Length * 2, entries.Length + additional);
        Array.Resize(ref entries, newcap);
    }

    public bool KeysEqual(SortedMap<K, V> map)
    {
        if (count != map.count)
            return false;

        for (int i = 0; i < count; ++i)
        {
            if (!entries[i].Key.Equals(map.entries[i].Key))
                return false;
        }

        return true;
    }

    public bool Equals(SortedMap<K, V> map)
    {
        if (count != map.count)
            return false;

        var comparator = EqualityComparer<V>.Default;

        for (int i = 0; i < map.count; ++i)
        {
            var (k1, v1) = entries[i];
            var (k2, v2) = map.entries[i];

            if (!k1.Equals(k2) || !comparator.Equals(v1, v2))
                return false;
        }

        return true;
    }

    public override bool Equals(object obj)
    {
        if (ReferenceEquals(this, obj))
            return true;

        if (obj is SortedMap<K, V> map)
            return Equals(map);

        return false;
    }

    public override int GetHashCode()
    {
        HashCode hasher = new();
        hasher.Add(count);

        foreach (var (key, value) in this)
            hasher.Add(key, value);

        return hasher.GetHashCode();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Builder CreateBuilder()
    {
        return new(this);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Enumerator GetEnumerator()
    {
        return new(this);
    }

    IEnumerator<KeyValuePair<K, V>> IEnumerable<KeyValuePair<K, V>>.GetEnumerator()
    {
        return new Enumerator(this);
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return new Enumerator(this);
    }

    public struct Builder(SortedMap<K, V> map) : IDisposable
    {
        readonly SortedMap<K, V> map = map;
        int count = 0;

        private readonly int Count => map.Count + count;

        public void Add(K key, V value)
        {
            if (map.ContainsKey(key))
                throw new ArgumentException($"key {key} already exists in the map");

            for (int i = map.count; i < Count; ++i)
                if (map.entries[i].Key.Equals(key))
                    throw new ArgumentException($"key {key} already exists in the map");

            AddUnchecked(key, value);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void AddUnchecked(K key, V value)
        {
            if (Count == map.Capacity)
                map.Expand(count + 1);

            map.entries[Count] = new(key, value);
            count += 1;
        }

        public void AddAll(IEnumerable<KeyValuePair<K, V>> enumerable)
        {
            AddAll(enumerable.GetEnumerator());
        }

        public void AddAll<T>(T enumerator)
            where T : IEnumerator<KeyValuePair<K, V>>
        {
            while (enumerator.MoveNext())
            {
                var (key, value) = enumerator.Current;
                Add(key, value);
            }
        }

        public void Commit()
        {
            if (count != 0)
                Array.Sort(map.entries, 0, Count, KeyComparer.Instance);
            map.count = Count;
            count = 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Dispose()
        {
            Commit();
        }
    }

    private class KeyComparer : IComparer<KeyValuePair<K, V>>
    {
        public static readonly KeyComparer Instance = new();

        public int Compare(KeyValuePair<K, V> x, KeyValuePair<K, V> y)
        {
            return x.Key.CompareTo(y.Key);
        }
    }

    [method: MethodImpl(MethodImplOptions.AggressiveInlining)]
    public struct Enumerator(SortedMap<K, V> map) : IEnumerator<KeyValuePair<K, V>>
    {
        readonly SortedMap<K, V> map = map;
        int index = -1;

        public readonly KeyValuePair<K, V> Current
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get { return map.entries[index]; }
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
    public struct KeyEnumerator(SortedMap<K, V> map) : IEnumerator<K>
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
    public struct ValueEnumerator(SortedMap<K, V> map) : IEnumerator<V>
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

    private class DebugView
    {
        readonly KeyValuePair<K, V>[] items;

        [DebuggerBrowsable(DebuggerBrowsableState.RootHidden)]
        public KeyValuePair<K, V>[] Items => items;

        public DebugView(SortedMap<K, V> map)
        {
            items = new KeyValuePair<K, V>[map.Count];
            Array.Copy(map.entries, items, map.Count);
        }
    }
}
