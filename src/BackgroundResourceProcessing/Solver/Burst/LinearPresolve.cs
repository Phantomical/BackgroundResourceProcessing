using System;
using System.Runtime.CompilerServices;
using BackgroundResourceProcessing.Collections.Unsafe;
using BackgroundResourceProcessing.Tracing;
using Unity.Burst;
using Unity.Burst.CompilerServices;
using Unity.Burst.Intrinsics;
using static Unity.Burst.Intrinsics.X86.Avx;
using static Unity.Burst.Intrinsics.X86.Avx2;
using static Unity.Burst.Intrinsics.X86.Fma;
using static Unity.Burst.Intrinsics.X86.Sse2;
using ConstraintState = BackgroundResourceProcessing.Solver.LinearPresolve.ConstraintState;

namespace BackgroundResourceProcessing.Solver.Burst;

[BurstCompile]
internal static class LinearPresolve
{
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

    private static v256 mm256_fmadd_pd(v256 a, v256 b, v256 c)
    {
        return mm256_add_pd(mm256_mul_pd(a, b), c);
    }

    private static v128 fmadd_pd(v128 a, v128 b, v128 c)
    {
        return add_pd(mul_pd(a, b), c);
    }

    private static v128 fmadd_sd(v128 a, v128 b, v128 c)
    {
        return new(a.Double0 * b.Double0 + c.Double0);
    }
#endif

    public static bool Presolve(
        Collections.Matrix equations,
        Collections.BitSet zeros,
        int equalities,
        int inequalities
    )
    {
        using var span = new TraceSpan("LinearPresolve.Presolve");

        if (equalities < 0)
            throw new ArgumentOutOfRangeException(nameof(equalities));
        if (inequalities < 0)
            throw new ArgumentOutOfRangeException(nameof(inequalities));

        unsafe
        {
            fixed (double* matrix = equations.Values)
            {
                fixed (ulong* bits = zeros.Bits)
                {
                    var unsolvable = UnsolvableState.SOLVABLE;

                    var result = Presolve(
                        matrix,
                        equations.Width,
                        equations.Height,
                        bits,
                        zeros.Bits.Length,
                        equalities,
                        inequalities,
                        &unsolvable
                    );

                    if (unsolvable == UnsolvableState.UNSOLVABLE)
                        throw new UnsolvableProblemException();

                    return result;
                }
            }
        }

        throw new NotImplementedException();
    }

    [BurstCompile]
    private static unsafe bool Presolve(
        [NoAlias] double* matrix,
        [AssumeRange(0, int.MaxValue)] int width,
        [AssumeRange(0, int.MaxValue)] int height,
        [NoAlias] ulong* bits,
        [AssumeRange(0, int.MaxValue)] int length,
        [AssumeRange(0, int.MaxValue)] int equalities,
        [AssumeRange(0, int.MaxValue)] int inequalities,
        [NoAlias] UnsolvableState* unsolvableState
    )
    {
        BitSpan zeros = new(new(bits, (uint)length));
        ref var unsolvable = ref *unsolvableState;

        if (Trace)
            TraceInitialMatrix(matrix, width, height);

        if (
            !InferZeros(matrix, width, height, zeros, equalities, inequalities, ref unsolvable)
            && equalities == 0
        )
            return false;

        if (unsolvable == UnsolvableState.UNSOLVABLE)
            return false;

        do
        {
            if (Trace)
                TraceInferZeros(matrix, width, height);

            PartialRowReduce(matrix, width, height, equalities);

            if (Trace)
                TraceMatrix(matrix, width, height);
        } while (
            InferZeros(matrix, width, height, zeros, equalities, inequalities, ref unsolvable)
        );

        return true;
    }

    private static unsafe bool InferZeros(
        [NoAlias] double* matrix,
        [AssumeRange(0, int.MaxValue)] int width,
        [AssumeRange(0, int.MaxValue)] int height,
        BitSpan zeros,
        [AssumeRange(0, int.MaxValue)] int equalities,
        [AssumeRange(0, int.MaxValue)] int inequalities,
        ref UnsolvableState unsolvable
    )
    {
        int ineqStop = equalities + inequalities;
        var found = false;

        for (int y = 0; y < ineqStop; ++y)
        {
            double* row = &matrix[y * width];
            double constant = row[width - 1];

            ComputeRowBounds(row, width - 1, out var positive, out var negative);

            bool zero = positive && negative;

            if (y < equalities)
            {
                if (zero)
                {
                    if (constant != 0.0)
                        return SetUnsolvable(ref unsolvable);
                    continue;
                }
                else if (negative)
                {
                    if (constant > 0.0)
                        return SetUnsolvable(ref unsolvable);
                    if (constant < 0.0)
                        continue;
                }
                else if (positive)
                {
                    if (constant < 0.0)
                        return SetUnsolvable(ref unsolvable);
                    if (constant > 0.0)
                        continue;
                }
                else
                {
                    continue;
                }
            }
            else
            {
                if (zero)
                {
                    if (constant < 0.0)
                        return SetUnsolvable(ref unsolvable);
                    continue;
                }
                else if (positive)
                {
                    if (constant < 0.0)
                        return SetUnsolvable(ref unsolvable);
                    if (constant > 0.0)
                        continue;
                }
                else
                {
                    continue;
                }
            }

            for (int x = 0; x < width - 1; ++x)
            {
                if (row[x] == 0.0)
                    continue;

                zeros[x] = true;
                found = true;

                for (int i = 0; i < height; ++i)
                    matrix[width * i + x] = 0.0;
            }
        }

        return found;
    }

