using System;
using System.Collections;
using System.Collections.Generic;
using BackgroundResourceProcessing.Utils;
using Unity.Burst.CompilerServices;
using Unity.Collections;
using static Unity.Burst.Intrinsics.X86.Bmi2;

namespace BackgroundResourceProcessing.Collections.Burst;

internal struct BitSet : IEnumerable<int>, IDisposable
{
    const int ULongBits = 64;

    RawArray<ulong> bits;

    public readonly int Capacity
    {
        [return: AssumeRange(0, int.MaxValue)]
        get => bits.Count * ULongBits;
    }
    public readonly Allocator Allocator => bits.Allocator;
    public readonly Span<ulong> Span => bits.Span;

    public readonly bool this[int key]
    {
        get
        {
            if (key < 0 || key >= Capacity)
                ThrowIndexOutOfRange();

            var word = key / ULongBits;
            var bit = key % ULongBits;

            return (bits[word] & (1ul << bit)) != 0;
        }
        set
        {
            if (key < 0 || key >= Capacity)
                ThrowIndexOutOfRange();

            var word = key / ULongBits;
            var bit = key % ULongBits;
            var mask = 1ul << bit;

            if (value)
                bits[word] |= mask;
            else
                bits[word] &= ~mask;
        }
    }

    public BitSet(Allocator allocator) => bits = new(allocator);

    public BitSet(int capacity, Allocator allocator) =>
        bits = new((capacity + ULongBits - 1) / ULongBits, allocator);

    public BitSet(Span<ulong> span, Allocator allocator) => bits = new(span, allocator);

    public void Dispose() => bits.Dispose();

    public readonly BitSet Clone() => new(Span, Allocator);

    public readonly bool Contains(int key)
    {
        if (key < 0 || key >= Capacity)
            return false;
        return this[key];
    }

    public void Add(int key) => this[key] = true;

    public void Remove(int key) => this[key] = false;

    public void Clear() => bits.Fill(0);

    public readonly int GetCount()
    {
        int count = 0;
        foreach (var word in bits)
            count += MathUtil.PopCount(word);
        return count;
    }

    public void Fill(bool value = true) => bits.Fill(value ? ulong.MaxValue : 0);

    [IgnoreWarning(1310)]
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

    [IgnoreWarning(1310)]
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

    [IgnoreWarning(1310)]
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

    [IgnoreWarning(1310)]
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

    [IgnoreWarning(1310)]
    public void CopyFrom(BitSet other)
    {
        if (other.bits.Length != bits.Length)
            throw new ArgumentException("bitset capacities did not match");

        for (int i = 0; i < bits.Length; ++i)
            bits[i] = other.bits[i];
    }

    [IgnoreWarning(1310)]
    public void CopyInverseFrom(BitSet other)
    {
        if (other.bits.Length != bits.Length)
            throw new ArgumentException("bitset capacities did not match");

        for (int i = 0; i < bits.Length; ++i)
            bits[i] = ~other.bits[i];
    }

    [IgnoreWarning(1310)]
    public void RemoveAll(BitSet other)
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

    [IgnoreWarning(1310)]
    static void ThrowIndexOutOfRange() =>
        throw new IndexOutOfRangeException("bitset index was out of range");

    #region IEnumerator<T>
    public readonly Enumerator GetEnumerator() => new(this);

    readonly IEnumerator<int> IEnumerable<int>.GetEnumerator() => GetEnumerator();

    readonly IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public struct Enumerator(BitSet set) : IEnumerator<int>
    {
        RawArray<ulong>.Enumerator words = set.bits.GetEnumerator();
        BitEnumerator bits = default;
        int wordIndex = -1;

        public readonly int Current => bits.Current;
        readonly object IEnumerator.Current => Current;

        public bool MoveNext()
        {
            if (bits.MoveNext())
                return true;

            while (true)
            {
                if (!words.MoveNext())
                    return false;
                wordIndex++;
                ulong word = words.Current;
                if (word != 0)
                {
                    int index = wordIndex * ULongBits;
                    bits = new BitEnumerator(index, word);
                    return bits.MoveNext();
                }
            }
        }

        void IEnumerator.Reset() => throw new NotSupportedException();

        public void Dispose() { }
    }

    #endregion
}
