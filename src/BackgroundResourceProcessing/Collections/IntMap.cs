using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using KSP.UI.Screens.Flight;

namespace BackgroundResourceProcessing.Collections;

/// <summary>
/// A map between small integers and their corresponding values.
/// </summary>
/// <typeparam name="V"></typeparam>
///
/// <remarks>
/// This is meant to be similar to <c>Dictionary&lt;int, V&gt;</c> except
/// that it performs fewer allocations since it gets allocated up-front.
/// </remarks>
[DebuggerTypeProxy(typeof(IntMap<>.DebugView))]
[DebuggerDisplay("Count = {Count}")]
internal class IntMap<V>(int capacity) : IEnumerable<KeyValuePair<int, V>>
{
    int count = 0;
    readonly bool[] present = new bool[capacity];
    readonly V[] values = new V[capacity];

    public KeyEnumerable Keys => new(this);

    public ValueEnumerable Values => new(this);

    public int Count => count;
    public int Capacity => present.Length;

    public V this[int key]
    {
        get
        {
            if (key < 0 || key > Capacity)
                throw new KeyNotFoundException($"Key {key} was out of bounds for this IntMap");
            if (!present[key])
                throw new KeyNotFoundException($"Key {key} was not present in the map");

            return values[key];
        }
        set { TryInsertOverwrite(key, value); }
    }

    public V this[uint key]
    {
        get
        {
            if (key > (uint)Capacity)
                throw new KeyNotFoundException($"Key {key} was out of bounds for this IntMap");
            if (!present[key])
                throw new KeyNotFoundException($"Key {key} was not present in the map");

            return values[key];
        }
        set { this[(int)key] = value; }
    }

    public bool ContainsKey(int key)
    {
        if (key < 0 || key > Capacity)
            throw new KeyNotFoundException();
        return present[key];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Add(int key, V value)
    {
        if (key < 0 || key >= Capacity)
            ThrowKeyOutOfBoundsException(key);

        if (present[key])
            ThrowDuplicateKeyException(key);

        present[key] = true;
        values[key] = value;
        count += 1;
    }

    public bool Remove(int key)
    {
        if (key < 0 || key > Capacity)
            throw new KeyNotFoundException();
        if (!present[key])
            return false;
        present[key] = false;
        values[key] = default;
        count -= 1;
        return true;
    }

    public bool TryGetValue(int key, out V value)
    {
        if (key < 0 || key > Capacity)
            ThrowKeyNotFoundException(key);
        if (present[key])
            value = values[key];
        else
            value = default;
        return present[key];
    }

    public void Clear()
    {
        Array.Clear(present, 0, present.Length);
        Array.Clear(values, 0, values.Length);
        count = 0;
    }

    public bool TryAdd(int key, V value)
    {
        if (key < 0 || key > Capacity)
            ThrowKeyOutOfBoundsException(key);

        if (present[key])
            return false;
        present[key] = true;
        values[key] = value;
        count += 1;
        return true;
    }

    public Enumerator GetEnumerator()
    {
        return new(this);
    }

    public Enumerator GetEnumeratorAt(int index)
    {
        return new(this, index);
    }

    IEnumerator<KeyValuePair<int, V>> IEnumerable<KeyValuePair<int, V>>.GetEnumerator()
    {
        return GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    private bool TryInsertOverwrite(int key, V value)
    {
        if (key < 0 || key >= Capacity)
            throw new ArgumentException("key was outside the bounds of the IntMap");

        var wasPresent = present[key];
        present[key] = true;
        values[key] = value;
        if (!wasPresent)
            count += 1;
        return wasPresent;
    }

    private void ThrowKeyNotFoundException(int key)
    {
        throw new KeyNotFoundException($"key {key} not found in the map");
    }

    private void ThrowKeyOutOfBoundsException(int key)
    {
        throw new ArgumentException("key was outside the bounds of the IntMap");
    }

    private void ThrowDuplicateKeyException(int key)
    {
        throw new ArgumentException(
            $"An item with the same key has already been added. Key: {key}"
        );
    }

    public struct Enumerator(IntMap<V> map) : IEnumerator<KeyValuePair<int, V>>
    {
        readonly IntMap<V> map = map;
        int index = -1;

        public readonly KeyValuePair<int, V> Current
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get { return new(index, map.values[index]); }
        }

        readonly object IEnumerator.Current => Current;

        public Enumerator(IntMap<V> map, int offset)
            : this(map)
        {
            if (offset < 0)
                throw new ArgumentOutOfRangeException("offset", "offset may not be negative");

            index = offset - 1;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool MoveNext()
        {
            while (true)
            {
                index += 1;

                if (index >= map.present.Length)
                    return false;

                if (map.present[index])
                    return true;
            }
        }

        public void Reset()
        {
            index = -1;
        }

        public readonly Enumerator GetEnumerator()
        {
            return this;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly void Dispose() { }
    }

    public struct KeyEnumerator(IntMap<V> map) : IEnumerator<int>
    {
        readonly IntMap<V> map = map;
        int index = -1;

        public readonly int Current
        {
            get
            {
                if (index < 0 || index >= map.Capacity)
                    throw new InvalidOperationException(
                        "Enumeration has either not started yet or has already completed"
                    );

                return index;
            }
        }

        readonly object IEnumerator.Current => Current;

        public bool MoveNext()
        {
            while (true)
            {
                index += 1;

                if (index >= map.Capacity)
                    return false;

                if (map.present[index])
                    return true;
            }
        }

        public void Reset()
        {
            index = -1;
        }

        public readonly void Dispose() { }
    }

    public struct ValueEnumerator(IntMap<V> map) : IEnumerator<V>
    {
        readonly IntMap<V> map = map;
        int index = -1;

        public readonly V Current
        {
            get
            {
                if (index < 0 || index >= map.Capacity)
                    throw new InvalidOperationException(
                        "Enumeration has either not started yet or has already completed"
                    );

                return map.values[index];
            }
        }

        readonly object IEnumerator.Current => Current;

        public bool MoveNext()
        {
            while (true)
            {
                index += 1;

                if (index >= map.Capacity)
                    return false;

                if (map.present[index])
                    return true;
            }
        }

        public void Reset()
        {
            index = -1;
        }

        public void Dispose() { }
    }

    public readonly struct KeyEnumerable(IntMap<V> map) : IEnumerable<int>
    {
        readonly IntMap<V> map = map;

        public KeyEnumerator GetEnumerator()
        {
            return new(map);
        }

        IEnumerator<int> IEnumerable<int>.GetEnumerator()
        {
            return GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }

    public readonly struct ValueEnumerable(IntMap<V> map) : IEnumerable<V>
    {
        readonly IntMap<V> map = map;

        public readonly ValueEnumerator GetEnumerator()
        {
            return new(map);
        }

        IEnumerator<V> IEnumerable<V>.GetEnumerator()
        {
            return GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }

    private sealed class DebugView(IntMap<V> map)
    {
        private readonly IntMap<V> map = map;

        [DebuggerBrowsable(DebuggerBrowsableState.RootHidden)]
        public KeyValuePair<int, V>[] Items
        {
            get
            {
                KeyValuePair<int, V>[] array = new KeyValuePair<int, V>[map.Count];
                int index = 0;
                foreach (var (key, value) in map)
                    array[index++] = new(key, value);
                return array;
            }
        }
    }
}
