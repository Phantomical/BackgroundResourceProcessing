using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Unity.Collections;

namespace BackgroundResourceProcessing.Collections.Burst;

[DebuggerTypeProxy(typeof(RawIntMap<>.DebugView))]
[DebuggerDisplay("Capacity = {Capacity}")]
public struct RawIntMap<T>(int capacity, Allocator allocator)
    : IEnumerable<KeyValuePair<int, T>>,
        IDisposable
    where T : struct
{
    struct Entry : IDisposable
    {
        public bool Present;
        public T Value;

        public void Dispose()
        {
            if (Present)
                ItemDisposer<T>.Dispose(ref Value);
            Present = false;
        }
    }

    RawArray<Entry> items = new(capacity, allocator);

    public readonly int Capacity => items.Count;
    public readonly Allocator Allocator => items.Allocator;

    public readonly KeyEnumerable Keys => new(this);
    public readonly ValueEnumerable Values => new(this);

    public readonly ref T this[int key]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            if (key < 0 || key >= Capacity)
                ThrowKeyOutOfBoundsException(key);
            if (!items[key].Present)
                ThrowKeyNotFoundException(key);

            return ref items[key].Value;
        }
    }

    public readonly bool TryGetValue(int key, out T value)
    {
        if (key < 0 || key >= Capacity)
        {
            value = default;
            return false;
        }

        ref var entry = ref items[key];
        value = entry.Value;
        return entry.Present;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly bool ContainsKey(int key)
    {
        return key >= 0 && key < Capacity && items[key].Present;
    }

    public void Add(int key, T value)
    {
        if (key < 0 || key >= Capacity)
            ThrowKeyOutOfBoundsException(key);
        if (items[key].Present)
            ThrowKeyExistsException(key);

        items[key] = new Entry { Present = true, Value = value };
    }

    public void Set(int key, T value)
    {
        if (key < 0 || key >= Capacity)
            ThrowKeyOutOfBoundsException(key);

        using var prev = items[key];
        items[key] = new Entry { Present = true, Value = value };
    }

    public bool TryRemove(int key, out T value)
    {
        if (key < 0 || key >= Capacity)
        {
            value = default;
            return false;
        }

        ref var entry = ref items[key];
        if (!entry.Present)
        {
            value = default;
            return false;
        }

        value = entry.Value;
        entry.Present = false;
        return true;
    }

    public bool Remove(int key)
    {
        if (key < 0 || key >= Capacity)
            return false;

        ref var entry = ref items[key];
        if (!entry.Present)
            return false;

        entry.Dispose();
        return true;
    }

    public readonly void Clear()
    {
        ItemDisposer<Entry>.DisposeRange(items);
        items.Fill(default);
    }

    public readonly int GetCount()
    {
        int count = 0;
        for (int i = 0; i < Capacity; i++)
        {
            if (items[i].Present)
                count++;
        }
        return count;
    }

    public void Dispose()
    {
        Clear(); // Dispose all entries first
        items.Dispose();
    }

    static void ThrowKeyNotFoundException(int key) =>
        throw new KeyNotFoundException($"key {key} not found in the map");

    static void ThrowKeyOutOfBoundsException(int key) =>
        throw new IndexOutOfRangeException($"key {key} was outside of the int map range");

    static void ThrowKeyExistsException(int key) =>
        throw new ArgumentException($"key {key} already exists in the map");

    #region Entry
    public readonly EntryRef GetEntry(int key)
    {
        if (key < 0 || key >= Capacity)
            ThrowKeyOutOfBoundsException(key);

        return new(this, key);
    }

    public readonly struct EntryRef
    {
        readonly RawIntMap<T> map;
        readonly int key;

        public bool HasValue => map.items[key].Present;
        public ref T Value
        {
            get
            {
                if (!HasValue)
                    ThrowKeyNotFoundException(key);
                return ref map.items[key].Value;
            }
        }

        public EntryRef(RawIntMap<T> map, int key)
        {
            if (key < 0 || key >= map.Capacity)
                ThrowKeyOutOfBoundsException(key);

            this.map = map;
            this.key = key;
        }

        public ref T Insert(T value)
        {
            map.items[key].Dispose(); // Dispose existing entry if present
            map.items[key] = new Entry { Present = true, Value = value };
            return ref map.items[key].Value;
        }
    }
    #endregion

    #region Enumerators
    public readonly Enumerator GetEnumerator() => new(this);

    public readonly Enumerator GetEnumeratorAt(int key) => new(this, key);

    readonly IEnumerator<KeyValuePair<int, T>> IEnumerable<KeyValuePair<int, T>>.GetEnumerator() =>
        GetEnumerator();

    readonly IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public struct Enumerator : IEnumerator<KeyValuePair<int, T>>
    {
        readonly RawIntMap<T> map;
        int index;

        public readonly KeyValuePair<int, T> Current
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => new(index, map.items[index].Value);
        }
        readonly object IEnumerator.Current => Current;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Enumerator(RawIntMap<T> map)
        {
            this.map = map;
            index = -1;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Enumerator(RawIntMap<T> map, int offset)
        {
            this.map = map;
            index = offset - 1;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool MoveNext()
        {
            while (++index < map.Capacity)
            {
                if (map.items[index].Present)
                    return true;
            }
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Reset() => index = -1;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly void Dispose() { }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly Enumerator GetEnumerator() => this;
    }

    public readonly struct KeyEnumerable(RawIntMap<T> map) : IEnumerable<int>
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public KeyEnumerator GetEnumerator() => new(map);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public KeyEnumerator GetEnumeratorAt(int offset) => new(map, offset);

        IEnumerator<int> IEnumerable<int>.GetEnumerator() => GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    public readonly struct ValueEnumerable(RawIntMap<T> map) : IEnumerable<T>
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ValueEnumerator GetEnumerator() => new(map);

        IEnumerator<T> IEnumerable<T>.GetEnumerator() => GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    public struct KeyEnumerator : IEnumerator<int>
    {
        readonly RawIntMap<T> map;
        int index;

        public readonly int Current => index;
        readonly object IEnumerator.Current => Current;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public KeyEnumerator(RawIntMap<T> map)
        {
            this.map = map;
            index = -1;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public KeyEnumerator(RawIntMap<T> map, int offset)
        {
            this.map = map;
            index = offset - 1;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool MoveNext()
        {
            while (++index < map.Capacity)
            {
                if (map.items[index].Present)
                    return true;
            }
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly void Dispose() { }

        public void Reset() => index = -1;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly KeyEnumerator GetEnumerator() => this;
    }

    [method: MethodImpl(MethodImplOptions.AggressiveInlining)]
    public struct ValueEnumerator(RawIntMap<T> map) : IEnumerator<T>
    {
        readonly RawIntMap<T> map = map;
        int index = -1;

        public readonly ref T Current => ref map.items[index].Value;
        readonly T IEnumerator<T>.Current => Current;
        readonly object IEnumerator.Current => Current;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool MoveNext()
        {
            while (++index < map.Capacity)
            {
                if (map.items[index].Present)
                    return true;
            }
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly void Dispose() { }

        public void Reset() => index = -1;
    }
    #endregion

    private sealed class DebugView(RawIntMap<T> map)
    {
        [DebuggerBrowsable(DebuggerBrowsableState.RootHidden)]
        public KeyValuePair<int, T>[] Items { get; } = [.. map];
    }
}
