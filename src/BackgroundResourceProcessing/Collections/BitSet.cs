using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using BackgroundResourceProcessing.Utils;

namespace BackgroundResourceProcessing.Collections
{
    internal class BitSet(int capacity) : IEnumerable<int>
    {
        const int ULongBits = 64;

        readonly ulong[] words = new ulong[(capacity + ULongBits - 1) / ULongBits];

        public bool this[uint key]
        {
            get
            {
                if (key / ULongBits >= words.Length)
                    throw new IndexOutOfRangeException(
                        $"Key {key} was out of bounds for this BitSet"
                    );

                var word = key / ULongBits;
                var bit = key % ULongBits;

                return (words[word] & ((ulong)1 << (int)bit)) != 0;
            }
            set
            {
                if (key / ULongBits >= words.Length)
                    throw new IndexOutOfRangeException(
                        $"Key {key} was out of bounds for this BitSet"
                    );

                var word = key / ULongBits;
                var bit = key % ULongBits;
                var mask = 1ul << (int)bit;

                if (value)
                    words[word] |= mask;
                else
                    words[word] &= ~mask;
            }
        }

        public bool this[int key]
        {
            get
            {
                if (key < 0)
                    throw new IndexOutOfRangeException(
                        $"Key {key} was out of bounds for this BitSet"
                    );

                return this[(uint)key];
            }
            set
            {
                if (key < 0)
                    throw new IndexOutOfRangeException(
                        $"Key {key} was out of bounds for this BitSet"
                    );

                this[(uint)key] = value;
            }
        }

        public int Count => words.Length * ULongBits;

        public bool Contains(uint key)
        {
            if (key / ULongBits >= words.Length)
                return false;
            return this[key];
        }

        public bool Contains(int key)
        {
            if (key < 0)
                return false;
            return Contains((uint)key);
        }

        public void Add(uint key)
        {
            this[key] = true;
        }

        public void Add(int key)
        {
            this[key] = true;
        }

        public IEnumerator<int> GetEnumerator()
        {
            return new Enumerator(this);
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        private int GetNextSetIndex(int index)
        {
            index += 1;
            while (index < Count)
            {
                var word = index / ULongBits;
                var bit = index % ULongBits;

                var mask = ~((1ul << bit) - 1);
                var value = words[word] & mask;
                if (value == 0)
                {
                    index = (word + 1) * ULongBits;
                    continue;
                }

                return word * ULongBits + MathUtil.TrailingZeroCount(value);
            }

            return Count;
        }

        private struct Enumerator(BitSet set) : IEnumerator<int>
        {
            readonly BitSet set = set;
            int index = -1;

            public readonly int Current => index;

            readonly object IEnumerator.Current => Current;

            public bool MoveNext()
            {
                index = set.GetNextSetIndex(index);
                return index < set.Count;
            }

            public void Reset()
            {
                index = -1;
            }

            public void Dispose() { }
        }

        private sealed class DebugView(BitSet set)
        {
            private readonly BitSet set = set;

            [DebuggerBrowsable(DebuggerBrowsableState.RootHidden)]
            public int[] Items => [];
        }
    }
}
