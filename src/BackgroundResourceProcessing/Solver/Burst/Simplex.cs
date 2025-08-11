#if BURST_TEST_OVERRIDE
#  if BURST_TEST_OVERRIDE_AVX2
#    define BURST_TEST_OVERRIDE_SSE41
#  endif
#  if BURST_TEST_OVERRIDE_SSE41
#    define BURST_TEST_OVERRIDE_SSE2
#  endif
#endif

using System;
using System.Runtime.CompilerServices;
using BackgroundResourceProcessing.Collections.Unsafe;
using BackgroundResourceProcessing.Tracing;
using Unity.Burst;
using Unity.Burst.CompilerServices;
using Unity.Burst.Intrinsics;
using static Unity.Burst.Intrinsics.X86.Avx;
using static Unity.Burst.Intrinsics.X86.Avx2;
using static Unity.Burst.Intrinsics.X86.Sse2;
using static Unity.Burst.Intrinsics.X86.Sse4_1;

namespace BackgroundResourceProcessing.Solver.Burst;

[BurstCompile]
internal static partial class Simplex
{
    const double Epsilon = 1e-9;
    const uint MaxIterations = 1000;

    private static bool Trace
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            bool trace = false;
            Managed(ref trace);
            return trace;

            [BurstDiscard]
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            static bool Managed(ref bool trace) =>
                trace = DebugSettings.Instance?.SolverTrace ?? false;
        }
    }

#if BURST_TEST_OVERRIDE
#if BURST_TEST_OVERRIDE_AVX2
    private static bool IsAvx2Supported => true;
#endif
#if BURST_TEST_OVERRIDE_SSE41
    private static bool IsSse41Supported => true;
#endif

#if BURST_TEST_OVERRIDE_SSE2
    private static bool IsSse2Supported => true;
