using BackgroundResourceProcessing.Collections.Burst;
using BackgroundResourceProcessing.Mathematics;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;

namespace BackgroundResourceProcessing.BurstSolver;

[BurstCompile]
internal unsafe struct ShadowJob : IJob
{
    [ReadOnly]
    public NativeArray<SystemBody> Bodies;

    [ReadOnly]
    public NativeArray<int> StarIndices;

    /// <summary>
    /// Per-star reference body indices. Only used for landed computations.
    /// </summary>
    [ReadOnly]
    public NativeArray<int> ReferenceIndices;

    public NativeArray<OrbitShadow.Terminator> Results;

    public Mathematics.Orbit VesselOrbit;
    public int PlanetIndex;
    public Vector3d Normal;
    public double CosLatitude;
    public double UT;
    public int StarCount;
    public bool IsLanded;

    public void Execute()
    {
        var system = new SolarSystem(
            new MemorySpan<SystemBody>((SystemBody*)Bodies.GetUnsafeReadOnlyPtr(), Bodies.Length)
        );

        for (int i = 0; i < StarCount; i++)
        {
            int starIndex = StarIndices[i];

            if (IsLanded)
            {
                Results[i] = LandedShadow.ComputeLandedTerminator(
                    system,
                    PlanetIndex,
                    starIndex,
                    ReferenceIndices[i],
                    Normal,
                    CosLatitude,
                    UT
                );
            }
            else
            {
                Results[i] = OrbitShadow.ComputeOrbitTerminator(
                    system,
                    in VesselOrbit,
                    starIndex,
                    UT
                );
            }
        }
    }
}
