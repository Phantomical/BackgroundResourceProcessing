using System;
using System.Collections.Generic;
using System.Linq;
using BackgroundResourceProcessing.BurstSolver;
using BackgroundResourceProcessing.Jobs;
using BackgroundResourceProcessing.Mathematics;
using BackgroundResourceProcessing.Tracing;
using BackgroundResourceProcessing.Utils;
using Unity.Collections;
using Unity.Jobs;

namespace BackgroundResourceProcessing.Shadow;

/// <summary>
/// A batch of shadow state computations for several vessels, run as a single
/// parallel job.
/// </summary>
///
/// <remarks>
/// <para>
/// Usage is: <see cref="Add"/> each vessel on the main thread, then
/// <see cref="Schedule"/> once. Later, once <see cref="IsCompleted"/> is true,
/// call <see cref="Complete"/> and read each vessel's result with
/// <see cref="GetResult"/>. Finally, <see cref="Dispose"/> the batch.
/// </para>
///
/// <para>
/// All managed data (stars, orbits, surface normals, the solar system) is
/// extracted on the main thread when vessels are added. The job itself only
/// computes one terminator per (vessel, star) pair. The solar system is
/// recorded once and shared by every vessel in the batch.
/// </para>
///
/// <para>
/// Native buffers use <see cref="Allocator.Persistent"/> because a batch may
/// outlive several frames (e.g. when the game is paused and
/// <c>FixedUpdate</c> stops running).
/// </para>
/// </remarks>
internal sealed class ShadowBatch(double UT) : IDisposable
{
    struct Entry
    {
        /// <summary>
        /// The result, if it was known without running the job.
        /// </summary>
        public ShadowState? Immediate;

        /// <summary>
        /// The slice of <see cref="tasks"/> that belongs to this entry.
        /// </summary>
        public int TaskStart;
        public List<CelestialBody> Stars;
    }

    public readonly double UT = UT;

    readonly List<Entry> entries = [];
    readonly List<ShadowRequest> requests = [];
    readonly List<ShadowTask> tasks = [];

    bool scheduled;
    bool completed;
    JobHandle handle;
    NativeArray<SystemBody> bodies;
    NativeArray<ShadowRequest> nativeRequests;
    NativeArray<ShadowTask> nativeTasks;
    NativeArray<OrbitShadow.Terminator> results;

    /// <summary>
    /// The number of vessels in this batch.
    /// </summary>
    public int Count => entries.Count;

    /// <summary>
    /// Whether the batch can be completed without blocking.
    /// </summary>
    public bool IsCompleted => !scheduled || completed || handle.IsCompleted;

    /// <summary>
    /// Add a vessel to this batch. Must be called before <see cref="Schedule"/>.
    /// </summary>
    /// <returns>The index to pass to <see cref="GetResult"/>.</returns>
    public int Add(Vessel vessel)
    {
        if (scheduled)
            throw new InvalidOperationException("Cannot add a vessel to a scheduled ShadowBatch");

        var index = entries.Count;
        entries.Add(
            vessel.situation switch
            {
                Vessel.Situations.ORBITING
                or Vessel.Situations.SUB_ORBITAL
                or Vessel.Situations.ESCAPING
                or Vessel.Situations.DOCKED => AddOrbit(vessel),
                Vessel.Situations.LANDED
                or Vessel.Situations.SPLASHED
                or Vessel.Situations.PRELAUNCH
                or Vessel.Situations.FLYING => AddLanded(vessel),
                _ => new Entry { Immediate = ShadowState.AlwaysInSun(Planetarium.fetch?.Sun) },
            }
        );
        return index;
    }

    Entry AddOrbit(Vessel vessel)
    {
        var stars = ShadowState.StarProvider.GetRelevantStars(vessel) ?? [];
        var settings = HighLogic.CurrentGame?.Parameters.CustomParams<Settings>();
        if (!(settings?.EnableOrbitShadows ?? false))
            return new Entry { Immediate = ShadowState.DefaultForStar(stars.FirstOrDefault()) };
        if (stars.Count == 0)
            return new Entry { Immediate = ShadowState.AlwaysInShadow() };

        var request = requests.Count;
        requests.Add(new ShadowRequest { Orbit = new Mathematics.Orbit(vessel) });

        var start = tasks.Count;
        foreach (var star in stars)
        {
            // Do not short-circuit when the vessel orbits a star directly.
            // ComputeOrbitTerminator handles that case, and every star needs a
            // result so that aggregation matches the synchronous path.
            tasks.Add(
                new ShadowTask
                {
                    Request = request,
                    StarIndex = (int)SolarSystem.GetBodyIndex(star),
                    ReferenceIndex = 0,
                }
            );
        }

        return new Entry { TaskStart = start, Stars = stars };
    }

