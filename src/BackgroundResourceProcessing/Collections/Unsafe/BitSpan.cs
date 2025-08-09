using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using BackgroundResourceProcessing.Utils;
using Unity.Burst.CompilerServices;

namespace BackgroundResourceProcessing.Collections.Unsafe;

[DebuggerTypeProxy(typeof(DebugView))]
[DebuggerDisplay("Capacity = {Capacity}")]
internal readonly unsafe struct BitSpan(MemorySpan<ulong> bits)
{
    const int ULongBits = 64;

    public readonly MemorySpan<ulong> Bits = bits;

    public uint Capacity => Bits.Length * ULongBits;

    public bool this[uint key]
    {
        get
        {
            if (key / ULongBits >= Bits.Length)
                ThrowIndexOutOfRangeException();

            var word = key / ULongBits;
            var bit = key % ULongBits;

            return (Bits[word] & (1ul << (int)bit)) != 0;
        }
        set
        {
            if (key / ULongBits >= Bits.Length)
                ThrowIndexOutOfRangeException();

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
                ThrowIndexOutOfRangeException();
            return this[(uint)key];
        }
        set
        {
            if (key < 0)
                ThrowIndexOutOfRangeException();
            this[(uint)key] = value;
        }
    }

    public BitSpan(ulong* bits, uint length)
        : this(new MemorySpan<ulong>(bits, length)) { }

    [IgnoreWarning(1370)]
    static void ThrowIndexOutOfRangeException() =>
        throw new IndexOutOfRangeException("index out of range for bitset");

    public bool Contains(uint key)
    {
        if (key / ULongBits >= Bits.Length)
            return false;
        return this[key];
    }

    public void Add(uint key)
    {
        this[key] = true;
    }

    public void Clear()
    {
        Bits.Fill(0);
    }

    public void Fill()
    {
        Bits.Fill(ulong.MaxValue);
    }

    /// <summary>
    /// Unset all bits with index &gt;= <c><paramref name="index"/></c>.
    /// </summary>
    /// <param name="index"></param>
    [IgnoreWarning(1370)]
    public void ClearUpFrom(uint index)
    {
        if (index < 0)
            throw new IndexOutOfRangeException("selected bit index was negative");

        var word = index / ULongBits;
        var bit = index % ULongBits;
        var mask = (1ul << (int)bit) - 1;

        if (word >= Bits.Length)
            return;

        Bits[word] &= mask;

        for (uint i = word + 1; i < Bits.Length; ++i)
            Bits[i] = 0;
    }

    /// <summary>
    /// Unset all bits with index &lt;= <c><paramref name="index"/></c>.
    /// </summary>
    /// <param name="index"></param>
    [IgnoreWarning(1370)]
    public void ClearUpTo(uint index)
    {
        if (index < 0)
            throw new IndexOutOfRangeException("selected bit index was negative");

        var word = index / ULongBits;
        var bit = index % ULongBits;
        var mask = ~((1ul << (int)bit) - 1);

        word = Math.Min(word, Bits.Length);

        for (uint i = 0; i < word; ++i)
            Bits[i] = 0;

        if (word < Bits.Length)
            Bits[word] &= mask;
    }

    /// <summary>
    /// Set all bits up to <paramref name="index"/> (not inclusive).
    /// </summary>
    /// <param name="index"></param>
    /// <exception cref="IndexOutOfRangeException"></exception>
    [IgnoreWarning(1370)]
    public void SetUpTo(uint index)
    {
        if (index > Capacity)
            throw new IndexOutOfRangeException(
                $"selected bit index was out of range ({index} >= {Capacity})"
            );

        var word = index / ULongBits;
        var bit = index % ULongBits;
        var mask = (1ul << (int)bit) - 1;

        for (uint i = 0; i < word; ++i)
            Bits[i] = ulong.MaxValue;

        if (word < Bits.Length)
            Bits[word] |= mask;
    }

    [IgnoreWarning(1370)]
    public void CopyFrom(BitSpan other)
    {
        if (other.Capacity != Capacity)
            throw new ArgumentException("Cannot copy from bitset with different length");

        for (uint i = 0; i < Bits.Length; ++i)
            Bits[i] = other.Bits[i];
    }

    [IgnoreWarning(1370)]
    public void CopyInverseFrom(BitSet other)
    {
        if (other.Capacity != Capacity)
            throw new ArgumentException("Cannot copy from bitset with different length");

        for (uint i = 0; i < Bits.Length; ++i)
            Bits[i] = ~other.Bits[i];
    }

    public Enumerator GetEnumerator()
    {
        return new(this);
    }

    public ref struct Enumerator(BitSpan span) : IEnumerator<int>
    {
        MemorySpan<ulong>.Enumerator words = span.Bits.GetEnumerator();
        BitEnumerator bits = new(0ul);
        int word = -ULongBits;

        public readonly int Current => word + bits.Current;

        readonly object IEnumerator.Current => Current;

        public bool MoveNext()
        {
            while (true)
            {
                if (bits.MoveNext())
                    return true;

                if (!words.MoveNext())
                    return false;

                bits = new(words.Current);
                word += ULongBits;
            }
        }

        public readonly void Dispose()
        {
            words.Dispose();
            bits.Dispose();
        }

        public void Reset()
        {
            throw new NotImplementedException();
        }
    }

    private ref struct BitEnumerator(ulong bits) : IEnumerator<int>
    {
        ulong word = bits;
        int offset = -1;

        public readonly int Current => offset;

        readonly object IEnumerator.Current => Current;

        public bool MoveNext()
        {
            offset += 1;
            if (word == 0)
                return false;

            var tzcnt = MathUtil.TrailingZeroCount(word);
            offset += tzcnt;
            word >>= tzcnt + 1;
            return true;
        }

        public readonly void Dispose() { }

        public void Reset()
        {
            throw new NotImplementedException();
        }
    }

    private class DebugView(BitSpan span)
    {
        readonly int[] bits = [.. span];

        [DebuggerBrowsable(DebuggerBrowsableState.RootHidden)]
        public int[] Items => bits;
    }
}