    private static unsafe void PartialRowReduce(
        [NoAlias] double* matrix,
        [AssumeRange(0, int.MaxValue)] int width,
        [AssumeRange(0, int.MaxValue)] int height,
        [AssumeRange(0, int.MaxValue)] int stop
    )
    {
        if (stop >= height)
            ThrowStopIndexOutOfRange();

        Matrix tableau = new(matrix, (uint)width, (uint)height);

        for (int pc = 0, pr = 0; pc < width && pr < stop; ++pc)
        {
            int rmax = -1;
            double vmax = 0.0;

            for (int r = pr; r < stop; ++r)
            {
                var elem = matrix[r * width + pc];
                var abs = Math.Abs(elem);

                if (abs > vmax)
                {
                    rmax = r;
                    vmax = abs;
                }
            }

            if (rmax == -1)
                continue;

            if (rmax != pr)
                SwapRows(&matrix[rmax * width], &matrix[pr * width], width);

            double* selected = &matrix[pr * width];
            double coef = selected[pc];
            for (int c = pc; c < width; ++c)
                selected[c] /= coef;

            if (IsAvx2Supported)
            {
                int r = 0;
                for (; r + 4 <= height; r += 4)
                {
                    double** row = stackalloc double*[4];
                    double* f = stackalloc double[4];
                    var vf = stackalloc v256[4];

                    for (int i = 0; i < 4; ++i)
                    {
                        row[i] = &matrix[(r + i) * width];
                        f[i] = -row[i][pc];

                        if (r + i == pr)
                            f[i] = 0.0;
                        vf[i] = mm256_set1_pd(f[i]);
                    }

                    if (f[0] == 0.0 && f[1] == 0.0 && f[2] == 0.0 && f[3] == 0.0)
                        continue;

                    int c = pc + 1;
                    for (; c + 4 <= width; c += 4)
                    {
                        var src = mm256_loadu_pd(&selected[c]);

                        for (int i = 0; i < 4; ++i)
                        {
                            var dst = mm256_loadu_pd(&row[i][c]);
                            mm256_storeu_pd(&row[i][c], mm256_fmadd_pd(src, vf[i], dst));
                        }
                    }

                    if (c + 2 <= width)
                    {
                        var src = loadu_si128(&selected[c]);

                        for (int i = 0; i < 4; ++i)
                        {
                            var hf = mm256_extractf128_pd(vf[i], 0);
                            var dst = loadu_si128(&row[i][c]);
                            storeu_si128(&row[i][c], fmadd_pd(src, hf, dst));
                        }

                        c += 2;
                    }

                    if (c < width)
                    {
                        var src = set_sd(selected[c]);
                        for (int i = 0; i < 4; ++i)
                        {
                            var hf = mm256_extractf128_pd(vf[i], 0);
                            row[i][c] = cvtsd_f64(fmadd_sd(src, hf, set_sd(row[i][c])));
                        }
                    }

                    for (int i = 0; i < 4; ++i)
                        row[i][pc] = 0.0;
                }

                for (; r < height; ++r)
                {
                    if (r == pr)
                        continue;

                    double* row = &matrix[r * width];
                    if (row[pc] == 0.0)
                        continue;

                    double f = -row[pc];
                    var vf = mm256_set1_pd(f);
                    var hf = set1_pd(f);
                    int c = pc + 1;

                    for (; c + 4 <= width; c += 4)
                    {
                        var dst = mm256_loadu_pd(&row[c]);
                        var src = mm256_loadu_pd(&selected[c]);
                        mm256_storeu_pd(&row[c], mm256_fmadd_pd(src, vf, dst));
                    }

                    if (c + 2 <= width)
                    {
                        var dst = loadu_si128(&row[c]);
                        var src = loadu_si128(&selected[c]);
                        storeu_si128(&row[c], fmadd_pd(src, hf, dst));
                        c += 2;
                    }

                    if (c < width)
                        row[c] = cvtsd_f64(fmadd_sd(set_sd(selected[c]), hf, set_sd(row[c])));

                    row[pc] = 0.0;
                }
            }
            else
            {
                for (int r = 0; r < height; ++r)
                {
                    if (r == pr)
                        continue;

                    double* row = &matrix[r * width];
                    if (row[pc] == 0.0)
                        continue;

                    double f = -row[pc];
                    for (int c = pc + 1; c < width; ++c)
                        row[c] += selected[c] * f;

                    row[pc] = 0.0;
                }
            }

            pr += 1;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void SwapRows(
        [NoAlias] double* src,
        [NoAlias] double* dst,
        [AssumeRange(0, int.MaxValue)] int length
    )
    {
        for (int i = 0; i < length; ++i)
            (src[i], dst[i]) = (dst[i], src[i]);
    }

    private static unsafe void ComputeRowBounds(
        [NoAlias] double* row,
        [AssumeRange(0, int.MaxValue)] int length,
        out bool positive,
        out bool negative
    )
    {
        positive = true;
        negative = true;

        if (IsAvx2Supported)
        {
            var vpos = mm256_set1_epi64x(-1);
            var vneg = mm256_set1_epi64x(-1);
            var vzero = mm256_setzero_pd();

            int x = 0;
            for (; x + 4 <= length; x += 4)
            {
                var current = mm256_loadu_pd(&row[x]);
                vpos = mm256_and_pd(vpos, mm256_cmp_pd(current, vzero, (int)CMP.GE_OS));
                vneg = mm256_and_pd(vneg, mm256_cmp_pd(current, vzero, (int)CMP.LE_OS));
            }

            positive &= mm256_movemask_pd(vpos) == 0xF;
            negative &= mm256_movemask_pd(vneg) == 0xF;

            if (x + 2 <= length)
            {
                var current = loadu_si128(&row[x]);
                var ipos = movemask_pd(cmp_pd(current, setzero_si128(), (int)CMP.GE_OS));
                var ineg = movemask_pd(cmp_pd(current, setzero_si128(), (int)CMP.LE_OS));

                positive &= ipos == 0x3;
                negative &= ineg == 0x3;

                x += 2;
            }

            if (x < length)
            {
                positive &= row[x] >= 0.0;
                negative &= row[x] <= 0.0;
            }
        }
        else
        {
            for (int x = 0; x < length; ++x)
            {
                positive &= row[x] >= 0.0;
                negative &= row[x] <= 0.0;
            }
        }
    }

    public static ConstraintState[] InferStates(Collections.Matrix matrix, int equalities)
    {
        if (equalities < 0 || equalities > matrix.Height)
            throw new ArgumentOutOfRangeException(nameof(equalities));

        var states = new ConstraintState[matrix.Height];

        unsafe
        {
            fixed (double* _matrix = matrix.Values)
            {
                fixed (ConstraintState* _states = states)
                {
                    InferStates(_matrix, matrix.Width, matrix.Height, _states, equalities);
                }
            }
        }

        return states;
    }

    [BurstCompile]
    private static unsafe void InferStates(
        [NoAlias] double* matrix,
        [AssumeRange(0, int.MaxValue)] int width,
        [AssumeRange(0, int.MaxValue)] int height,
        [NoAlias] ConstraintState* states,
        [AssumeRange(0, int.MaxValue)] int equalities
    )
    {
        int y = 0;
        for (; y < height; ++y)
        {
            double* row = &matrix[y * width];
            double constant = row[width - 1];

            ComputeRowBounds(row, width - 1, out var positive, out var negative);
            bool zero = positive && negative;

            if (zero)
            {
                if (y < equalities)
                {
                    if (constant == 0.0)
                        states[y] = ConstraintState.VACUOUS;
                    else
                        states[y] = ConstraintState.UNSOLVABLE;
                }
                else
                {
                    if (constant >= 0.0)
                        states[y] = ConstraintState.VACUOUS;
                    else
                        states[y] = ConstraintState.UNSOLVABLE;
                }
            }
            else if (negative)
            {
                if (constant >= 0.0)
                    states[y] = ConstraintState.VACUOUS;
                else
                    states[y] = ConstraintState.VALID;
            }
            else if (positive)
            {
                if (constant < 0.0)
                    states[y] = ConstraintState.UNSOLVABLE;
                else
                    states[y] = ConstraintState.VALID;
            }
            else
            {
                states[y] = ConstraintState.VALID;
            }
        }
    }

    private static unsafe void TraceInferZeros(double* matrix, int width, int height)
    {
        Matrix tableau = new(matrix, (uint)width, (uint)height);
        LogUtil.Log($"Presolve matrix after InferZeros:\n{tableau}");
    }

    private static unsafe void TraceInitialMatrix(double* matrix, int width, int height)
    {
        Matrix tableau = new(matrix, (uint)width, (uint)height);
        LogUtil.Log($"Initial presolve matrix:\n{tableau}");
    }

    private static unsafe void TraceMatrix(double* matrix, int width, int height)
    {
        Matrix tableau = new(matrix, (uint)width, (uint)height);
        LogUtil.Log($"Presolve matrix after RowReduce:\n{tableau}");
    }

    [IgnoreWarning(1370)]
    private static void ThrowStopIndexOutOfRange() =>
        throw new ArgumentOutOfRangeException("stop index was larger than the matrix height");

    enum UnsolvableState
    {
        SOLVABLE,
        UNSOLVABLE,
    }

    private static bool SetUnsolvable(ref UnsolvableState unsolvable)
    {
        [BurstDiscard]
        static void Managed() => throw new UnsolvableProblemException();

        Managed();
        unsolvable = UnsolvableState.UNSOLVABLE;
        return false;
    }
}
