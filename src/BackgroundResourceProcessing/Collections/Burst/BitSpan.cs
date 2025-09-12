using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using BackgroundResourceProcessing.Utils;
using Unity.Burst.CompilerServices;
using static Unity.Burst.Intrinsics.X86.Bmi2;

namespace BackgroundResourceProcessing.Collections.Burst;

[DebuggerDisplay("Count = {Count}/{Capacity}")]
[DebuggerTypeProxy(typeof(DebugView))]
internal struct BitSpan(MemorySpan<ulong> bits) : IEnumerable<int>
{
    const int ULongBits = 64;

    MemorySpan<ulong> bits = bits;

    public readonly int Capacity
    {
        [return: AssumeRange(0, int.MaxValue)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => bits.Length * ULongBits;
    }
    public readonly int Words
    {
        [return: AssumeRange(0, int.MaxValue)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => bits.Length;
    }
    public readonly int Count
    {
        get
        {
            int count = 0;
            foreach (ulong word in bits)
                count += MathUtil.PopCount(word);
            return count;
        }
    }

    public readonly MemorySpan<ulong> Span => bits;

    public readonly bool this[int key]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            if (key < 0 || key >= Capacity)
                ThrowIndexOutOfRangeException();

            var word = key / ULongBits;
            var bit = key % ULongBits;

            return (bits[word] & (1ul << bit)) != 0;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set
        {
            if (key < 0 || key >= Capacity)
                ThrowIndexOutOfRangeException();

            var word = key / ULongBits;
            var bit = key % ULongBits;
            var mask = 1ul << bit;

            if (value)
                bits[word] |= mask;
            else
                bits[word] &= ~mask;
        }
    }

    public readonly bool this[uint key]
    {
        get => this[(int)key];
        set => this[(int)key] = value;
    }

    public unsafe BitSpan(ulong* bits, int length)
        : this(new MemorySpan<ulong>(bits, length)) { }

    public BitSpan(BitSet set)
        : this(set.Span.bits) { }

    public static implicit operator BitSpan(BitSet set) => new(set);

    [IgnoreWarning(1370)]
    static void ThrowIndexOutOfRangeException() =>
        throw new IndexOutOfRangeException("index out of range for bitset");

    public readonly bool Contains(int key)
    {
        if (key < 0 || key >= Capacity)
            return false;
        return this[key];
    }

    public readonly int GetCount()
    {
        int count = 0;
        foreach (ulong word in bits)
            count += MathUtil.PopCount(word);
        return count;
    }

    public bool Add(int key) => this[key] = true;

    public bool Remove(int key) => this[key] = false;

    public readonly void Clear() => bits.Clear();

    public readonly void Fill(bool value) => bits.Fill(value ? ulong.MaxValue : 0);

    public void AndWith(BitSpan other)
    {
        if (bits.Length < other.bits.Length)
            ThrowMismatchedSetCapacity();

        for (int i = 0; i < other.bits.Length; ++i)
            bits[i] &= other.bits[i];
    }

    public unsafe void AndWith(BitSliceX other)
    {
        fixed (ulong* words = other.Bits)
            AndWith(new BitSpan(words, other.Bits.Length));
    }

    public void OrWith(BitSpan other)
    {
        if (bits.Length < other.bits.Length)
            ThrowMismatchedSetCapacity();

        for (int i = 0; i < other.bits.Length; ++i)
            bits[i] |= other.bits[i];
    }

    public unsafe void OrWith(BitSliceX other)
    {
        fixed (ulong* words = other.Bits)
            OrWith(new BitSpan(words, other.Bits.Length));
    }

    public void XorWith(BitSpan other)
    {
        if (bits.Length < other.bits.Length)
            ThrowMismatchedSetCapacity();

        for (int i = 0; i < other.bits.Length; ++i)
            bits[i] ^= other.bits[i];
    }

    public unsafe void XorWith(BitSliceX other)
    {
        fixed (ulong* words = other.Bits)
            XorWith(new BitSpan(words, other.Bits.Length));
    }

    public unsafe void Assign(BitSliceX other)
    {
        fixed (ulong* words = other.Bits)
            Assign(new BitSpan(words, other.Bits.Length));
    }

    public unsafe void Assign(ulong[] other)
    {
        var length = Math.Min(bits.Length, other.Length);

        for (int i = 0; i < length; ++i)
            bits[i] = other[i];
    }

    public void Assign(BitSpan other)
    {
        var length = Math.Min(bits.Length, other.bits.Length);

        for (int i = 0; i < length; ++i)
            bits[i] = other.bits[i];
    }

    [IgnoreWarning(1370)]
    static void ThrowMismatchedSetCapacity() =>
        throw new ArgumentException("bitspan instances have different capacities");

    [IgnoreWarning(1370)]
    public void ClearUpFrom(int index)
    {
        if (index < 0)
            throw new ArgumentOutOfRangeException(nameof(index));

        var word = index / ULongBits;
        var bit = index % ULongBits;
        var mask = MaskLo(bit);

        if (word >= bits.Length)
            return;

        bits[word] &= mask;

        for (int i = word + 1; i < bits.Length; ++i)
            bits[i] = 0;
    }

    [IgnoreWarning(1370)]
    public void ClearUpTo(int index)
    {
        if (index < 0)
            throw new ArgumentOutOfRangeException(nameof(index));

        var word = index / ULongBits;
        var bit = index % ULongBits;
        var mask = ~MaskLo(bit);

        if (word >= bits.Length)
            Clear();
        else
        {
            for (int i = 0; i < word; ++i)
                bits[i] = 0;

            bits[word] &= mask;
        }
    }

    [IgnoreWarning(1370)]
    public void ClearOutsideRange(int start, int end)
    {
        if (start < 0)
            throw new ArgumentOutOfRangeException(nameof(start));
        if (end < start)
            throw new ArgumentOutOfRangeException(nameof(end));
        if (end > Capacity)
            throw new ArgumentOutOfRangeException(nameof(end));

        int sword = start / ULongBits;
        int eword = end / ULongBits;
        int sbit = start % ULongBits;
        int ebit = end % ULongBits;

        for (int i = 0; i < sword; ++i)
            bits[i] = 0;

        if (sword >= bits.Length)
            return;

        ulong smask = ulong.MaxValue << sbit;
        ulong emask = MaskLo(ebit);

        bits[sword] &= smask;

        if (eword >= bits.Length)
            return;

        bits[eword] &= emask;

        for (int i = eword + 1; i < bits.Length; ++i)
            bits[i] = 0;
    }

    [IgnoreWarning(1370)]
    public void SetUpTo(int index)
    {
        if (index < 0 || index > Capacity)
            throw new ArgumentOutOfRangeException(nameof(index));

        var word = index / ULongBits;
        var bit = index % ULongBits;
        var mask = MaskLo(bit);

        for (int i = 0; i < word; ++i)
            bits[i] = ulong.MaxValue;

        if (word < bits.Length)
            bits[word] |= mask;
    }

    [IgnoreWarning(1370)]
    public void CopyFrom(BitSpan other)
    {
        if (other.bits.Length != bits.Length)
            throw new ArgumentException("bitset capacities did not match");

        for (int i = 0; i < bits.Length; ++i)
            bits[i] = other.bits[i];
    }

    [IgnoreWarning(1370)]
    public void CopyInverseFrom(BitSpan other)
    {
        if (other.bits.Length != bits.Length)
            throw new ArgumentException("bitset capacities did not match");

        for (int i = 0; i < bits.Length; ++i)
            bits[i] = ~other.bits[i];
    }

    [IgnoreWarning(1370)]
    public void RemoveAll(BitSpan other)
    {
        if (other.bits.Length != bits.Length)
            throw new ArgumentException("bitset capacities did not match");

        for (int i = 0; i < bits.Length; ++i)
            bits[i] &= ~other.bits[i];
    }

    static ulong MaskLo(int bit)
    {
        if (IsBmi2Supported)
            return bzhi_u64(ulong.MaxValue, (ulong)bit);
        else if (bit >= 64)
            return ulong.MaxValue;
        else
            return (1ul << bit) - 1;
    }

    #region operators
    public static bool operator ==(BitSpan a, BitSpan b) =>
        MemorySpanExtensions.SequenceEqual(a.bits, b.bits);

    public static bool operator !=(BitSpan a, BitSpan b)
    {
        return !(a == b);
    }
    #endregion

    public override readonly bool Equals(object obj) => false;

    // This suppresses the warning but will always throw an exception.
    public override readonly int GetHashCode() => bits.GetHashCode();

    #region IEnumerator<T>
    public readonly Enumerator GetEnumerator() => new(this);

    readonly IEnumerator<int> IEnumerable<int>.GetEnumerator() => GetEnumerator();

    readonly IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public struct Enumerator(BitSpan set) : IEnumerator<int>
    {
        readonly MemorySpan<ulong> words = set.bits;
        int index = -1;
        int bit = -1;
        ulong word = 0;

        public readonly int Current => index * ULongBits + bit;
        readonly object IEnumerator.Current => Current;

        public bool MoveNext()
        {
            while (true)
            {
                if (word != 0)
                {
                    bit = MathUtil.TrailingZeroCount(word);
                    word ^= 1ul << bit;
                    return true;
                }

                index += 1;
                if (index >= words.Length)
                    return false;

                word = words[index];
            }
        }

        void IEnumerator.Reset() => throw new NotSupportedException();

        public void Dispose() { }
    }
    #endregion

    internal sealed class DebugView(BitSpan span)
    {
        public DebugView(BitSet set)
            : this(set.Span) { }

        [DebuggerBrowsable(DebuggerBrowsableState.RootHidden)]
        public int[] Items { get; } = [.. span];
    }
}
