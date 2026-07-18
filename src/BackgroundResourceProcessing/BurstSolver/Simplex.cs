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
using BackgroundResourceProcessing.Collections.Burst;
using BackgroundResourceProcessing.Utils;
using Unity.Burst;
using Unity.Burst.CompilerServices;
using Unity.Burst.Intrinsics;
using static Unity.Burst.Intrinsics.X86.Avx;
using static Unity.Burst.Intrinsics.X86.Avx2;
using static Unity.Burst.Intrinsics.X86.Sse2;
using static Unity.Burst.Intrinsics.X86.Sse4_1;

namespace BackgroundResourceProcessing.BurstSolver;

[BurstCompile]
internal static partial class Simplex
{
    const double Epsilon = 1e-9;
    const uint MaxIterations = 1000;

    /// <summary>
    /// The number of pivots to take under Dantzig's rule before falling back
    /// to Bland's rule.
    /// </summary>
    ///
    /// <remarks>
    /// Dantzig's rule (most negative reduced cost, lowest column index
    /// breaking ties) picks good pivots but can cycle forever on degenerate
    /// problems, and the problems built here are frequently degenerate -- every
    /// inventory pinned at empty or full contributes a zero-RHS row. Bland's
    /// rule cannot cycle but picks worse pivots, so it is only worth switching
    /// to once a solve has gone on long enough that something is clearly wrong.
    /// Real problems settle in far fewer pivots than this.
    /// </remarks>
    const uint BlandThreshold = 100;

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
            static void Managed(ref bool trace) =>
                trace = DebugSettings.Instance?.SolverTrace ?? false;
        }
    }

#if BURST_TEST_OVERRIDE_AVX2
    private static bool IsAvx2Supported => true;
#endif

    [MustUseReturnValue]
    public static unsafe bool SolveTableau(Matrix tableau, BitSpan selected, int slackStart)
    {
        if (selected.Capacity < tableau.Cols - 1)
            BurstCrashHandler.Crash(Error.Simplex_SolveTableau_SelectedTooSmall);

        // The column index of the variable that is currently basic in each row.
        // Row y starts out basic in its own slack variable. Bland's rule needs
        // this to break ratio-test ties by variable index rather than by row
        // index, which is what makes it immune to cycling.
        int* basis = stackalloc int[tableau.Rows];
        for (int y = 1; y < tableau.Rows; ++y)
            basis[y] = slackStart + y - 1;

        for (uint iter = 0; iter < MaxIterations; ++iter)
        {
            bool bland = iter >= BlandThreshold;

            int pivot = bland
                ? SelectPivotBland(tableau.GetRowPtr(0), tableau.Cols - 1)
                : SelectPivot(tableau.GetRowPtr(0), tableau.Cols - 1);
            if (pivot < 0)
            {
                // No improving column left, so the tableau is optimal.
                if (Trace)
                    TraceFinal(tableau);

                return true;
            }

            int index = bland ? SelectRowBland(tableau, pivot, basis) : SelectRow(tableau, pivot);
            if (index < 0)
                return false;

            if (Trace)
                TracePivot(pivot, index, tableau);

            selected[pivot] = true;
            basis[index] = pivot;
            tableau.InvScaleRow(index, tableau[index, pivot]);

            for (int y = 0; y < tableau.Rows; ++y)
            {
                if (y == index)
                    continue;

                tableau.ScaleReduce(y, index, pivot);
            }
        }

        // Running out of iterations means optimality was never reached. Every
        // pivot preserves feasibility, so the tableau still holds a perfectly
        // valid-looking point -- returning it would quietly hand back a
        // suboptimal answer with converters running slower than they should and
        // nothing anywhere to say so. Report the failure instead.
        WarnIterationLimit();
        return false;
    }

    /// <summary>
    /// Bland's entering rule: the lowest-index column with a negative reduced
    /// cost, rather than the most negative one.
    /// </summary>
    private static unsafe int SelectPivotBland(
        [NoAlias] double* row,
        [AssumeRange(0, int.MaxValue)] int length
    )
    {
        for (int x = 0; x < length; ++x)
        {
            if (row[x] < 0.0)
                return x;
        }

        return -1;
    }

    /// <summary>
    /// Bland's leaving rule: the usual minimum ratio, but ties are broken by
    /// the lowest-index basic variable instead of by the lowest row.
    /// </summary>
    private static unsafe int SelectRowBland(Matrix tableau, int pivot, [NoAlias] int* basis)
    {
        int index = -1;
        double value = double.PositiveInfinity;

        for (int y = 1; y < tableau.Rows; ++y)
        {
            double* row = tableau.GetRowPtr(y);
            double den = row[pivot];

            if (den <= 0.0)
                continue;

            double ratio = row[tableau.Cols - 1] / den;

            // We ignore all negative ratios
            if (ratio < 0.0)
                continue;

            if (index < 0 || ratio < value || (ratio == value && basis[y] < basis[index]))
            {
                index = y;
                value = ratio;
            }
        }

        return index;
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
            var hmask = cmple_pd(vlo, vhi);
            var hpivot = blendv_pd(ihi, ilo, hmask);

            double lo = cvtsd_f64(hvalue);
            double hi = cvtsd_f64(unpackhi_pd(hvalue, hvalue));

            if (lo <= hi)
                return (int)extract_epi64(hpivot, 0);
            else
                return (int)extract_epi64(hpivot, 1);
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

    private static unsafe int SelectRow(Matrix tableau, int pivot)
    {
        int index = -1;
        double value = double.PositiveInfinity;

        for (int y = 1; y < tableau.Rows; ++y)
        {
            double* row = tableau.GetRowPtr(y);
            double den = row[pivot];
            double num = row[tableau.Cols - 1];
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
            if (Math.Abs(r) >= Epsilon)
                continue;

            // If d and s almost perfectly cancel out then just truncate to 0.
            if (Math.Abs(r) / (Math.Abs(d) + Math.Abs(s)) < Epsilon)
                dst[i] = 0.0;
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
            return mm256_andnot_si256(x, mm256_set1_epi64x(-1));
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
    static void WarnIterationLimit() =>
        LogUtil.Warn(
            $"Simplex did not converge within {MaxIterations} iterations. ",
            "Treating the problem as unsolved rather than returning a suboptimal solution."
        );

    [BurstDiscard]
    static void TraceSelectRow(double y, double num, double den, double ratio)
    {
        LogUtil.Log($"Considering row {y} {num:g4}/{den:g4} = {ratio:g4}");
    }

    [BurstDiscard]
    static unsafe void TracePivot(int col, int row, Matrix tableau) =>
        LogUtil.Log($"Pivoting on column {col}, row {row}:\n{tableau}");

    [BurstDiscard]
    static unsafe void TraceFinal(Matrix tableau) => LogUtil.Log($"Final:\n{tableau}");
}
