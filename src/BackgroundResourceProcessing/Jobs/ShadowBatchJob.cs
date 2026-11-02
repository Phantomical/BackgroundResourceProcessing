using BackgroundResourceProcessing.Collections.Burst;
using BackgroundResourceProcessing.Mathematics;
using BackgroundResourceProcessing.Shadow;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;

namespace BackgroundResourceProcessing.Jobs;

/// <summary>
/// The per-vessel inputs for a <see cref="ShadowBatchJob"/>.
/// </summary>
internal struct ShadowRequest
{
    public Mathematics.Orbit Orbit;
    public Vector3d Normal;
    public double CosLatitude;
    public int PlanetIndex;
    public bool IsLanded;
}

/// <summary>
/// A single (vessel, star) pair for a <see cref="ShadowBatchJob"/> to compute
/// a terminator for.
/// </summary>
internal struct ShadowTask
{
    /// <summary>
    /// The index of the <see cref="ShadowRequest"/> this task belongs to.
    /// </summary>
    public int Request;
    public int StarIndex;

    /// <summary>
    /// The reference body index for the star. Only used for landed vessels.
    /// </summary>
    public int ReferenceIndex;
}

/// <summary>
/// Computes one shadow terminator per <see cref="ShadowTask"/>. Scheduled by
/// <see cref="Shadow.ShadowBatch"/>.
/// </summary>
[BurstCompile]
internal unsafe struct ShadowBatchJob : IJobParallelFor
{
    [ReadOnly]
    public NativeArray<SystemBody> Bodies;

    [ReadOnly]
    public NativeArray<ShadowRequest> Requests;

    [ReadOnly]
    public NativeArray<ShadowTask> Tasks;

    [WriteOnly]
    public NativeArray<OrbitShadow.Terminator> Results;

    public double UT;

    public void Execute(int index)
    {
        var system = new SolarSystem(
            new MemorySpan<SystemBody>((SystemBody*)Bodies.GetUnsafeReadOnlyPtr(), Bodies.Length)
        );

        var task = Tasks[index];
        var request = Requests[task.Request];

        if (request.IsLanded)
        {
            Results[index] = LandedShadow.ComputeLandedTerminator(
                system,
                request.PlanetIndex,
                task.StarIndex,
                task.ReferenceIndex,
                request.Normal,
                request.CosLatitude,
                UT
            );
        }
        else
        {
            Results[index] = OrbitShadow.ComputeOrbitTerminator(
                system,
                in request.Orbit,
                task.StarIndex,
                UT
            );
        }
    }
}
