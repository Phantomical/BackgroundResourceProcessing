using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;

namespace BackgroundResourceProcessing.Collections
{
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
    internal class IntMap<V>(int capacity) : IEnumerable<KVPair<int, V>>
    {
        int count = 0;
        readonly bool[] present = new bool[capacity];
        readonly V[] values = new V[capacity];

        public IEnumerable<int> Keys => new KeyEnumerable(this);

        public IEnumerable<V> Values => new ValueEnumerable(this);

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

        public bool ContainsKey(int key)
        {
            if (key < 0 || key > Capacity)
                throw new KeyNotFoundException();
            return present[key];
        }

        public void Add(int key, V value)
        {
            TryInsertThrow(key, value);
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
                throw new KeyNotFoundException();

            if (present[key])
                value = values[key];
            else
                value = default;
            return present[key];
        }

        public void Clear()
        {
            for (int i = 0; i < present.Length; ++i)
            {
                present[i] = false;
                values[i] = default;
            }

            count = 0;
        }

        public bool TryAdd(int key, V value)
        {
            if (key < 0 || key > Capacity)
                throw new ArgumentException("key was outside the bounds of the IntMap");

            if (present[key])
                return false;
            present[key] = true;
            values[key] = value;
            count += 1;
            return true;
        }

        public IEnumerator<KVPair<int, V>> GetEnumerator()
        {
            return new Enumerator(this);
        }

        IEnumerator<KVPair<int, V>> IEnumerable<KVPair<int, V>>.GetEnumerator()
        {
            return GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        private bool TryInsertOverwrite(int key, V value)
        {
            if (key < 0 || key > Capacity)
                throw new ArgumentException("key was outside the bounds of the IntMap");

            var wasPresent = present[key];
            present[key] = true;
            values[key] = value;
            if (!wasPresent)
                count += 1;
            return wasPresent;
        }

        private void TryInsertThrow(int key, V value)
        {
            if (key < 0 || key > Capacity)
                throw new ArgumentException("key was outside the bounds of the IntMap");

            if (present[key])
                throw new ArgumentException(
                    $"An item with the same key has already been added. Key: {key}"
                );
            present[key] = true;
            values[key] = value;
            count += 1;
        }

        private struct Enumerator(IntMap<V> map) : IEnumerator<KVPair<int, V>>
        {
            readonly IntMap<V> map = map;
            int index = -1;

            public readonly KVPair<int, V> Current
            {
                get
                {
                    if (index < 0 || index >= map.Capacity)
                        throw new InvalidOperationException(
                            "Enumeration has either not started yet or has already completed"
                        );

                    return new KVPair<int, V>(index, map.values[index]);
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

        private struct KeyEnumerator(IntMap<V> map) : IEnumerator<int>
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

        private struct ValueEnumerator(IntMap<V> map) : IEnumerator<V>
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

        private struct KeyEnumerable(IntMap<V> map) : IEnumerable<int>
        {
            IntMap<V> map = map;

            public IEnumerator<int> GetEnumerator()
            {
                return new KeyEnumerator(map);
            }

            IEnumerator IEnumerable.GetEnumerator()
            {
                return GetEnumerator();
            }
        }

        private struct ValueEnumerable(IntMap<V> map) : IEnumerable<V>
        {
            IntMap<V> map = map;

            public IEnumerator<V> GetEnumerator()
            {
                return new ValueEnumerator(map);
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
}