#endif
#endif

    public static void SolveTableau(Collections.Matrix tableau, Collections.BitSet selected)
    {
        using var span = new TraceSpan("Burst.Simplex.SolveTableau");

        unsafe
        {
            fixed (double* _tableau = tableau.Values)
            {
                fixed (ulong* _selected = selected.Bits)
                {
                    var solvable = SolveTableau(
                        _tableau,
                        tableau.Width,
                        tableau.Height,
                        _selected,
                        selected.Bits.Length
                    );

                    if (!solvable)
                        throw new UnsolvableProblemException();
                }
            }
        }
    }

    [BurstCompile]
    private static unsafe bool SolveTableau(
        [NoAlias] double* tableau,
        [AssumeRange(0, int.MaxValue)] int width,
        [AssumeRange(0, int.MaxValue)] int height,
        [NoAlias] ulong* bits,
        [AssumeRange(0, int.MaxValue)] int length
    )
    {
        BitSpan selected = new(new(bits, (uint)length));

        for (uint iter = 0; iter < MaxIterations; ++iter)
        {
            int pivot = SelectPivot(tableau, width - 1);
            if (pivot < 0)
                break;

            int index = SelectRow(tableau, width, height, pivot);
            if (index < 0)
                return false;

            if (Trace)
                TracePivot(pivot, index, tableau, width, height);

            selected[(uint)pivot] = true;

            double* src = &tableau[index * width];
            InvScaleRow(src, width, src[pivot]);

            for (int y = 0; y < height; ++y)
            {
                if (y == index)
                    continue;

                double* dst = &tableau[y * width];
                double scale = dst[pivot];

                if (scale == 0.0)
                    continue;

                ScaleReduce(src, dst, scale, width);
            }
        }

        if (Trace)
            TraceFinal(tableau, width, height);

        return true;
    }

    private static unsafe int SelectPivot(
        [NoAlias] double* row,
        [AssumeRange(0, int.MaxValue)] int length
    )
    {
        if (IsAvx2Supported)
        {
            var vpivot = mm256_set1_epi64x(-1);
            var vvalue = mm256_set1_pd(0.0);
            var indices = mm256_set_epi64x(3, 2, 1, 0);

            int i = 0;
            for (; i + 4 <= length; i += 4)
            {
                var current = mm256_loadu_pd(&row[i]);
                var mask = mm256_cmp_pd(current, vvalue, (int)CMP.LT_OS);

                vvalue = mm256_min_pd(current, vvalue);
                vpivot = mm256_blendv_pd(vpivot, indices, mask);
                indices = mm256_add_epi64(indices, mm256_set1_epi64x(4));
            }

            if (i < length)
            {
                var ltmask = mm256_cmplt_epi64(indices, mm256_set1_epi64x(length));
                var current = mm256_maskload_pd(&row[i], ltmask);
                indices = mm256_or_si256(indices, mm256_not_si256(ltmask));

                var mask = mm256_cmp_pd(current, vvalue, (int)CMP.LT_OS);
                vvalue = mm256_min_pd(current, vvalue);
                vpivot = mm256_blendv_pd(vpivot, indices, mask);
            }

            var vlo = mm256_extractf128_pd(vvalue, 0);
            var vhi = mm256_extractf128_pd(vvalue, 1);
            var ilo = mm256_extractf128_si256(vpivot, 0);
            var ihi = mm256_extractf128_si256(vpivot, 1);

            var hvalue = min_pd(vlo, vhi);
            var hmask = cmplt_pd(vlo, vhi);
            var hpivot = blendv_pd(ihi, ilo, hmask);

            double lo = cvtsd_f64(hvalue);
            double hi = cvtsd_f64(unpackhi_pd(hvalue, hvalue));

            if (lo < hi)
                return (int)extract_epi64(hpivot, 0);
            else
                return (int)extract_epi64(hpivot, 1);
        }
        else if (IsSse2Supported)
        {
            var vpivot = set1_epi64x(-1);
            var vvalue = set1_pd(0.0);
            var indices = set_epi64x(1, 0);

            int i = 0;
            for (; i + 2 <= length; i += 2)
            {
                var current = loadu_si128(&row[i]);
                var mask = cmplt_pd(current, vvalue);

                if (IsSse41Supported)
                    vpivot = blendv_pd(vpivot, indices, mask);
                else
                    vpivot = or_pd(andnot_pd(mask, vpivot), and_pd(mask, indices));

                vvalue = min_pd(current, vvalue);
                indices = add_epi64(indices, set1_epi64x(2));
            }

            double lo = cvtsd_f64(vvalue);
            double hi = cvtsd_f64(unpackhi_pd(vvalue, vvalue));

            double value;
            int pivot;

            if (lo <= hi)
            {
                value = lo;
                pivot = (int)extract_epi64(vpivot, 0);
            }
            else
            {
                value = hi;
                pivot = (int)extract_epi64(vpivot, 1);
            }

            if (i < length)
            {
                double current = row[i];
                if (current < value)
                {
                    value = current;
                    pivot = i;
                }
            }

            return pivot;
        }
        else
        {
            int pivot = -1;
            double value = 0.0;

            for (int x = 0; x < length; ++x)
            {
                var current = row[x];
                if (current < value)
                {
                    pivot = x;
                    value = current;
                }
            }

            return pivot;
        }
    }

    private static unsafe int SelectRow(double* tableau, int width, int height, int pivot)
    {
        int index = -1;
        double value = double.PositiveInfinity;

        for (int y = 1; y < height; ++y)
        {
            double* row = &tableau[y * width];
            double den = row[pivot];
            double num = row[width - 1];
            double ratio = num / den;

            if (den <= 0.0)
                continue;

            // We ignore all negative ratios
            if (ratio < 0.0)
                continue;

            if (Trace)
                TraceSelectRow(y, num, den, ratio);

            if (ratio < value)
            {
                index = y;
                value = ratio;
            }
        }

        return index;
    }

    private static unsafe void InvScaleRow(
        [NoAlias] double* row,
        [AssumeRange(0, int.MaxValue)] int length,
        double scale
    )
    {
        if (scale == 1.0)
            return;

        int i = 0;

        if (IsAvxSupported)
        {
            var vscale = mm256_set1_pd(scale);

            for (; i <= length - 4; i += 4)
                mm256_storeu_pd(&row[i], mm256_div_pd(mm256_loadu_pd(&row[i]), vscale));

            if (i <= length - 2)
            {
                storeu_si128(&row[i], div_pd(loadu_si128(&row[i]), set1_pd(scale)));
                i += 2;
            }

            if (i < length)
                row[i] /= scale;
        }
        else if (IsSse2Supported)
        {
            var vscale = set1_pd(scale);

            for (; i <= length - 2; i += 2)
                storeu_si128(&row[i], div_pd(loadu_si128(&row[i]), vscale));

            if (i < length)
                row[i] /= scale;
        }
        else
        {
            for (; i < length; ++i)
                row[i] /= scale;
        }
    }

    private static unsafe void ScaleReduce(
        [NoAlias] double* src,
        [NoAlias] double* dst,
        double scale,
        [AssumeRange(0, int.MaxValue)] int length
    )
    {
        if (scale == 0.0)
            return;

        int i = 0;
        if (IsAvx2Supported)
        {
            v256 vscale = mm256_set1_pd(scale);
            v256 epsilon = mm256_set1_pd(Epsilon);

            for (; i + 4 <= length; i += 4)
            {
                var d = mm256_loadu_pd(&dst[i]);
                var s = mm256_mul_pd(mm256_loadu_pd(&src[i]), vscale);
                var r = mm256_sub_pd(d, s);

                var absd = mm256_abs_pd(d);
                var abss = mm256_abs_pd(s);
                var absr = mm256_abs_pd(r);
                var rel = mm256_div_pd(absr, mm256_add_pd(abss, absd));

                var mask1 = mm256_cmp_pd(absr, epsilon, (int)CMP.LT_OS);
                var mask2 = mm256_cmp_pd(rel, epsilon, (int)CMP.LT_OS);
                var mask = mm256_and_pd(mask1, mask2);

                mm256_storeu_pd(&dst[i], mm256_andnot_pd(mask, r));
            }

            Hint.Assume(length - i < 4);
        }

        if (IsSse2Supported)
        {
            v128 vscale = set1_pd(scale);
            v128 epsilon = set1_pd(Epsilon);

            for (; i + 2 <= length; i += 2)
            {
                v128 d = loadu_si128(&dst[i]);
                v128 s = mul_pd(loadu_si128(&src[i]), vscale);
                v128 r = sub_pd(d, s);

                v128 absr = abs_pd(r);
                v128 rel = div_pd(absr, add_pd(abs_pd(d), abs_pd(s)));

                v128 mask1 = cmpge_pd(absr, epsilon);
                v128 mask2 = cmpge_pd(rel, epsilon);
                v128 mask = or_pd(mask1, mask2);

                storeu_si128(&dst[i], and_pd(r, mask));
            }

            Hint.Assume(length - i < 2);
        }

        for (; i < length; ++i)
        {
            var d = dst[i];
            var s = src[i] * scale;
            var r = d - s;

            dst[i] = r;

            // Some hacks to attempt to truncate numerical errors down to
            // 0 without breaking the simplex algorithm when working with
            // big-M constants.
            if (Math.Abs(r) >= 1e-9)
                continue;

            // If d and s almost perfectly cancel out then just truncate to 0.
            if (Math.Abs(r) / (Math.Abs(d) + Math.Abs(s)) < 1e-9)
                dst[i] = 0.0;
        }
    }

    [IgnoreWarning(1370)]
    private static v256 mm256_cmpge_epi64(v256 a, v256 b)
    {
        if (IsAvx2Supported)
        {
            return mm256_andnot_si256(mm256_cmpgt_epi64(a, b), mm256_set1_epi8(0xFF));
        }
        else
        {
            throw UnsupportedInstructionSet();
        }
    }

    [IgnoreWarning(1370)]
    private static v256 mm256_cmplt_epi64(v256 a, v256 b)
    {
        if (IsAvx2Supported)
            return mm256_cmpgt_epi64(b, a);
        else
            throw UnsupportedInstructionSet();
    }

    [IgnoreWarning(1370)]
    private static v256 mm256_not_si256(v256 x)
    {
        if (IsAvx2Supported)
            return mm256_andnot_si256(x, mm256_setzero_si256());
        else
            throw UnsupportedInstructionSet();
    }

    [IgnoreWarning(1370)]
    private static v256 mm256_abs_pd(v256 x)
    {
        if (IsAvx2Supported)
        {
            return mm256_andnot_pd(mm256_set1_pd(-0.0), x);
        }
        else
        {
            throw UnsupportedInstructionSet();
        }
    }

    [IgnoreWarning(1370)]
    private static v256 mm256_select_si256(v256 mask, v256 a, v256 b)
    {
        if (IsAvx2Supported)
        {
            var sa = mm256_and_si256(mask, a);
            var sb = mm256_andnot_si256(mask, b);
            return mm256_or_si256(sa, sb);
        }
        else
        {
            throw UnsupportedInstructionSet();
        }
    }

    [IgnoreWarning(1370)]
    private static v128 abs_pd(v128 x)
    {
        if (IsSse2Supported)
        {
            return andnot_pd(set1_pd(-0.0), x);
        }
        else
        {
            throw UnsupportedInstructionSet();
        }
    }

    // Select elements in a when mask is 1, b otherwise.
    [IgnoreWarning(1370)]
    private static v128 select_si128(v128 mask, v128 a, v128 b)
    {
        if (IsSse2Supported)
        {
            var sa = and_si128(mask, a);
            var sb = andnot_si128(mask, b);
            return or_si128(sa, sb);
        }
        else
        {
            throw UnsupportedInstructionSet();
        }
    }

    private static Exception UnsupportedInstructionSet() =>
        new("code path is not supported with the current instruction set");

    [IgnoreWarning(1370)]
    private static void ThrowUnsupportedInstructionSet() => throw UnsupportedInstructionSet();

    [BurstDiscard]
    static void TraceSelectRow(double y, double num, double den, double ratio)
    {
        LogUtil.Log($"Considering row {y} {num:g4}/{den:g4} = {ratio:g4}");
    }

    [BurstDiscard]
    static unsafe void TracePivot(int col, int row, double* _tableau, int width, int height)
    {
        Matrix tableau = new(_tableau, (uint)width, (uint)height);
        LogUtil.Log($"Pivoting on column {col}, row {row}:\n{tableau}");
    }

    [BurstDiscard]
    static unsafe void TraceFinal(double* _tableau, int width, int height)
    {
        Matrix tableau = new(_tableau, (uint)width, (uint)height);
        LogUtil.Log($"Final:\n{tableau}");
    }
}
