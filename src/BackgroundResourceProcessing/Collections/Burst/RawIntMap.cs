using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using Unity.Burst.CompilerServices;

#pragma warning disable CS8500 // This takes the address of, gets the size of, or declares a pointer to a managed type

namespace BackgroundResourceProcessing.Collections.Burst;

[DebuggerTypeProxy(typeof(RawIntMap<>.DebugView))]
[DebuggerDisplay("Capacity = {Capacity}")]
public struct RawIntMap<T> : IEnumerable<KeyValuePair<int, T>>
    where T : struct
{
    public struct Entry
    {
        public bool Present;
        public T Value;
    }

    RawArray<Entry> items;
    int count = 0;

    public readonly int Count => count;
    public readonly int Capacity => items.Count;
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

    public RawIntMap(int capacity, AllocatorHandle allocator) => items = new(capacity, allocator);

    public unsafe RawIntMap(Entry* items, int capacity) => this.items = new(items, capacity);

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

        count += 1;
        items[key] = new Entry { Present = true, Value = value };
    }

    public void Set(int key, T value)
    {
        if (key < 0 || key >= Capacity)
            ThrowKeyOutOfBoundsException(key);

        var prev = items[key];
        if (!prev.Present)
            count += 1;
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

        count -= 1;
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

        count -= 1;
        entry.Present = false;
        return true;
    }

    public void Clear()
    {
        count = 0;
        items.Fill(default);
    }

    public readonly int GetCount() => Count;

    [IgnoreWarning(1370)]
    static void ThrowKeyNotFoundException(int key) =>
        throw new KeyNotFoundException($"key {key} not found in the map");

    [IgnoreWarning(1370)]
    static void ThrowKeyOutOfBoundsException(int key) =>
        throw new IndexOutOfRangeException($"key {key} was outside of the int map range");

    [IgnoreWarning(1370)]
    static void ThrowKeyExistsException(int key) =>
        throw new ArgumentException($"key {key} already exists in the map");

    #region Enumerators
    public readonly Enumerator GetEnumerator() => new(this);

    public readonly Enumerator GetEnumeratorAt(int key) => new(this, key);

    readonly IEnumerator<KeyValuePair<int, T>> IEnumerable<KeyValuePair<int, T>>.GetEnumerator() =>
        GetEnumerator();

    readonly IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    [method: MethodImpl(MethodImplOptions.AggressiveInlining)]
    public struct Enumerator(RawIntMap<T> map, int offset = 0) : IEnumerator<KeyValuePair<int, T>>
    {
        readonly MemorySpan<Entry> span = map.items.Span;
        int index = offset - 1;

        public readonly KeyValuePair<int, T> Current
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => new(index, span[index].Value);
        }
        readonly object IEnumerator.Current => Current;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool MoveNext()
        {
            while (true)
            {
                index += 1;
                if (index >= span.Length)
                    return false;

                if (span[index].Present)
                    break;
            }

            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void IEnumerator.Reset() => throw new NotSupportedException();

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
