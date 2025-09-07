using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using BackgroundResourceProcessing.Utils;
using Unity.Burst.CompilerServices;
using static Unity.Burst.Intrinsics.X86.Bmi2;

namespace BackgroundResourceProcessing.Collections;

[DebuggerDisplay("Capacity = {Capacity}")]
[DebuggerTypeProxy(typeof(DebugView))]
internal ref struct BitSpan(Span<ulong> bits)
{
    const int ULongBits = 64;

    Span<ulong> bits = bits;

    public readonly int Capacity
    {
        [return: AssumeRange(0, int.MaxValue)]
        get => bits.Length * ULongBits;
    }
    public readonly int Words
    {
        [return: AssumeRange(0, int.MaxValue)]
        get => bits.Length;
    }

    public readonly Span<ulong> Span => bits;

    public readonly bool this[int key]
    {
        get
        {
            if (key < 0 || key >= Capacity)
                ThrowIndexOutOfRangeException();

            var word = key / ULongBits;
            var bit = key % ULongBits;

            return (bits[word] & (1ul << bit)) != 0;
        }
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
        : this(new Span<ulong>(bits, length)) { }

    public BitSpan(BitSet set)
        : this(set.Bits) { }

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

    public void Clear() => Fill(false);

    public void Fill(bool value) => bits.Fill(value ? ulong.MaxValue : 0);

    public void AndWith(BitSpan other)
    {
        if (bits.Length != other.bits.Length)
            ThrowMismatchedSetCapacity();

        for (int i = 0; i < bits.Length; ++i)
            bits[i] &= other.bits[i];
    }

    public void OrWith(BitSpan other)
    {
        if (bits.Length != other.bits.Length)
            ThrowMismatchedSetCapacity();

        for (int i = 0; i < bits.Length; ++i)
            bits[i] |= other.bits[i];
    }

    public void XorWith(BitSpan other)
    {
        if (bits.Length != other.bits.Length)
            ThrowMismatchedSetCapacity();

        for (int i = 0; i < bits.Length; ++i)
            bits[i] ^= other.bits[i];
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
    public static bool operator ==(BitSpan a, BitSpan b)
    {
        return a.bits == b.bits;
    }

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

    public ref struct Enumerator(BitSpan set) : IEnumerator<int>
    {
        Span<ulong>.Enumerator words = set.bits.GetEnumerator();
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

    private sealed class DebugView(BitSpan span)
    {
        [DebuggerBrowsable(DebuggerBrowsableState.RootHidden)]
        public int[] Items { get; } = [.. span];
    }
}
