using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using BackgroundResourceProcessing.Utils;

namespace BackgroundResourceProcessing.Collections;

/// <summary>
/// A dictionary data structure that stores the underlying entries in a sorted
/// list.
/// </summary>
/// <typeparam name="K"></typeparam>
/// <typeparam name="V"></typeparam>
[DebuggerTypeProxy(typeof(SortedMap<,>.DebugView))]
[DebuggerDisplay("Count = {Count}")]
public class SortedMap<K, V> : IEnumerable<KeyValuePair<K, V>>, IEquatable<SortedMap<K, V>>
    where K : IEquatable<K>, IComparable<K>
{
    [DebuggerDisplay("[{Key}, {Value}]")]
    public struct Entry(K key, V value)
    {
        public readonly K Key = key;
        public V Value = value;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly void Deconstruct(out K key, out V value)
        {
            key = Key;
            value = Value;
        }
    }

    private static readonly Entry[] Empty = [];

    Entry[] entries;
    int count;

    public int Count => count;
    public int Capacity => entries.Length;
    public bool IsEmpty => count == 0;

    public KeyEnumerator Keys => new(this);
    public ValueEnumerator Values => new(this);
    public EntryEnumerable Entries => new(this);

    public ref V this[K key]
    {
        get
        {
            if (!TryGetIndex(key, out var index))
                throw new KeyNotFoundException($"Key not found in the map. Key: {key}");

            return ref entries[index].Value;
        }
    }

    #region Constructors
    private SortedMap(Entry[] entries, int count)
    {
        this.entries = entries;
        this.count = count;

        if (count != 0)
            Array.Sort(entries, 0, count, KeyComparer.Instance);
    }

    public SortedMap()
        : this(Empty) { }

    public SortedMap(int capacity)
        : this(new Entry[capacity], 0) { }

    public SortedMap(Entry[] entries)
        : this(entries, entries.Length) { }

    public SortedMap(IEnumerable<Entry> enumerable)
        : this([.. enumerable]) { }

    public SortedMap(IEnumerable<KeyValuePair<K, V>> enumerable)
        : this(enumerable.Select(entry => new Entry(entry.Key, entry.Value))) { }
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

    public void Reserve(int capacity)
    {
        if (capacity <= Capacity)
            return;

        Expand(capacity - Capacity);
    }

    public SortedMap<K, V> Clone()
    {
        if (count == 0)
            return new();

        return new((Entry[])entries.Clone(), count);
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

        public void Set(K key, V value)
        {
            if (map.TryGetIndex(key, out var index))
            {
                map.entries[index].Value = value;
                return;
            }

            for (int i = map.count; i < Count; ++i)
            {
                if (!map.entries[i].Key.Equals(key))
                    continue;

                map.entries[i].Value = value;
                return;
            }

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

    private class KeyComparer : IComparer<Entry>
    {
        public static readonly KeyComparer Instance = new();

        public int Compare(Entry x, Entry y)
        {
            return x.Key.CompareTo(y.Key);
        }
    }

    [method: MethodImpl(MethodImplOptions.AggressiveInlining)]
    public struct Enumerator(SortedMap<K, V> map) : IEnumerator<KeyValuePair<K, V>>
    {
        EntryEnumerator enumerator = new(map);

        public readonly KeyValuePair<K, V> Current
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                var (key, value) = enumerator.Current;
                return new(key, value);
            }
        }

        readonly object IEnumerator.Current => Current;

        public bool MoveNext() => enumerator.MoveNext();

        public readonly void Dispose() => enumerator.Dispose();

        public void Reset() => enumerator.Reset();

        public readonly Enumerator GetEnumerator() => this;
    }

    [method: MethodImpl(MethodImplOptions.AggressiveInlining)]
    public struct KeyEnumerator(SortedMap<K, V> map) : IEnumerator<K>
    {
        EntryEnumerator enumerator = new(map);

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
        EntryEnumerator enumerator = new(map);

        public readonly ref V Current
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get { return ref enumerator.Current.Value; }
        }

        readonly V IEnumerator<V>.Current => Current;

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

    [method: MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly struct EntryEnumerable(SortedMap<K, V> map) : IEnumerable<Entry>
    {
        readonly SortedMap<K, V> map = map;

        public ref Entry this[int index]
        {
            [method: MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                if (index < 0 || index >= map.Count)
                    ThrowIndexOutOfRangeException(index);

                return ref map.entries[index];
            }
        }

        public int Count => map.Count;

        private void ThrowIndexOutOfRangeException(int index) =>
            throw new IndexOutOfRangeException(
                $"index {index} out of range for sorted map entries (expected index <= {map.Count})"
            );

        public EntryEnumerator GetEnumerator() => new(map);

        IEnumerator<Entry> IEnumerable<Entry>.GetEnumerator() => GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    public struct EntryEnumerator(SortedMap<K, V> map) : IEnumerator<Entry>
    {
        readonly SortedMap<K, V> map = map;
        int index = -1;

        public readonly ref Entry Current => ref map.entries[index];
        readonly Entry IEnumerator<Entry>.Current => Current;
        readonly object IEnumerator.Current => Current;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool MoveNext()
        {
            index += 1;
            return index < map.Count;
        }

        public void Reset()
        {
            index = -1;
        }

        public readonly void Dispose() { }
    }

    private class DebugView(SortedMap<K, V> map)
    {
        [DebuggerBrowsable(DebuggerBrowsableState.RootHidden)]
        public KeyValuePair<K, V>[] Items { get; } = [.. map];
    }
}
