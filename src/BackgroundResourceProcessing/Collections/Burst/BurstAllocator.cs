using System;
using System.Runtime.CompilerServices;
using BackgroundResourceProcessing.BurstSolver;
using BackgroundResourceProcessing.Utils;
using Unity.Burst.CompilerServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

#pragma warning disable CS8500 // This takes the address of, gets the size of, or declares a pointer to a managed type

namespace BackgroundResourceProcessing.Collections.Burst;

internal unsafe struct BurstAllocator() : IDisposable
{
    const uint SlabSize = 512 * 1024;

    struct Slab
    {
        public byte* data;
        public uint tail;
        public uint length;
    }

    AllocatorHandle allocator = AllocatorHandle.TempJob;
    RawList<Slab> slabs = new(8, AllocatorHandle.Temp);

    [IgnoreWarning(1370)]
    public T* Allocate<T>(int count)
        where T : struct
    {
        if (count < 0)
            throw new ArgumentOutOfRangeException(
                nameof(count),
                "cannot create a negative-size allocation"
            );

        int bytes = sizeof(T) * count;
        if (!slabs.IsEmpty)
        {
            uint aligned = ((uint)bytes + 15) & ~15u;
            ref var slab = ref slabs[slabs.Count - 1];

            if (slab.length - slab.tail >= aligned)
            {
                var data = &slab.data[slab.tail];
                slab.tail += aligned;
                return (T*)data;
            }
        }

        return (T*)AllocateSlow(bytes);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private byte* AllocateSlow(int bytes)
    {
        if (bytes > SlabSize)
        {
            var data = allocator.Allocate<byte>(bytes);
            var slab = new Slab
            {
                data = data,
                length = (uint)bytes,
                tail = (uint)bytes,
            };

            bool empty = !slabs.TryPop(out var last);
            slabs.Add(slab);
            if (!empty)
                slabs.Add(last);

            return data;
        }

        uint aligned = ((uint)bytes + 15) & ~15u;
        if (!slabs.IsEmpty)
        {
            ref var slab = ref slabs[slabs.Count - 1];

            if (slab.length - slab.tail >= aligned)
            {
                var data = &slab.data[slab.tail];
                slab.tail += aligned;
                return data;
            }
        }

        {
            var data = allocator.Allocate<byte>((int)SlabSize);
            var slab = new Slab
            {
                data = data,
                length = SlabSize,
                tail = 0,
            };

            slab.tail += aligned;
            slabs.Add(slab);

            return data;
        }
    }

    public void Reset()
    {
        foreach (var slab in slabs)
            allocator.Free(slab.data);

        slabs.Clear();
    }

    public void Dispose() => Reset();
}

internal readonly unsafe struct AllocatorHandle
{
    readonly void* handle;

    public static AllocatorHandle Invalid => new(Allocator.Invalid);
    public static AllocatorHandle Temp => new(Allocator.Temp);
    public static AllocatorHandle TempJob => new(Allocator.Persistent);
    public static AllocatorHandle Persistent => new(Allocator.Persistent);

    public AllocatorHandle(BurstAllocator* allocator)
    {
        handle = allocator;
    }

    public AllocatorHandle(Allocator allocator)
    {
        if ((int)allocator < 0 || allocator > Allocator.Persistent)
            ThrowInvalidAllocatorId();

        handle = (void*)(UIntPtr)(int)allocator;
    }

    private bool Match(out Allocator allocator, out BurstAllocator* handle)
    {
        if ((ulong)(UIntPtr)this.handle <= (ulong)Allocator.Persistent)
        {
            allocator = (Allocator)(int)(UIntPtr)this.handle;
            handle = null;
            return true;
        }
        else
        {
            allocator = Allocator.Invalid;
            handle = (BurstAllocator*)this.handle;
            return false;
        }
    }

    [IgnoreWarning(1370)]
    public T* Allocate<T>(int count)
        where T : unmanaged
    {
        if (count < 0 || count > int.MaxValue / sizeof(T))
            throw new ArgumentOutOfRangeException(nameof(count));
        if (count == 0)
            return null;

        if (!Match(out var allocator, out var handle))
            return handle->Allocate<T>(count);
        if (BurstUtil.UseTestAllocator)
            return TestAllocator.Alloc<T>(count);
        if (allocator == Allocator.Invalid)
            throw new InvalidOperationException("cannot allocate using the invalid allocator");

        return AllocateUnity<T>(count, allocator);
    }

    [IgnoreWarning(1370)]
    public void Free<T>(T* ptr)
        where T : unmanaged
    {
        if (ptr is null)
            return;

        if (BurstUtil.UseTestAllocator)
            return;

        if (Match(out var allocator, out var _))
            FreeUnity(ptr, allocator);
    }

    private static T* AllocateUnity<T>(int count, Allocator allocator)
        where T : struct
    {
        return (T*)UnsafeUtility.Malloc(sizeof(T) * count, UnsafeUtility.AlignOf<T>(), allocator);
    }

    private static void FreeUnity<T>(T* ptr, Allocator allocator)
        where T : struct
    {
        if (allocator == Allocator.Temp)
            return;

        UnsafeUtility.Free(ptr, allocator);
    }

    public static bool operator ==(AllocatorHandle a, AllocatorHandle b) => a.handle == b.handle;

    public static bool operator !=(AllocatorHandle a, AllocatorHandle b) => !(a == b);

    public override bool Equals(object obj) => obj is AllocatorHandle handle && handle == this;

    public override int GetHashCode() => throw new NotSupportedException();

    [IgnoreWarning(1370)]
    private static void ThrowInvalidAllocatorId() =>
        throw new ArgumentOutOfRangeException(
            "cannot create allocator handles with custom allocators"
        );
}
