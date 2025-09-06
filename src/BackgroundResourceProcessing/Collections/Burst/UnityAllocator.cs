using System;
using Unity.Burst.CompilerServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace BackgroundResourceProcessing.Collections.Burst;

#pragma warning disable CS8500 // This takes the address of, gets the size of, or declares a pointer to a managed type

/// <summary>
/// You can't directly call ECall methods from wihtin a test, but wrapping them
/// is fine.
/// </summary>
internal unsafe static class UnityAllocator
{
    [IgnoreWarning(1310)]
    public static T* Alloc<T>(int count, Allocator allocator)
        where T : struct
    {
        if (count < 0)
            throw new ArgumentOutOfRangeException(nameof(count));

        return (T*)
            UnsafeUtility.Malloc(
                UnsafeUtility.SizeOf<T>() * count,
                UnsafeUtility.AlignOf<T>(),
                allocator
            );
    }

    [IgnoreWarning(1310)]
    public static void Free<T>(T* ptr, Allocator allocator)
        where T : struct
    {
        if (ptr is null)
            return;

        UnsafeUtility.Free(ptr, allocator);
    }

    [IgnoreWarning(1310)]
    public static void Copy<T>(T* dst, T* src, int count)
        where T : struct
    {
        if (count < 0)
            throw new ArgumentOutOfRangeException(nameof(count));

        UnsafeUtility.MemCpy(dst, src, UnsafeUtility.SizeOf<T>() * count);
    }

    [IgnoreWarning(1310)]
    public static void Clear<T>(T* dst, int count)
        where T : struct
    {
        if (count < 0)
            throw new ArgumentOutOfRangeException(nameof(count));

        UnsafeUtility.MemClear(dst, UnsafeUtility.SizeOf<T>() * count);
    }
}
