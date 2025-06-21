using System;
using System.Runtime.InteropServices;

namespace BackgroundResourceProcessing.Utils
{
    internal static class MathUtil
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

        public static bool ApproxEqual(double a, double b, double epsilon = 1e-6)
        {
            return Math.Abs(a - b) < epsilon;
        }

        public static double Clamp(double x, double lo, double hi)
        {
            return Math.Max(Math.Min(x, hi), lo);
        }

        internal static int TrailingZeroCount(ulong v)
        {
            int c = 64;

            v &= (ulong)-(long)v;
            if (v != 0)
                c--;
            if ((v & 0x00000000FFFFFFFF) != 0)
                c -= 32;
            if ((v & 0x0000FFFF0000FFFF) != 0)
                c -= 16;
            if ((v & 0x00FF00FF00FF00FF) != 0)
                c -= 8;
            if ((v & 0x0F0F0F0F0F0F0F0F) != 0)
                c -= 4;
            if ((v & 0x3333333333333333) != 0)
                c -= 2;
            if ((v & 0x5555555555555555) != 0)
                c -= 1;

            return c;
        }
    }
}
