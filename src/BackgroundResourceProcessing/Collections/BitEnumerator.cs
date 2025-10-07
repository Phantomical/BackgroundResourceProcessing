using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using BackgroundResourceProcessing.Utils;

namespace BackgroundResourceProcessing.Collections;

internal struct BitEnumerator(int start, ulong bits) : IEnumerator<int>
{
    int index = start - 1;
    ulong bits = bits;

    public readonly int Current => index;

    readonly object IEnumerator.Current => Current;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool MoveNext()
    {
        if (bits == 0)
            return false;
        index += 1;
        int bit = MathUtil.TrailingZeroCount(bits);

        index += bit;
        if (bit == 63)
            bits = 0;
        else
            bits >>= bit + 1;
        return true;
    }

    public void Reset()
    {
        throw new NotImplementedException();
    }

    public readonly void Dispose() { }

    public readonly BitEnumerator GetEnumerator()
    {
        return this;
    }

    public override string ToString()
    {
        return $"index = {index}, bits = {bits:X}";
    }
}
