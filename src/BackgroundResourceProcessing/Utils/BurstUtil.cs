using System.Runtime.CompilerServices;
using Unity.Burst;

namespace BackgroundResourceProcessing.Utils;

internal static class BurstUtil
{
#if DEBUG
    const bool IsDebug = true;
#else
    const bool IsDebug = false;
#endif

    public static bool IsBurstCompiled
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            bool burst = true;
            Managed(ref burst);
            return burst;

            [BurstDiscard]
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            static void Managed(ref bool burst) => burst = false;
        }
    }

    public static bool ExceptionsEnabled
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
#if DEBUG
            return true;
#else
            return !IsBurstCompiled;
#endif
        }
    }
}
