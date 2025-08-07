using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace BackgroundResourceProcessing.Collections;

[DebuggerTypeProxy(typeof(RefIntMap<>.DebugView))]
[DebuggerDisplay("Capacity = {Capacity}")]
internal readonly struct RefIntMap<V>(int capacity) : IEnumerable<KeyValuePair<int, V>>
{
    readonly BitSet present = new(capacity);
    readonly V[] values = new V[capacity];

    public int Capacity => values.Length;

    public KeyEnumerable Keys => new(this);
    public ValueEnumerable Values => new(this);

    public ref V this[int key]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            if (key < 0 || key >= Capacity)
                ThrowKeyOutOfBoundsException(key);
            if (!present[key])
                ThrowKeyNotFoundException(key);

            return ref values[key];
        }
    }

    public bool TryGetValue(int key, out V value)
    {
        if (key < 0 || key >= Capacity)
            ThrowKeyOutOfBoundsException(key);

        value = values[key];
        return present[key];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool ContainsKey(int key)
    {
        return present.Contains(key);
    }

    public void Add(int key, V value)
    {
        if (key < 0 || key >= Capacity)
            ThrowKeyOutOfBoundsException(key);
        if (present[key])
            ThrowKeyExistsException(key);

        present[key] = true;
        values[key] = value;
    }

    public void Set(int key, V value)
    {
        if (key < 0 || key >= Capacity)
            ThrowKeyOutOfBoundsException(key);

        present[key] = true;
        values[key] = value;
    }

    public bool Remove(int key)
    {
        if (key < 0 || key >= Capacity)
            ThrowKeyOutOfBoundsException(key);

        bool prev = present[key];
        present[key] = false;
        values[key] = default;
        return prev;
    }

    public void Clear()
    {
        present.Clear();
        Array.Clear(values, 0, values.Length);
    }

    public readonly Enumerator GetEnumerator() => new(this);

    public readonly Enumerator GetEnumeratorAt(int key) => new(this, key);

    readonly IEnumerator<KeyValuePair<int, V>> IEnumerable<KeyValuePair<int, V>>.GetEnumerator() =>
        GetEnumerator();

    readonly IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    static void ThrowKeyNotFoundException(int key) =>
        throw new KeyNotFoundException($"key {key} not found in the map");

    static void ThrowKeyOutOfBoundsException(int key) =>
        throw new IndexOutOfRangeException($"key {key} was outside of the int map range");

    static void ThrowKeyExistsException(int key) =>
        throw new ArgumentException($"key {key} already exists in the map");

    public struct Enumerator : IEnumerator<KeyValuePair<int, V>>
    {
        BitSet.Enumerator enumerator;
        readonly V[] values;

        public readonly KeyValuePair<int, V> Current
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                var index = enumerator.Current;
                return new(index, values[index]);
            }
        }
        readonly object IEnumerator.Current => Current;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Enumerator(RefIntMap<V> map)
        {
            enumerator = map.present.GetEnumerator();
            values = map.values;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Enumerator(RefIntMap<V> map, int offset)
        {
            enumerator = map.present.GetEnumeratorAt(offset);
            values = map.values;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool MoveNext() => enumerator.MoveNext();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Reset() => enumerator.Reset();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly void Dispose() => enumerator.Dispose();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly Enumerator GetEnumerator() => this;
    }

    public readonly struct KeyEnumerable(RefIntMap<V> map) : IEnumerable<int>
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public KeyEnumerator GetEnumerator() => new(map);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public KeyEnumerator GetEnumeratorAt(int offset) => new(map, offset);

        IEnumerator<int> IEnumerable<int>.GetEnumerator() => GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    public readonly struct ValueEnumerable(RefIntMap<V> map) : IEnumerable<V>
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ValueEnumerator GetEnumerator() => new(map);

        IEnumerator<V> IEnumerable<V>.GetEnumerator() => GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    public struct KeyEnumerator : IEnumerator<int>
    {
        BitSet.Enumerator enumerator;

        public readonly int Current => enumerator.Current;
        readonly object IEnumerator.Current => Current;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public KeyEnumerator(RefIntMap<V> map)
        {
            enumerator = map.present.GetEnumerator();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public KeyEnumerator(RefIntMap<V> map, int offset)
        {
            enumerator = map.present.GetEnumeratorAt(offset);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool MoveNext() => enumerator.MoveNext();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly void Dispose() => enumerator.Dispose();

        public void Reset() => enumerator.Reset();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly KeyEnumerator GetEnumerator() => this;
    }

    [method: MethodImpl(MethodImplOptions.AggressiveInlining)]
    public struct ValueEnumerator(RefIntMap<V> map) : IEnumerator<V>
    {
        BitSet.Enumerator enumerator = map.present.GetEnumerator();
        readonly V[] values = map.values;

        public readonly V Current => values[enumerator.Current];
        readonly object IEnumerator.Current => Current;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool MoveNext() => enumerator.MoveNext();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly void Dispose() => enumerator.Dispose();

        public void Reset() => enumerator.Reset();
    }

    private sealed class DebugView(RefIntMap<V> map)
    {
        [DebuggerBrowsable(DebuggerBrowsableState.RootHidden)]
        public KeyValuePair<int, V>[] Items { get; } = [.. map];
    }
}
