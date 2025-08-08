using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using BackgroundResourceProcessing.Collections;
using Unity.Burst.Intrinsics;
using UnityEngine;

namespace BackgroundResourceProcessing.Utils;

internal static class MathUtil
{
    internal const double DEG2RAD = Math.PI / 180.0;
    internal const double RAD2DEG = 180.0 / Math.PI;

    /// <summary>
    /// This is present in .NET standard 2.1 but KSP doesn't have that available.
    /// </summary>
    /// <param name="v"></param>
    /// <returns></returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsFinite(double v)
    {
        long bits = BitConverter.DoubleToInt64Bits(v);
        return (bits & 0x7FFFFFFFFFFFFFFF) < 0x7FF0000000000000;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool ApproxEqual(double a, double b, double epsilon = 1e-6)
    {
        return Math.Abs(a - b) < epsilon;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double Clamp(double x, double lo, double hi)
    {
        if (x > hi)
            x = hi;
        if (x < lo)
            x = lo;
        return x;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Clamp(int x, int lo, int hi)
    {
        if (x > hi)
            x = hi;
        if (x < lo)
            x = lo;
        return x;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static (double, double) SinCos(double x)
    {
        return (Math.Sin(x), Math.Cos(x));
    }

    /// <summary>
    /// Computes <c>x - y*floor(x / y)</c>
    /// </summary>
    internal static double FMod(double x, double y)
    {
        var rem = x % y;
        if (rem < 0)
            rem += y;
        return rem;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static int TrailingZeroCount(ulong v)
    {
        if (!BurstUtil.IsBurstCompiled)
            return TrailingZeroCountFallback(v);

        if (X86.Bmi1.IsBmi1Supported)
            return (int)X86.Bmi1.tzcnt_u64(v);

        return TrailingZeroCountFallback(v);
    }

    private static int TrailingZeroCountFallback(ulong v)
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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static int PopCount(ulong x)
    {
        x -= (x >> 1) & 0x5555555555555555;
        x = (x & 0x3333333333333333) + ((x >> 2) & 0x3333333333333333);
        x = (x + (x >> 4)) & 0xF0F0F0F0F0F0F0F;
        return (int)((x * 0x101010101010101) >> 56);
    }

    /// <summary>
    /// Find the previous time at which the curve will have had value
    /// <paramref name="epsilon"/>.
    /// </summary>
    ///
    /// <param name="curve">The float curve.</param>
    /// <param name="start">The starting time.</param>
    /// <param name="epsilon">The value to check.</param>
    /// <returns>
    /// The resulting time, or <see cref="float.NegativeInfinity"/> otherwise.
    /// </returns>
    internal static float FindErrorBoundaryForward(FloatCurve curve, float start, float epsilon)
    {
        var index = FindKeyFrameIndex(curve, start);
        var frames = curve.Curve.keys;

        var current = curve.Evaluate(start);
        var hi = current * (1f + epsilon);
        var lo = current * (1f - epsilon);

        if (index == frames.Length)
            return float.PositiveInfinity;

        if (index == -1)
        {
            if (frames[0].value < lo || frames[0].value > hi)
                return frames[0].time;
            index = 0;
        }

        for (; index < frames.Length - 1; ++index)
        {
            var frame0 = frames[index];
            var frame1 = frames[index + 1];

            var dt = frame1.time - frame0.time;

            // We treat x as time here and use that to get the control points.
            var x0 = 0;
            var x1 = frame0.outWeight * dt;
            var x2 = dt * (1f - frame1.inWeight);
            var x3 = dt;

            // Then y is the float curve value
            var y0 = frame0.value;
            var y1 = frame0.value + frame0.outTangent * frame0.outWeight * dt;
            var y2 = frame1.value - frame1.inTangent * frame1.inWeight * dt;
            var y3 = frame1.value;

            // Solve y(t) = v for t
            // y(t) = (1-t)³ y0 + 3t(1-t)² y1 + 3t²(1-t) y2 + t³ y3
            var a = -y0 + 3 * y1 - 3 * y2 + y3;
            var b = 3 * y2 - 6 * y1 + 3 * y0;
            var c = -3 * y0 + 3 * y1;
            var d = y0;

            var min = Mathf.Min(frame0.value, frame1.value);
            var max = Mathf.Max(frame0.value, frame1.value);

            // Can this curve even return a value for v in the range 0 <= t <= 1
            var extremes = SolveQuadratic(3 * a, 2 * b, c);
            if (extremes != null)
            {
                var (t0, t1) = extremes.Value;

                if (0 <= t0 && t0 <= 1)
                {
                    var y = ((a * t0 + b) * t0 + c) * t0 + d;
                    min = Mathf.Min(min, y);
                    max = Mathf.Max(max, y);
                }

                if (0 <= t1 && t1 <= 1)
                {
                    var y = ((a * t1 + b) * t1 + c) * t1 + d;
                    min = Mathf.Min(min, y);
                    max = Mathf.Max(max, y);
                }
            }

            // We don't need to check this segment.
            if (lo <= min && max <= hi)
                continue;

            var guess = 0f;
            if (frame0.time < start)
                guess = (start - frame0.time) / dt;

            // Is there a solution?
            var hiS = SolveCubic01(a, b, c, d - hi, guess);
            var loS = SolveCubic01(a, b, c, d - lo, guess);

            if (hiS == null && loS == null)
                continue;

            // We now have a solution for t, so we just need to get the
            // time value out of the bezier.

            var t = Mathf.Min(hiS ?? float.PositiveInfinity, loS ?? float.PositiveInfinity);
            var u = 1f - t;

            var t2 = t * t;
            var t3 = t2 * t;
            var u2 = u * u;
            var u3 = u2 * u;

            var time = u3 * x0 + 3 * t * u2 * x1 + 3 * t2 * u * x2 + t3 * x3;
            if (time < start)
                continue;

            return time;
        }

        return float.PositiveInfinity;
    }

    /// <summary>
    /// Find the previous time at which the curve will have had value
    /// <paramref name="epsilon"/>.
    /// </summary>
    ///
    /// <param name="curve">The float curve.</param>
    /// <param name="start">The starting time.</param>
    /// <param name="epsilon">The value to check.</param>
    /// <returns>
    /// The resulting time, or <see cref="float.NegativeInfinity"/> otherwise.
    /// </returns>
    internal static float FindErrorBoundaryBackward(FloatCurve curve, float start, float epsilon)
    {
        var index = FindKeyFrameIndex(curve, start);
        var frames = curve.Curve.keys;

        var current = curve.Evaluate(start);
        var hi = current * (1f + epsilon);
        var lo = current * (1f - epsilon);

        if (index == -1)
            return float.PositiveInfinity;

        if (index == frames.Length)
        {
            var last = frames.Length - 1;
            if (frames[last].value < lo || frames[last].value > hi)
                return frames[0].time;
            index = last;
        }

        for (; index > 0; --index)
        {
            var frame0 = frames[index];
            var frame1 = frames[index - 1];

            var dt = frame1.time - frame0.time;

            // We treat x as time here and use that to get the control points.
            var x0 = 0;
            var x1 = frame0.outWeight * dt;
            var x2 = dt * (1f - frame1.inWeight);
            var x3 = dt;

            // Then y is the float curve value
            var y0 = frame0.value;
            var y1 = frame0.value + frame0.outTangent * frame0.outWeight * dt;
            var y2 = frame1.value - frame1.inTangent * frame1.inWeight * dt;
            var y3 = frame1.value;

            // Solve y(t) = v for t
            // y(t) = (1-t)³ y0 + 3t(1-t)² y1 + 3t²(1-t) y2 + t³ y3
            var a = -y0 + 3 * y1 - 3 * y2 + y3;
            var b = 3 * y2 - 6 * y1 + 3 * y0;
            var c = -3 * y0 + 3 * y1;
            var d = y0;

            var min = Mathf.Min(frame0.value, frame1.value);
            var max = Mathf.Max(frame0.value, frame1.value);

            // Can this curve even return a value for v in the range 0 <= t <= 1
            var extremes = SolveQuadratic(3 * a, 2 * b, c);
            if (extremes != null)
            {
                var (t0, t1) = extremes.Value;

                if (0 <= t0 && t0 <= 1)
                {
                    var y = ((a * t0 + b) * t0 + c) * t0 + d;
                    min = Mathf.Min(min, y);
                    max = Mathf.Max(max, y);
                }

                if (0 <= t1 && t1 <= 1)
                {
                    var y = ((a * t1 + b) * t1 + c) * t1 + d;
                    min = Mathf.Min(min, y);
                    max = Mathf.Max(max, y);
                }
            }

            // We don't need to check this segment.
            if (lo <= min && max <= hi)
                continue;

            var guess = 0f;
            if (frame1.time > start)
                guess = (start - frame1.time) / dt;

            // Is there a solution?
            var hiS = SolveCubic01(a, b, c, d - hi, guess);
            var loS = SolveCubic01(a, b, c, d - lo, guess);

            if (hiS == null && loS == null)
                continue;

            // We now have a solution for t, so we just need to get the
            // time value out of the bezier.

            var t = Mathf.Max(hiS ?? float.NegativeInfinity, loS ?? float.NegativeInfinity);
            var u = 1f - t;

            var t2 = t * t;
            var t3 = t2 * t;
            var u2 = u * u;
            var u3 = u2 * u;

            var time = u3 * x0 + 3 * t * u2 * x1 + 3 * t2 * u * x2 + t3 * x3;
            if (time > start)
                continue;

            return time;
        }

        return float.PositiveInfinity;
    }

    private static int FindKeyFrameIndex(FloatCurve curve, float time)
    {
        var frames = curve.Curve.keys;

        if (frames.Length == 0)
            return -1;

        for (int i = 0; i < frames.Length; ++i)
        {
            if (frames[i].time <= time)
                return i;
        }

        return frames.Length;
    }

    private static KeyValuePair<float, float>? SolveQuadratic(float a, float b, float c)
    {
        var det = b * b - 4 * a * c;
        if (det < 0)
            return null;

        var sqrtdet = Mathf.Sqrt(det);
        var soln1 = (-b + sqrtdet) / (2 * a);
        var soln2 = (-b - sqrtdet) / (2 * a);

        return new(soln1, soln2);
    }

    enum CubicStop
    {
        Success,
        Low,
        Hi,
    }

    /// <summary>
    /// Solve a cubic starting with an initial guess.
    /// </summary>
    /// <returns></returns>
    private static float? SolveCubic01(float a, float b, float c, float d, float guess)
    {
        var t = guess;

        for (int i = 0; i < 10; ++i)
        {
            var y = ((a * t + b) * t + c) * t + d;
            var dy = (3 * a * t + 2 * b) * t + c;

            if (ApproxEqual(y, 0))
                break;
            if (dy == 0.0)
                break;

            if (t < 0 && dy < 0)
                return null;
            if (t > 1 && dy > 1)
                return null;

            t -= y / dy;
        }

        if (t < 0)
            return null;

        if (t > 1)
            return null;

        return t;
    }

    /// <summary>
    /// Bisects an area, returning a tighter boundary around the location where
    /// <paramref name="isIn"/> switches from <c>false</c> to <c>true</c>.
    /// </summary>
    /// <returns></returns>
    internal static (double, double) Bisect(
        double inP,
        double outP,
        Func<double, bool> isIn,
        int iterations = 8
    )
    {
        for (int i = 0; i < iterations; ++i)
        {
            double probe = 0.5 * (inP + outP);

            if (isIn(probe))
                inP = probe;
            else
                outP = probe;
        }

        return (inP, outP);
    }
}
