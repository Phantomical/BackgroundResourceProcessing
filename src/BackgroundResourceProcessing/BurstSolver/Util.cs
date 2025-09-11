using System;
using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Burst.CompilerServices;

namespace BackgroundResourceProcessing.BurstSolver;

internal static class BurstUtil
{
    internal class AssertionError(string message) : Exception(message) { }

    internal static bool IsBurstCompiled
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            [BurstDiscard]
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            static void Managed(ref bool burst) => burst = false;

            bool burst = true;
            Managed(ref burst);
            return burst;
        }
    }

    internal static bool Trace
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            [BurstDiscard]
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            static void Managed(ref bool trace) =>
                trace = DebugSettings.Instance?.SolverTrace ?? false;

            bool trace = false;
            Managed(ref trace);
            return trace;
        }
    }

    internal static bool UseTestAllocator
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            [BurstDiscard]
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            static void Managed(ref bool use) =>
                use = DebugSettings.Instance?.UseTestAllocator ?? false;

            bool use = false;
            Managed(ref use);
            return use;
        }
    }

    internal static bool SolverTrace
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            [BurstDiscard]
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            static void Managed(ref bool trace) =>
                trace = DebugSettings.Instance?.SolverTrace ?? false;

            bool trace = false;
            Managed(ref trace);
            return trace;
        }
    }

    internal static T Take<T>(ref T item)
    {
        var value = item;
        item = default;
        return value;
    }
}
