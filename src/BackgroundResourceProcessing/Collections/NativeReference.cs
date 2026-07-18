using System;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace BackgroundResourceProcessing.Collections;

[NativeContainer]
[NativeContainerSupportsDeallocateOnJobCompletion]
internal unsafe struct NativeReference<T> : IDisposable
    where T : unmanaged
{
    [NativeDisableUnsafePtrRestriction]
    T* m_Buffer;
    Allocator m_AllocatorLabel;

    public readonly ref T Value => ref *m_Buffer;

    public NativeReference(T value, Allocator allocator)
    {
        m_Buffer = (T*)
            UnsafeUtility.Malloc(UnsafeUtility.SizeOf<T>(), UnsafeUtility.AlignOf<T>(), allocator);
        m_AllocatorLabel = allocator;

        Value = value;
    }

    public readonly T* GetUnsafePtr() => m_Buffer;

    public void Dispose()
    {
        UnsafeUtility.Free(m_Buffer, m_AllocatorLabel);
        this = default;
    }
}
