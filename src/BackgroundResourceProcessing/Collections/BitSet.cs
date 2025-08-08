using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using BackgroundResourceProcessing.Utils;

namespace BackgroundResourceProcessing.Collections;

internal struct BitSet : IEnumerable<int>
{
    private static readonly ulong[] Empty = [];

    const int ULongBits = 64;

    public ulong[] Bits;

    public readonly bool this[uint key]
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

    public readonly bool this[int key]
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

    public readonly int Capacity => Bits.Length * ULongBits;

    public BitSet()
        : this(Empty) { }

    public BitSet(int capacity)
        : this(new ulong[(capacity + ULongBits - 1) / ULongBits]) { }

    private BitSet(ulong[] words)
    {
        Bits = words;
    }

    public readonly bool Contains(uint key)
    {
        if (key / ULongBits >= Bits.Length)
            return false;
        return this[key];
    }

    public readonly bool Contains(int key)
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

    public readonly void Clear()
    {
        Array.Clear(Bits, 0, Bits.Length);
    }

    public readonly int GetCount()
    {
        int count = 0;
        for (int i = 0; i < Bits.Length; ++i)
            count += MathUtil.PopCount(Bits[i]);
        return count;
    }

    /// <summary>
    /// Set all bits in this <see cref="BitSet"/>.
    /// </summary>
    public readonly void Fill()
    {
        for (int i = 0; i < Bits.Length; ++i)
            Bits[i] = ulong.MaxValue;
    }

    /// <summary>
    /// Unset all bits with index &gt;= <c><paramref name="index"/></c>.
    /// </summary>
    /// <param name="index"></param>
    public readonly void ClearUpFrom(int index)
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
    public readonly void ClearUpTo(int index)
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

    public readonly void ClearOutsideRange(int start, int end)
    {
        static void ThrowStartOutOfRange(int start) =>
            throw new ArgumentOutOfRangeException(
                nameof(start),
                $"start was negative (got {start})"
            );

        static void ThrowEndOutOfRange(int start, int end) =>
            throw new ArgumentOutOfRangeException(
                nameof(end),
                $"end was smaller than start ({end} < {start})"
            );

        static void ThrowIndexOutOfRange() =>
            throw new IndexOutOfRangeException("range went outside of the bounds of this bitset");

        if (start < 0)
            ThrowStartOutOfRange(start);
        if (end < start)
            ThrowEndOutOfRange(start, end);
        if (end > Capacity)
            ThrowIndexOutOfRange();

        uint sword = (uint)start / ULongBits;
        uint eword = (uint)end / ULongBits;
        uint sbit = (uint)start % ULongBits;
        uint ebit = (uint)end % ULongBits;

        for (uint i = 0; i < sword; ++i)
            Bits[i] = 0;

        if (sword >= Bits.Length)
            return;

        ulong smask = ulong.MaxValue << (int)sbit;
        ulong emask = (1ul << (int)ebit) - 1;

        Bits[sword] &= smask;

        if (eword >= Bits.Length)
            return;

        Bits[eword] &= emask;

        for (uint i = eword + 1; i < Bits.Length; ++i)
            Bits[i] = 0;
    }

    /// <summary>
    /// Set all bits up to <paramref name="index"/> (not inclusive).
    /// </summary>
    /// <param name="index"></param>
    /// <exception cref="IndexOutOfRangeException"></exception>
    public readonly void SetUpTo(int index)
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

    public readonly void CopyFrom(BitSet other)
    {
        if (other.Capacity != Capacity)
            throw new ArgumentException("Cannot copy from bitset with different length");

        for (int i = 0; i < Bits.Length; ++i)
            Bits[i] = other.Bits[i];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly void RemoveAll(BitSet other)
    {
        static void ThrowMismatchedSetCapacity() =>
            throw new ArgumentException(
                nameof(other),
                "bitset capacity did not match other bitset capacity"
            );

        if (Bits.Length != other.Bits.Length)
            ThrowMismatchedSetCapacity();

        for (int i = 0; i < Bits.Length; ++i)
            Bits[i] &= ~other.Bits[i];
    }

    public readonly void CopyInverseFrom(BitSet other)
    {
        if (other.Capacity != Capacity)
            throw new ArgumentException("Cannot copy from bitset with different length");

        for (int i = 0; i < Bits.Length; ++i)
            Bits[i] = ~other.Bits[i];
    }

    internal readonly BitSliceX AsSlice()
    {
        return new BitSliceX(Bits);
    }

    public readonly BitSet Clone()
    {
        return new((ulong[])Bits.Clone());
    }

    public void Resize(int newSize)
    {
        if (newSize < 0)
            throw new ArgumentOutOfRangeException(nameof(newSize));

        int newWords = (newSize + (ULongBits - 1)) / ULongBits;
        if (newWords == Bits.Length)
            return;

        Array.Resize(ref Bits, newWords);
    }

    public readonly Enumerator GetEnumerator()
    {
        return new(this);
    }

    public readonly Enumerator GetEnumeratorAt(int offset)
    {
        return new(this, offset);
    }

    readonly IEnumerator<int> IEnumerable<int>.GetEnumerator()
    {
        return new Enumerator(this);
    }

    readonly IEnumerator IEnumerable.GetEnumerator()
    {
        return ((IEnumerable<int>)this).GetEnumerator();
    }

    public struct Enumerator(BitSet set) : IEnumerator<int>
    {
        readonly ulong[] bits = set.Bits;
        BitEnumerator inner = default;
        int index = -1;

        public readonly int Current => inner.Current;

        readonly object IEnumerator.Current => Current;

        public Enumerator(BitSet set, int offset)
            : this(set)
        {
            if (offset < 0)
                throw new ArgumentOutOfRangeException(nameof(offset));

            int word = offset / ULongBits;
            int bit = offset % ULongBits;

            index = word;
            if (word >= bits.Length)
                return;

            inner = new(offset, bits[word] >> bit);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool MoveNext()
        {
            while (true)
            {
                if (inner.MoveNext())
                    return true;

                index += 1;
                if (index >= bits.Length)
                    return false;
                inner = new(index * ULongBits, bits[index]);
            }
        }

        public void Reset()
        {
            index = -1;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly void Dispose() { }
    }

    private sealed class DebugView(BitSet set)
    {
        private readonly BitSet set = set;

        [DebuggerBrowsable(DebuggerBrowsableState.RootHidden)]
        public int[] Items => [];
    }
}
