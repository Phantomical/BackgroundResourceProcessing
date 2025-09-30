using System.Runtime.CompilerServices;

namespace BackgroundResourceProcessing.Utils;

internal static class VectorExt
{
    // Unity uses a built-in method for the normalized property which is not
    // compatible with burst.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector3d Normalized(this Vector3d v)
    {
        return v * (1.0 / v.magnitude);
    }
}
