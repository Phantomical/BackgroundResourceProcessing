using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using BackgroundResourceProcessing.Utils;

namespace BackgroundResourceProcessing.Collections;

public class DynamicBitSet : IEnumerable<int>
{
    const int ULongBits = 64;

    private ulong[] words;

    public bool this[uint key]
    {
        get
        {
            if (key / ULongBits >= words.Length)
                return false;

            var word = key / ULongBits;
            var bit = key % ULongBits;

            return (words[word] & ((ulong)1 << (int)bit)) != 0;
        }
        set
        {
            if (key / ULongBits >= words.Length)
            {
                if (!value)
                    return;

                Expand((int)(key / ULongBits) + 1);
            }

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
                throw new IndexOutOfRangeException($"Key {key} was out of bounds for this BitSet");

            return this[(uint)key];
        }
        set
        {
            if (key < 0)
                throw new IndexOutOfRangeException($"Key {key} was out of bounds for this BitSet");

            this[(uint)key] = value;
        }
    }

    public int Capacity => words.Length * ULongBits;
    public ulong[] Bits => words;

    public DynamicBitSet()
        : this(0) { }

    public DynamicBitSet(int capacity)
        : this(new ulong[(capacity + ULongBits - 1) / ULongBits]) { }

    private DynamicBitSet(ulong[] words)
    {
        this.words = words;
    }

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

    private void Expand(int required)
    {
        var newCap = Math.Max(words.Length * 2, required);
        var newWords = new ulong[newCap];

        for (int i = 0; i < words.Length; ++i)
            newWords[i] = words[i];

        words = newWords;
    }

    public DynamicBitSet Clone()
    {
        return new((ulong[])words.Clone());
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
        while (index < Capacity)
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

        return Capacity;
    }

    private struct Enumerator(DynamicBitSet set) : IEnumerator<int>
    {
        readonly DynamicBitSet set = set;
        int index = -1;

        public readonly int Current => index;

        readonly object IEnumerator.Current => Current;

        public bool MoveNext()
        {
            index = set.GetNextSetIndex(index);
            return index < set.Capacity;
        }

        public void Reset()
        {
            index = -1;
        }

        public void Dispose() { }
    }

    private sealed class DebugView(DynamicBitSet set)
    {
        private readonly DynamicBitSet set = set;

        [DebuggerBrowsable(DebuggerBrowsableState.RootHidden)]
        public int[] Items => [.. set];
    }
}
