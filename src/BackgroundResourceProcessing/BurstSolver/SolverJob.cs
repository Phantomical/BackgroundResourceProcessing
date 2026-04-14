using BackgroundResourceProcessing.Collections.Burst;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;

namespace BackgroundResourceProcessing.BurstSolver;

[BurstCompile]
internal unsafe struct SolverJob : IJob
{
    public ResourceGraph Graph;
    public NativeArray<double> InventoryRates;
    public NativeArray<double> ConverterRates;

    /// <summary>
    /// Single-element array holding an <see cref="Error"/> code as an int.
    /// Zero means success.
    /// </summary>
    public NativeArray<int> ErrorCode;

    public void Execute()
    {
        var invSpan = new MemorySpan<double>(
            (double*)InventoryRates.GetUnsafePtr(),
            InventoryRates.Length
        );
        var convSpan = new MemorySpan<double>(
            (double*)ConverterRates.GetUnsafePtr(),
            ConverterRates.Length
        );

        var result = Solver.ComputeInventoryRates(
            ref Graph,
            invSpan,
            convSpan,
            AllocatorHandle.Temp
        );

        if (result.TryGetError(out var err))
            ErrorCode[0] = (int)err;
    }
}
