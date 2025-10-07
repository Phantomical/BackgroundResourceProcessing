using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using BackgroundResourceProcessing.BurstSolver;
using Unity.Burst;
using Unity.Burst.CompilerServices;
using CSUnsafe = System.Runtime.CompilerServices.Unsafe;

namespace BackgroundResourceProcessing.Collections.Burst;

internal static unsafe class TestAllocator
{
    static readonly ThreadLocal<List<IntPtr>> Allocations = new(() => []);

    public class AllocationException(string message) : Exception(message) { }

    public struct TestGuard() : IDisposable
    {
        public readonly void Dispose()
        {
            Cleanup();
        }
    }

    public static void Cleanup()
    {
        var allocs = Allocations.Value;
        foreach (var alloc in allocs)
            Marshal.FreeHGlobal(alloc);

        allocs.Clear();
    }

    public static T* Alloc<T>(int count)
        where T : unmanaged
    {
        if (BurstUtil.IsBurstCompiled)
            ThrowBurstException();
        AllocWrap(out T* ptr, count);
        return ptr;
    }

    [BurstDiscard]
    static void AllocWrap<T>(out T* ptr, int count)
        where T : unmanaged => ptr = AllocImpl<T>(count);

    static T* AllocImpl<T>(int count)
        where T : unmanaged
    {
        if (count < 0)
            throw new ArgumentOutOfRangeException(nameof(count));

        IntPtr ptr = Marshal.AllocHGlobal(count * CSUnsafe.SizeOf<T>());
        Allocations.Value.Add(ptr);
        return (T*)ptr;
    }

    [IgnoreWarning(1370)]
    static void ThrowBurstException() =>
        throw new AllocationException("cannot use test allocator when burst-compiled");

    [IgnoreWarning(1370)]
    static void ThrowTypeHasGcReferenceException<T>() =>
        throw new InvalidOperationException(
            $"cannot allocate type {typeof(T).Name} on the unmanaged heap as it contains GC references"
        );
}
