using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;

#pragma warning disable CS0660 // Type defines operator == or operator != but does not override Object.Equals(object o)
#pragma warning disable CS0661 // Type defines operator == or operator != but does not override Object.GetHashCode()

namespace BackgroundResourceProcessing.Collections;

[DebuggerTypeProxy(typeof(BitSliceDebugView))]
[DebuggerDisplay("Capacity = {Capacity}")]
internal readonly ref struct BitSliceX(Span<ulong> bits)
{
    const int ULongBits = 64;

    readonly Span<ulong> bits = bits;

    public readonly int Capacity => bits.Length * ULongBits;
    public readonly Span<ulong> Bits => bits;

    public bool this[int column]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            if (column < 0 || column >= Capacity)
                ThrowOutOfRangeException(column);

            var word = bits[column / ULongBits];
            int bit = column % ULongBits;

            return (word & (1ul << bit)) != 0;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set
        {
            if (column < 0 || column >= Capacity)
                ThrowOutOfRangeException(column);

            var word = column / ULongBits;
            var bit = column % ULongBits;
            var mask = 1ul << bit;

            if (value)
                bits[word] |= mask;
            else
                bits[word] &= ~mask;
        }
    }

    private void ThrowOutOfRangeException(int column)
    {
        throw new IndexOutOfRangeException(
            $"Column index was out of bounds for this BitSlice ({column} >= {Capacity})"
        );
    }

    public void AndWith(BitSliceX other)
    {
        if (other.Capacity > Capacity)
            throw new ArgumentException("BitSlice instances have different lengths");

        for (int i = 0; i < other.bits.Length; ++i)
            bits[i] &= other.bits[i];
    }

    public void OrWith(BitSliceX other)
    {
        if (other.Capacity > Capacity)
            throw new ArgumentException("BitSlice instances have different lengths");

        for (int i = 0; i < other.bits.Length; ++i)
            bits[i] |= other.bits[i];
    }

    public void Zero()
    {
        bits.Fill(0);
    }

    public readonly Enumerator GetEnumerator()
    {
        return new(this);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(BitSliceX a, BitSliceX b)
    {
        return MemoryExtensions.SequenceEqual(a.bits, b.bits);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(BitSliceX a, BitSliceX b)
    {
        return !(a == b);
    }

    public ref struct Enumerator(BitSliceX slice) : IEnumerator<int>
    {
        int index = -1;
        readonly BitSliceX slice = slice;
        BitEnumerator inner = default;

        public readonly int Current => inner.Current;

        readonly object IEnumerator.Current => Current;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool MoveNext()
        {
            while (true)
            {
                if (inner.MoveNext())
                    return true;

                index += 1;

                if (index >= slice.bits.Length)
                    return false;
                inner = new(index * ULongBits, slice.bits[index]);
            }
        }

        public void Reset()
        {
            index = -1;
            inner = default;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly void Dispose() { }
    }
}

internal class BitSliceDebugView
{
    readonly int[] elements;

    public BitSliceDebugView(BitSliceX slice)
    {
        elements = [.. slice];
    }

    [DebuggerBrowsable(DebuggerBrowsableState.RootHidden)]
    public int[] Items => elements;

    public override string ToString()
    {
        return $"Count = {elements.Length}";
    }
}
