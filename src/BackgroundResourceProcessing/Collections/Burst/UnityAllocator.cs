using System;
using Unity.Burst;
using Unity.Burst.CompilerServices;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using static Unity.Burst.Intrinsics.X86;
using static Unity.Collections.LowLevel.Unsafe.UnsafeUtility;

namespace BackgroundResourceProcessing.Collections.Burst;

#pragma warning disable CS8500 // This takes the address of, gets the size of, or declares a pointer to a managed type

/// <summary>
/// You can't directly call ECall methods from wihtin a test, but wrapping them
/// is fine.
/// </summary>
[BurstCompile]
internal static unsafe class UnityAllocator
{
    private static int VectorAlign()
    {
        if (Avx.IsAvxSupported)
            return sizeof(v256);
        if (Sse.IsSseSupported)
            return sizeof(v128);
        return 1;
    }

    [IgnoreWarning(1370)]
    public static T* Alloc<T>(int count, Allocator allocator)
        where T : struct
    {
        if (count < 0 || count > int.MaxValue / sizeof(T))
            throw new ArgumentOutOfRangeException(nameof(count));

        return (T*)Malloc(sizeof(T) * count, Math.Max(AlignOf<T>(), VectorAlign()), allocator);
    }

    [IgnoreWarning(1370)]
    public static void Free<T>(T* ptr, Allocator allocator)
        where T : struct
    {
        if (ptr is null)
            return;

        // Deallocations with the temp allocator are a no-op.
        if (allocator == Allocator.Temp)
            return;

        UnsafeUtility.Free(ptr, allocator);
    }

    [IgnoreWarning(1370)]
    public static void Copy<T>([NoAlias] T* dst, [NoAlias] T* src, int count)
        where T : struct
    {
        if (count < 0)
            throw new ArgumentOutOfRangeException(nameof(count));

        MemCpy(dst, src, sizeof(T) * count);
    }

    [IgnoreWarning(1370)]
    public static void Clear<T>(T* dst, int count)
        where T : struct
    {
        if (count < 0)
            throw new ArgumentOutOfRangeException(nameof(count));

        MemClear(dst, sizeof(T) * count);
    }

    [IgnoreWarning(1370)]
    public static void Set<T>(T* dst, byte b, int count)
        where T : struct
    {
        if (count < 0)
            throw new ArgumentOutOfRangeException(nameof(count));

        MemSet(dst, b, sizeof(T) * count);
    }

    [IgnoreWarning(1370)]
    public static int Cmp<T>(T* a, T* b, int count)
        where T : struct
    {
        if (count < 0)
            throw new ArgumentOutOfRangeException(nameof(count));

        return MemCmp(a, b, sizeof(T) * count);
    }
}
