using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using BackgroundResourceProcessing.Collections.Burst;

namespace BackgroundResourceProcessing.Utils;

/// <summary>
/// A hash-code combiner, based off a similar type within .NET Core.
/// </summary>
[method: MethodImpl(MethodImplOptions.AggressiveInlining)]
internal ref struct HashCode(uint seed)
{
    private static readonly uint GlobalSeed = GenerateSeed();

    private const uint Prime1 = 2654435761U;
    private const uint Prime2 = 2246822519U;
    private const uint Prime3 = 3266489917U;
    private const uint Prime4 = 668265263U;
    private const uint Prime5 = 374761393U;

    uint v1 = seed + Prime1 + Prime2;
    uint v2 = seed + Prime2;
    uint v3 = seed + 0;
    uint v4 = seed - Prime1;

    uint pos = 0;

    public HashCode()
        : this(GlobalSeed) { }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Add<T>(T item)
    {
        uint hc = (uint)(item?.GetHashCode() ?? 0);

        switch (pos)
        {
            case 0:
                Round(ref v1, hc);
                break;
            case 1:
                Round(ref v2, hc);
                break;
            case 2:
                Round(ref v3, hc);
                break;
            case 3:
                Round(ref v4, hc);
                break;
        }

        pos = (pos + 1) % 4;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Add<T1, T2>(T1 item1, T2 item2)
    {
        uint hc1 = (uint)(item1?.GetHashCode() ?? 0);
        uint hc2 = (uint)(item2?.GetHashCode() ?? 0);

        switch (pos)
        {
            case 0:
                Round(ref v1, hc1);
                Round(ref v2, hc2);
                break;
            case 1:
                Round(ref v2, hc1);
                Round(ref v3, hc2);
                break;
            case 2:
                Round(ref v3, hc1);
                Round(ref v4, hc2);
                break;
            case 3:
                Round(ref v4, hc1);
                Round(ref v1, hc2);
                break;
        }

        pos = (pos + 2) % 4;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Add<T1, T2, T3>(T1 item1, T2 item2, T3 item3)
    {
        uint hc1 = (uint)(item1?.GetHashCode() ?? 0);
        uint hc2 = (uint)(item2?.GetHashCode() ?? 0);
        uint hc3 = (uint)(item3?.GetHashCode() ?? 0);

        switch (pos)
        {
            case 0:
                Round(ref v1, hc1);
                Round(ref v2, hc2);
                Round(ref v3, hc3);
                break;
            case 1:
                Round(ref v2, hc1);
                Round(ref v3, hc2);
                Round(ref v4, hc3);
                break;
            case 2:
                Round(ref v3, hc1);
                Round(ref v4, hc2);
                Round(ref v1, hc3);
                break;
            case 3:
                Round(ref v4, hc1);
                Round(ref v1, hc2);
                Round(ref v2, hc3);
                break;
        }

        pos = (pos + 3) % 4;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Add<T1, T2, T3, T4>(T1 item1, T2 item2, T3 item3, T4 item4)
    {
        uint hc1 = (uint)(item1?.GetHashCode() ?? 0);
        uint hc2 = (uint)(item2?.GetHashCode() ?? 0);
        uint hc3 = (uint)(item3?.GetHashCode() ?? 0);
        uint hc4 = (uint)(item4?.GetHashCode() ?? 0);

        switch (pos)
        {
            case 0:
                Round(ref v1, hc1);
                Round(ref v2, hc2);
                Round(ref v3, hc3);
                Round(ref v4, hc4);
                break;
            case 1:
                Round(ref v2, hc1);
                Round(ref v3, hc2);
                Round(ref v4, hc3);
                Round(ref v1, hc4);
                break;
            case 2:
                Round(ref v3, hc1);
                Round(ref v4, hc2);
                Round(ref v1, hc3);
                Round(ref v2, hc4);
                break;
            case 3:
                Round(ref v4, hc1);
                Round(ref v1, hc2);
                Round(ref v2, hc3);
                Round(ref v4, hc4);
                break;
        }

        // pos = (pos + 4) % 4;
    }

    public void AddAll<T>(T[] items)
    {
        AddAll((Span<T>)items);
    }

    public void AddAll<T>(List<T> items)
    {
        var length = items.Count;
        int index = 0;

        if (length < 4)
        {
            switch (length)
            {
                case 3:
                    Add(items[index++]);
                    goto case 2;
                case 2:
                    Add(items[index++]);
                    goto case 1;
                case 1:
                    Add(items[index++]);
                    goto case 0;
                case 0:
                    Add(length);
                    break;
            }

            return;
        }

        switch (pos)
        {
            case 3:
                Add(items[index++]);
                goto case 2;
            case 2:
                Add(items[index++]);
                goto case 1;
            case 1:
                Add(items[index++]);
                break;
        }

        pos = 0;
        for (; index + 3 < length; index += 4)
            AddAligned(items[index], items[index + 1], items[index + 2], items[index + 3]);

        switch (length - index)
        {
            case 3:
                Add(items[index++]);
                goto case 2;
            case 2:
                Add(items[index++]);
                goto case 1;
            case 1:
                Add(items[index++]);
                goto case 0;
            case 0:
                Add(length);
                break;
        }
    }

    public void AddAll<I, T>(I items)
        where I : IEnumerable<T>
    {
        foreach (var item in items)
            Add(item);
    }

    public void AddAll<T>(Span<T> items)
    {
        var length = items.Length;
        int index = 0;

        if (length < 4)
        {
            switch (length)
            {
                case 3:
                    Add(items[index++]);
                    goto case 2;
                case 2:
                    Add(items[index++]);
                    goto case 1;
                case 1:
                    Add(items[index++]);
                    goto case 0;
                case 0:
                    Add(length);
                    break;
            }

            return;
        }

        switch (pos)
        {
            case 3:
                Add(items[index++]);
                goto case 2;
            case 2:
                Add(items[index++]);
                goto case 1;
            case 1:
                Add(items[index++]);
                break;
        }

        pos = 0;
        for (; index + 3 < length; index += 4)
            AddAligned(items[index], items[index + 1], items[index + 2], items[index + 3]);

        switch (length - index)
        {
            case 3:
                Add(items[index++]);
                goto case 2;
            case 2:
                Add(items[index++]);
                goto case 1;
            case 1:
                Add(items[index++]);
                goto case 0;
            case 0:
                Add(length);
                break;
        }
    }

    public void AddAll<T>(MemorySpan<T> items)
        where T : unmanaged => AddAll(items.AsSystemSpan());

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override readonly int GetHashCode()
    {
        uint h32 =
            ((v1 << 1) | (v1 >> (32 - 1)))
            + ((v2 << 7) | (v2 >> (32 - 7)))
            + ((v3 << 12) | (v3 >> (32 - 12)))
            + ((v4 << 18) | (v4 >> (32 - 18)));

        // xxHash32 finalizer
        h32 ^= h32 >> 15;
        h32 *= Prime2;
        h32 ^= h32 >> 13;
        h32 *= Prime3;
        h32 ^= h32 >> 16;

        return (int)h32;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void AddAligned<T1, T2, T3, T4>(T1 item1, T2 item2, T3 item3, T4 item4)
    {
        uint hc1 = (uint)(item1?.GetHashCode() ?? 0);
        uint hc2 = (uint)(item2?.GetHashCode() ?? 0);
        uint hc3 = (uint)(item3?.GetHashCode() ?? 0);
        uint hc4 = (uint)(item4?.GetHashCode() ?? 0);

        Round(ref v1, hc1);
        Round(ref v2, hc2);
        Round(ref v3, hc3);
        Round(ref v4, hc4);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Round(ref uint v, uint data)
    {
        v += data * Prime2;
        v = (v << 13) | (v >> (32 - 13));
        v *= Prime1;
    }

    private static uint GenerateSeed()
    {
        Random rnd = new();
        return (uint)rnd.Next();
    }
}
