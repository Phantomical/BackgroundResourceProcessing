using System;

namespace UnifiedBackgroundProcessing.Utils
{
    public static class MathUtil
    {
        /// <summary>
        /// This is present in .NET standard 2.1 but KSP doesn't have that available.
        /// </summary>
        /// <param name="v"></param>
        /// <returns></returns>
        public static bool IsFinite(double v)
        {
            long bits = BitConverter.DoubleToInt64Bits(v);
            return (bits & 0x7FFFFFFFFFFFFFFF) < 0x7FF0000000000000;
        }
    }
}
