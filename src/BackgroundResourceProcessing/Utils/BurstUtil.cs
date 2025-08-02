using System.Runtime.CompilerServices;
using Unity.Burst;

namespace BackgroundResourceProcessing.Utils;

internal static class BurstUtil
{
    public static bool IsBurstCompiled
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            bool burst = true;
            Managed(ref burst);
            return burst;

            [BurstDiscard]
            static void Managed(ref bool burst) => burst = false;
        }
    }
}
