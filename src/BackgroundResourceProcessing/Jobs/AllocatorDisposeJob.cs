using BackgroundResourceProcessing.Collections;
using BackgroundResourceProcessing.Collections.Burst;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

namespace BackgroundResourceProcessing.Jobs;

/// <summary>
/// Disposes a <see cref="BurstAllocator"/> once the jobs it depends on have
/// completed.
/// </summary>
[BurstCompile]
internal struct AllocatorDisposeJob(NativeReference<BurstAllocator> allocator) : IJob
{
    [DeallocateOnJobCompletion]
    public NativeReference<BurstAllocator> allocator = allocator;

    public void Execute() => allocator.Value.Dispose();
}
