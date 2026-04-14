using System;
using BackgroundResourceProcessing.Core;
using BackgroundResourceProcessing.Utils;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

namespace BackgroundResourceProcessing.BurstSolver;

/// <summary>
/// Represents an in-flight or completed solver computation.
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
/// using var solve = processor.ComputeRates();
/// solve.Complete();
/// </code>
/// </para>
///
/// <para>
/// For coroutine callers:
/// <code>
/// using var solve = processor.ComputeRates();
/// yield return solve;
/// solve.Complete();
/// </code>
/// </para>
/// </remarks>
internal class SolveHandle : CustomYieldInstruction, IDisposable
{
    readonly ResourceProcessor processor;
    readonly int cacheHash;
    readonly bool isScheduled;

    JobHandle jobHandle;
    NativeArray<double> nativeInventoryRates;
    NativeArray<double> nativeConverterRates;
    NativeArray<int> errorCode;
    SolverSolution cachedSolution;

    bool completed;

    /// <summary>
    /// The changepoint time associated with this solve, if any.
    /// Set by the caller after scheduling the solve.
    /// </summary>
    public double Changepoint { get; set; }

    /// <summary>
    /// Create a handle for a cache-hit result. No job is scheduled;
    /// <see cref="keepWaiting"/> returns <c>false</c> immediately.
    /// </summary>
    internal SolveHandle(ResourceProcessor processor, SolverSolution cached)
    {
        this.processor = processor;
        this.cachedSolution = cached;
    }

    /// <summary>
    /// Create a handle for a scheduled solver job.
    /// </summary>
    internal SolveHandle(
        ResourceProcessor processor,
        JobHandle jobHandle,
        NativeArray<double> inventoryRates,
        NativeArray<double> converterRates,
        NativeArray<int> errorCode,
        int cacheHash
    )
    {
        this.processor = processor;
        this.jobHandle = jobHandle;
        this.nativeInventoryRates = inventoryRates;
        this.nativeConverterRates = converterRates;
        this.errorCode = errorCode;
        this.cacheHash = cacheHash;
        this.isScheduled = true;
    }

    /// <inheritdoc/>
    public override bool keepWaiting => !completed && !jobHandle.IsCompleted;

    /// <summary>
    /// Block until the solver job completes and apply the computed rates
    /// to the processor's inventories and converters.
    /// </summary>
    ///
    /// <remarks>
    /// This method is idempotent. Calling it multiple times is safe.
    /// </remarks>
    public void Complete()
    {
        if (completed)
            return;

        try
        {
            SolverSolution soln;
            if (isScheduled)
            {
                jobHandle.Complete();
                jobHandle = default;

                if (errorCode[0] != 0)
                    ((Error)errorCode[0]).ThrowRepresentativeError();

                soln = new SolverSolution
                {
                    inventoryRates = [.. nativeInventoryRates],
                    converterRates = [.. nativeConverterRates],
                };

                ResourceProcessor.CacheSolution(cacheHash, processor, soln);
            }
            else
            {
                soln = cachedSolution;
            }

            foreach (var inventory in processor.inventories)
                inventory.Rate = 0.0;
            foreach (var converter in processor.converters)
                converter.Rate = 0.0;

            for (int i = 0; i < processor.inventories.Count; ++i)
            {
                var rate = soln.inventoryRates[i];
                if (!MathUtil.IsFinite(rate))
                    throw new Exception(
                        $"Rate for inventory {processor.inventories[i].Id} was {rate}"
                    );
                processor.inventories[i].Rate = rate;
            }

            for (int i = 0; i < processor.converters.Count; ++i)
            {
                double rate = soln.converterRates[i];
                if (!MathUtil.IsFinite(rate))
                    throw new Exception($"Rate for converter {i} was {rate}");
                processor.converters[i].Rate = rate;
            }
        }
        catch (Exception e)
        {
            foreach (var inventory in processor.inventories)
                inventory.Rate = 0.0;
            foreach (var converter in processor.converters)
                converter.Rate = 0.0;

            processor.DumpCrashReport(e);
        }

        completed = true;
    }

    /// <summary>
    /// Free native resources owned by this handle. If the job has not
    /// yet completed, this will block until it does.
    /// </summary>
    public void Dispose()
    {
        jobHandle.Complete();
        jobHandle = default;

        nativeInventoryRates.Dispose();
        nativeInventoryRates = default;
        nativeConverterRates.Dispose();
        nativeConverterRates = default;
        errorCode.Dispose();
        errorCode = default;
    }
}
