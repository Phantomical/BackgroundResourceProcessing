using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using BackgroundResourceProcessing.BurstSolver;
using BackgroundResourceProcessing.Utils;
using Unity.Burst;
using Unity.Burst.CompilerServices;
using CSUnsafe = System.Runtime.CompilerServices.Unsafe;

namespace BackgroundResourceProcessing.Collections.Burst;

#pragma warning disable CS8500 // This takes the address of, gets the size of, or declares a pointer to a managed type

public static unsafe class TestAllocator
{
    struct Allocation
    {
        public int size;
        public Type elem;
    }

    public class AllocationException(string message) : Exception(message) { }

    public struct TestGuard : IDisposable
    {
        public TestGuard()
        {
            Allocations.Value.Clear();
        }

        public void Dispose()
        {
            var allocs = Allocations.Value;
            if (allocs.Count == 0)
                return;

            int count = allocs.Count;
            int total = allocs.Values.Select(alloc => alloc.size).Sum();
            allocs.Clear();

            throw new AllocationException(
                $"test leaked {total} bytes of memory across {count} allocations"
            );
        }
    }

    static readonly ThreadLocal<Dictionary<IntPtr, Allocation>> Allocations = new(() => []);

    public static T* Alloc<T>(int count)
        where T : struct
    {
        if (BurstUtil.IsBurstCompiled)
            ThrowBurstException();
        if (UnsafeUtil.ContainsReferences<T>())
            ThrowTypeHasGcReferenceException<T>();
        AllocWrap(out T* ptr, count);
        return ptr;
    }

    [BurstDiscard]
    static void AllocWrap<T>(out T* ptr, int count)
        where T : struct => ptr = AllocImpl<T>(count);

    static T* AllocImpl<T>(int count)
        where T : struct
    {
        if (count < 0)
            throw new ArgumentOutOfRangeException(nameof(count));

        IntPtr ptr = Marshal.AllocHGlobal(count * CSUnsafe.SizeOf<T>());
        Allocations.Value.Add(ptr, new() { size = count * CSUnsafe.SizeOf<T>(), elem = typeof(T) });
        return (T*)ptr;
    }

    public static T* Realloc<T>(T* ptr, int newcount)
        where T : struct
    {
        if (BurstUtil.IsBurstCompiled)
            ThrowBurstException();
        if (UnsafeUtil.ContainsReferences<T>())
            ThrowTypeHasGcReferenceException<T>();
        ReallocWrap(out var res, ptr, newcount);
        return res;
    }

    [BurstDiscard]
    static void ReallocWrap<T>(out T* res, T* ptr, int newcount)
        where T : struct
    {
        res = ReallocImpl(ptr, newcount);
    }

    static T* ReallocImpl<T>(T* ptr, int newcount)
        where T : struct
    {
        if (newcount < 0)
            throw new ArgumentOutOfRangeException(nameof(newcount));
        if (ptr is null)
            return AllocImpl<T>(newcount);

        var allocs = Allocations.Value;
        if (!allocs.TryGetValue((IntPtr)ptr, out var alloc))
            throw new AllocationException(
                $"attempted to realloc pointer not previously allocated by the test allocator"
            );

        if (alloc.elem != typeof(T))
            throw new AllocationException(
                $"allocation with element type {alloc.elem.Name} reallocated as different type {typeof(T).Name}"
            );

        IntPtr newptr = Marshal.ReAllocHGlobal(
            (IntPtr)ptr,
            (IntPtr)(CSUnsafe.SizeOf<T>() * newcount)
        );

        allocs.Remove((IntPtr)ptr);
        allocs.Add(newptr, new() { elem = alloc.elem, size = CSUnsafe.SizeOf<T>() * newcount });

        return (T*)newptr;
    }

    public static void Free<T>(T* ptr)
        where T : struct
    {
        if (BurstUtil.IsBurstCompiled)
            ThrowBurstException();
        if (UnsafeUtil.ContainsReferences<T>())
            ThrowTypeHasGcReferenceException<T>();

        FreeImpl(ptr);
    }

    [BurstDiscard]
    static void FreeImpl<T>(T* ptr)
        where T : struct
    {
        if (ptr is null)
            return;

        var allocs = Allocations.Value;
        if (!allocs.TryGetValue((IntPtr)ptr, out var alloc))
            throw new AllocationException(
                $"attempted to free pointer not previously allocated by the test allocator"
            );

        if (alloc.elem != typeof(T))
            throw new AllocationException(
                $"allocation with element type {alloc.elem.Name} freed as different type {typeof(T).Name}"
            );

        allocs.Remove((IntPtr)ptr);

        Marshal.FreeHGlobal((IntPtr)ptr);
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