    Entry AddLanded(Vessel vessel)
    {
        var stars = ShadowState.StarProvider.GetRelevantStars(vessel) ?? [];
        var settings = HighLogic.CurrentGame?.Parameters.CustomParams<Settings>();
        if (!(settings?.EnableLandedShadows ?? false))
            return new Entry { Immediate = ShadowState.DefaultForStar(stars.FirstOrDefault()) };
        if (stars.Count == 0)
            return new Entry { Immediate = ShadowState.AlwaysInShadow() };

        var planet = vessel.orbit.referenceBody;
        var request = requests.Count;
        requests.Add(
            new ShadowRequest
            {
                PlanetIndex = (int)SolarSystem.GetBodyIndex(planet),
                Normal = ShadowState.GetLandedSurfaceNormal(
                    planet,
                    vessel.latitude,
                    vessel.longitude,
                    UT
                ),
                CosLatitude = Math.Cos(Math.Abs(vessel.latitude) * MathUtil.DEG2RAD),
                IsLanded = true,
            }
        );

        var start = tasks.Count;
        foreach (var star in stars)
        {
            tasks.Add(
                new ShadowTask
                {
                    Request = request,
                    StarIndex = (int)SolarSystem.GetBodyIndex(star),
                    ReferenceIndex = ShadowState.FindReferenceBodyIndex(planet, star),
                }
            );
        }

        return new Entry { TaskStart = start, Stars = stars };
    }

    /// <summary>
    /// Schedule the job for every vessel added so far. Does nothing if no
    /// vessel needs the job.
    /// </summary>
    public void Schedule()
    {
        if (scheduled)
            throw new InvalidOperationException("ShadowBatch has already been scheduled");
        scheduled = true;

        if (tasks.Count == 0)
        {
            completed = true;
            return;
        }

        using var span = new TraceSpan("ShadowBatch.Schedule");

        BurstCrashHandler.Init();

        var system = SolarSystem.Record();
        bodies = new NativeArray<SystemBody>(system, Allocator.Persistent);
        nativeRequests = new NativeArray<ShadowRequest>(requests.ToArray(), Allocator.Persistent);
        nativeTasks = new NativeArray<ShadowTask>(tasks.ToArray(), Allocator.Persistent);
        results = new NativeArray<OrbitShadow.Terminator>(tasks.Count, Allocator.Persistent);

        var job = new ShadowBatchJob
        {
            Bodies = bodies,
            Requests = nativeRequests,
            Tasks = nativeTasks,
            Results = results,
            UT = UT,
        };

        handle = job.Schedule(tasks.Count, 1);
        JobHandle.ScheduleBatchedJobs();
    }

    /// <summary>
    /// Wait for the job to finish. This only blocks if <see cref="IsCompleted"/>
    /// is false.
    /// </summary>
    public void Complete()
    {
        if (!scheduled)
            Schedule();
        if (completed)
            return;

        handle.Complete();
        handle = default;
        completed = true;
    }

    /// <summary>
    /// Get the aggregated and validated shadow state for the vessel added at
    /// <paramref name="index"/>. The batch must be completed first.
    /// </summary>
    ///
    /// <remarks>
    /// Results are validated against the time the batch was created at, not
    /// the time it was completed. A terminator that falls between the two (which
    /// can easily happen at high time warp) is a valid result, and should be
    /// handled as a changepoint rather than rejected as being in the past.
    /// </remarks>
    public ShadowState GetResult(int index)
    {
        if (!completed)
            throw new InvalidOperationException("ShadowBatch has not been completed");

        var entry = entries[index];
        if (entry.Immediate is ShadowState immediate)
            return immediate;

        try
        {
            var terminators = new OrbitShadow.Terminator[entry.Stars.Count];
            for (int i = 0; i < terminators.Length; i++)
                terminators[i] = results[entry.TaskStart + i];

            return ShadowState.ValidateShadowState(
                ShadowState.AggregateShadowState(terminators, entry.Stars),
                UT
            );
        }
        catch (Exception e)
        {
            LogUtil.Error($"Shadow state computation threw an exception: {e}");
            return ShadowState.FallbackShadowState(UT);
        }
    }

    /// <summary>
    /// Free the native buffers. Blocks if the job is still running.
    /// </summary>
    public void Dispose()
    {
        handle.Complete();
        handle = default;
        completed = true;

        if (bodies.IsCreated)
            bodies.Dispose();
        if (nativeRequests.IsCreated)
            nativeRequests.Dispose();
        if (nativeTasks.IsCreated)
            nativeTasks.Dispose();
        if (results.IsCreated)
            results.Dispose();
    }
}
