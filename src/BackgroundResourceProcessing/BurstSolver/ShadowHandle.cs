using System;
using System.Collections.Generic;
using BackgroundResourceProcessing.Mathematics;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

namespace BackgroundResourceProcessing.BurstSolver;

/// <summary>
/// Represents an in-flight or completed shadow state computation.
/// </summary>
///
/// <remarks>
/// <para>
/// This type serves double duty as both a <see cref="CustomYieldInstruction"/>
/// (for use with coroutines) and a synchronous completion handle (via
/// <see cref="Complete"/>).
/// </para>
///
/// <para>
/// For synchronous callers:
/// <code>
/// using var shadow = ShadowState.ScheduleShadowState(vessel);
/// processor.ShadowState = shadow.Complete();
/// </code>
/// </para>
///
/// <para>
/// For coroutine callers:
/// <code>
/// using var shadow = ShadowState.ScheduleShadowState(vessel);
/// yield return shadow;
/// processor.ShadowState = shadow.Complete();
/// </code>
/// </para>
/// </remarks>
internal class ShadowHandle : CustomYieldInstruction, IDisposable
{
    readonly bool isScheduled;
    bool completed;

    JobHandle jobHandle;
    NativeArray<SystemBody> bodies;
    NativeArray<int> starIndices;
    NativeArray<int> referenceIndices;
    NativeArray<OrbitShadow.Terminator> results;

    /// <summary>
    /// The managed star references, needed to construct the final
    /// <see cref="ShadowState"/> with the correct <c>Star</c> field.
    /// </summary>
    List<CelestialBody> stars;

    ShadowState cachedResult;

    public bool IsComplete => completed;

    /// <summary>
    /// Create a handle for an immediate result. No job is scheduled;
    /// <see cref="keepWaiting"/> returns <c>false</c> immediately.
    /// </summary>
    internal ShadowHandle(ShadowState result)
    {
        cachedResult = result;
        completed = true;
    }

    /// <summary>
    /// Create a handle for a scheduled shadow job.
    /// </summary>
    internal ShadowHandle(
        JobHandle jobHandle,
        NativeArray<SystemBody> bodies,
        NativeArray<int> starIndices,
        NativeArray<int> referenceIndices,
        NativeArray<OrbitShadow.Terminator> results,
        List<CelestialBody> stars
    )
    {
        this.isScheduled = true;
        this.jobHandle = jobHandle;
        this.bodies = bodies;
        this.starIndices = starIndices;
        this.referenceIndices = referenceIndices;
        this.results = results;
        this.stars = stars;
    }

    /// <inheritdoc/>
    public override bool keepWaiting => !completed && !jobHandle.IsCompleted;

    /// <summary>
    /// Block until the shadow job completes and return the computed shadow
    /// state.
    /// </summary>
    ///
    /// <remarks>
    /// This method is idempotent. Calling it multiple times is safe.
    /// </remarks>
    public ShadowState Complete()
    {
        if (completed)
            return cachedResult;

        try
        {
            if (isScheduled)
            {
                jobHandle.Complete();
                jobHandle = default;

                // Aggregate results using the same logic as
                // ShadowState.GetOrbitShadowState / GetLandedShadowState.
                var state = ShadowState.AlwaysInShadow();
                for (int i = 0; i < stars.Count; i++)
                {
                    var term = results[i];
                    if (term.UT == double.MaxValue)
                        term.UT = double.PositiveInfinity;

                    if (!term.InShadow)
                    {
                        cachedResult = new ShadowState(
                            Math.Min(term.UT, state.NextTerminatorEstimate),
                            false,
                            stars[i]
                        );
                        completed = true;
                        return cachedResult;
                    }

                    if (term.UT < state.NextTerminatorEstimate)
                        state = new ShadowState(term.UT, true);
                }

                cachedResult = state;
            }

            // Validate the result the same way GetShadowState does.
            if (double.IsNaN(cachedResult.NextTerminatorEstimate))
            {
                LogUtil.Error("Shadow state calculations returned NaN terminator estimate");
                cachedResult = ShadowState.DefaultForStar(Planetarium.fetch?.Sun);
            }
            else if (cachedResult.NextTerminatorEstimate < Planetarium.GetUniversalTime())
            {
                LogUtil.Error("Shadow state calculations returned a terminator in the past");
                cachedResult = ShadowState.DefaultForStar(Planetarium.fetch?.Sun);
            }
        }
        catch (Exception e)
        {
            LogUtil.Error($"Shadow state computation threw an exception: {e}");
            cachedResult = ShadowState.DefaultForStar(Planetarium.fetch?.Sun);
        }

        completed = true;
        return cachedResult;
    }

    /// <summary>
    /// Free native resources owned by this handle. If the job has not
    /// yet completed, this will block until it does.
    /// </summary>
    public void Dispose()
    {
        jobHandle.Complete();
        jobHandle = default;

        bodies.Dispose();
        bodies = default;
        starIndices.Dispose();
        starIndices = default;
        referenceIndices.Dispose();
        referenceIndices = default;
        results.Dispose();
        results = default;
    }
}
