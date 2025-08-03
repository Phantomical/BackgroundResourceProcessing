using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using BackgroundResourceProcessing.Utils;

namespace BackgroundResourceProcessing.Collections;

public class BitSet : IEnumerable<int>
{
    const int ULongBits = 64;

    public readonly ulong[] Bits;

    public bool this[uint key]
    {
        get
        {
            if (key / ULongBits >= Bits.Length)
                throw new IndexOutOfRangeException($"Key {key} was out of bounds for this BitSet");

            var word = key / ULongBits;
            var bit = key % ULongBits;

            return (Bits[word] & ((ulong)1 << (int)bit)) != 0;
        }
        set
        {
            if (key / ULongBits >= Bits.Length)
                throw new IndexOutOfRangeException($"Key {key} was out of bounds for this BitSet");

            var word = key / ULongBits;
            var bit = key % ULongBits;
            var mask = 1ul << (int)bit;

            if (value)
                Bits[word] |= mask;
            else
                Bits[word] &= ~mask;
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

    public int Capacity => Bits.Length * ULongBits;

    public BitSet(int capacity)
        : this(new ulong[(capacity + ULongBits - 1) / ULongBits]) { }

    private BitSet(ulong[] words)
    {
        Bits = words;
    }

    public bool Contains(uint key)
    {
        if (key / ULongBits >= Bits.Length)
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

    public void Clear()
    {
        new Span<ulong>(Bits).Fill(0);
    }

    /// <summary>
    /// Set all bits in this <see cref="BitSet"/>.
    /// </summary>
    public void Fill()
    {
        new Span<ulong>(Bits).Fill(ulong.MaxValue);
    }

    /// <summary>
    /// Unset all bits with index &gt;= <c><paramref name="index"/></c>.
    /// </summary>
    /// <param name="index"></param>
    public void ClearUpFrom(int index)
    {
        if (index < 0)
            throw new IndexOutOfRangeException("selected bit index was negative");

        var word = index / ULongBits;
        var bit = index % ULongBits;
        var mask = (1ul << bit) - 1;

        if (word >= Bits.Length)
            return;

        Bits[word] &= mask;

        for (int i = word + 1; i < Bits.Length; ++i)
            Bits[i] = 0;
    }

    /// <summary>
    /// Unset all bits with index &lt;= <c><paramref name="index"/></c>.
    /// </summary>
    /// <param name="index"></param>
    public void ClearUpTo(int index)
    {
        if (index < 0)
            throw new IndexOutOfRangeException("selected bit index was negative");

        var word = index / ULongBits;
        var bit = index % ULongBits;
        var mask = ~((1ul << bit) - 1);

        word = Math.Min(word, Bits.Length);

        for (int i = 0; i < word; ++i)
            Bits[i] = 0;

        if (word < Bits.Length)
            Bits[word] &= mask;
    }

    /// <summary>
    /// Set all bits up to <paramref name="index"/> (not inclusive).
    /// </summary>
    /// <param name="index"></param>
    /// <exception cref="IndexOutOfRangeException"></exception>
    public void SetUpTo(int index)
    {
        if (index < 0 || index > Capacity)
            throw new IndexOutOfRangeException(
                $"selected bit index was out of range ({index} >= {Capacity})"
            );

        var word = index / ULongBits;
        var bit = index % ULongBits;
        var mask = (1ul << bit) - 1;

        for (int i = 0; i < word; ++i)
            Bits[i] = ulong.MaxValue;

        if (word < Bits.Length)
            Bits[word] |= mask;
    }

    public void CopyFrom(BitSet other)
    {
        if (other.Capacity != Capacity)
            throw new ArgumentException("Cannot copy from bitset with different length");

        for (int i = 0; i < Bits.Length; ++i)
            Bits[i] = other.Bits[i];
    }

    public void CopyInverseFrom(BitSet other)
    {
        if (other.Capacity != Capacity)
            throw new ArgumentException("Cannot copy from bitset with different length");

        for (int i = 0; i < Bits.Length; ++i)
            Bits[i] = ~other.Bits[i];
    }

    internal BitSliceX AsSlice()
    {
        return new BitSliceX(Bits);
    }

    public BitSet Clone()
    {
        return new((ulong[])Bits.Clone());
    }

    RefEnumerator GetEnumerator()
    {
        return new(this);
    }

    IEnumerator<int> IEnumerable<int>.GetEnumerator()
    {
        return new Enumerator(this);
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return ((IEnumerable<int>)this).GetEnumerator();
    }

    private int GetNextSetIndex(int index)
    {
        index += 1;
        while (index < Capacity)
        {
            var word = index / ULongBits;
            var bit = index % ULongBits;

            var mask = ~((1ul << bit) - 1);
            var value = Bits[word] & mask;
            if (value == 0)
            {
                index = (word + 1) * ULongBits;
                continue;
            }

            return word * ULongBits + MathUtil.TrailingZeroCount(value);
        }

        return Capacity;
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
            return index < set.Capacity;
        }

        public void Reset()
        {
            index = -1;
        }

        public void Dispose() { }
    }

    private ref struct RefEnumerator(BitSet set) : IEnumerator<int>
    {
        readonly BitSet set = set;
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

        public readonly RefEnumerator GetEnumerator() => this;
    }

    private sealed class DebugView(BitSet set)
    {
        private readonly BitSet set = set;

        [DebuggerBrowsable(DebuggerBrowsableState.RootHidden)]
        public int[] Items => [];
    }
}
