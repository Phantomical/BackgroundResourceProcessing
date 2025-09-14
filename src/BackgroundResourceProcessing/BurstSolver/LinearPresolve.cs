using System;
using System.Runtime.CompilerServices;
using BackgroundResourceProcessing.Collections.Burst;
using BackgroundResourceProcessing.Utils;
using Unity.Burst;
using Unity.Burst.CompilerServices;
using Unity.Burst.Intrinsics;
using static Unity.Burst.Intrinsics.X86.Avx;
using static Unity.Burst.Intrinsics.X86.Avx2;
using static Unity.Burst.Intrinsics.X86.Fma;
using static Unity.Burst.Intrinsics.X86.Sse2;

namespace BackgroundResourceProcessing.BurstSolver;

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

    [MustUseReturnValue]
    internal static Result<bool> Presolve(
        Matrix matrix,
        BitSpan zeros,
        int equalities,
        int inequalities
    )
    {
        if (Trace)
            TraceInitialMatrix(matrix);

        if (!InferZeros(matrix, zeros, equalities, inequalities).Match(out var found, out var err))
            return err;
        if (!found && equalities == 0)
            return false;

        do
        {
            if (Trace)
                TraceInferZeros(matrix);

            PartialRowReduce(matrix, equalities);

            if (Trace)
                TraceMatrix(matrix);

            if (!InferZeros(matrix, zeros, equalities, inequalities).Match(out found, out err))
                return err;
        } while (found);

        return true;
    }

    [MustUseReturnValue]
    private static unsafe Result<bool> InferZeros(
        in Matrix matrix,
        BitSpan zeros,
        [AssumeRange(0, int.MaxValue)] int equalities,
        [AssumeRange(0, int.MaxValue)] int inequalities
    )
    {
        int ineqStop = equalities + inequalities;
        var found = false;

        for (int y = 0; y < ineqStop; ++y)
        {
            double* row = matrix.GetRowPtr(y);
            double constant = row[matrix.Cols - 1];

            ComputeRowBounds(row, matrix.Cols - 1, out var positive, out var negative);

            bool zero = positive && negative;

            if (y < equalities)
            {
                if (zero)
                {
                    if (constant != 0.0)
                        return BurstError.Unsolvable();
                    continue;
                }
                else if (negative)
                {
                    if (constant > 0.0)
                        return BurstError.Unsolvable();
                    if (constant < 0.0)
                        continue;
                }
                else if (positive)
                {
                    if (constant < 0.0)
                        return BurstError.Unsolvable();
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
                        return BurstError.Unsolvable();
                    continue;
                }
                else if (positive)
                {
                    if (constant < 0.0)
                        return BurstError.Unsolvable();
                    if (constant > 0.0)
                        continue;
                }
                else
                {
                    continue;
                }
            }

            for (int x = 0; x < matrix.Cols - 1; ++x)
            {
                if (row[x] == 0.0)
                    continue;

                zeros[x] = true;
                found = true;

                for (int i = 0; i < matrix.Rows; ++i)
                    matrix[i, x] = 0.0;
            }
        }

        return found;
    }

    private static unsafe void PartialRowReduce(
        in Matrix tableau,
        [AssumeRange(0, int.MaxValue)] int stop
    )
    {
        if (stop >= tableau.Rows)
            ThrowStopIndexOutOfRange(stop);

        double** avx2_row = stackalloc double*[4];
        double* avx2_f = stackalloc double[4];
        var avx2_vf = stackalloc v256[4];

        for (int pc = 0, pr = 0; pc < tableau.Cols && pr < stop; ++pc)
        {
            int rmax = -1;
            double vmax = 0.0;

            for (int r = pr; r < stop; ++r)
            {
                var elem = tableau[r, pc];
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
                tableau.SwapRows(rmax, pr);

            var selected = tableau.GetRowPtr(pr);
            double coef = selected[pc];
            for (int c = pc; c < tableau.Cols; ++c)
                selected[c] /= coef;

            if (IsAvx2Supported)
            {
                int r = 0;
                for (; r + 4 <= tableau.Rows; r += 4)
                {
                    double** row = avx2_row;
                    double* f = avx2_f;
                    var vf = avx2_vf;

                    for (int i = 0; i < 4; ++i)
                    {
                        row[i] = tableau.GetRowPtr(r + i);
                        f[i] = -row[i][pc];

                        if (r + i == pr)
                            f[i] = 0.0;
                        vf[i] = mm256_set1_pd(f[i]);
                    }

                    if (f[0] == 0.0 && f[1] == 0.0 && f[2] == 0.0 && f[3] == 0.0)
                        continue;

                    int c = pc + 1;
                    for (; c + 4 <= tableau.Cols; c += 4)
                    {
                        var src = mm256_loadu_pd(&selected[c]);

                        for (int i = 0; i < 4; ++i)
                        {
                            var dst = mm256_loadu_pd(&row[i][c]);
                            mm256_storeu_pd(&row[i][c], mm256_fmadd_pd(src, vf[i], dst));
                        }
                    }

                    if (c + 2 <= tableau.Cols)
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

                    if (c < tableau.Cols)
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

                for (; r < tableau.Rows; ++r)
                {
                    if (r == pr)
                        continue;

                    double* row = tableau.GetRowPtr(r);
                    if (row[pc] == 0.0)
                        continue;

                    double f = -row[pc];
                    var vf = mm256_set1_pd(f);
                    var hf = set1_pd(f);
                    int c = pc + 1;

                    for (; c + 4 <= tableau.Cols; c += 4)
                    {
                        var dst = mm256_loadu_pd(&row[c]);
                        var src = mm256_loadu_pd(&selected[c]);
                        mm256_storeu_pd(&row[c], mm256_fmadd_pd(src, vf, dst));
                    }

                    if (c + 2 <= tableau.Cols)
                    {
                        var dst = loadu_si128(&row[c]);
                        var src = loadu_si128(&selected[c]);
                        storeu_si128(&row[c], fmadd_pd(src, hf, dst));
                        c += 2;
                    }

                    if (c < tableau.Cols)
                        row[c] = cvtsd_f64(fmadd_sd(set_sd(selected[c]), hf, set_sd(row[c])));

                    row[pc] = 0.0;
                }
            }
            else
            {
                for (int r = 0; r < tableau.Rows; ++r)
                {
                    if (r == pr)
                        continue;

                    double* row = tableau.GetRowPtr(r);
                    if (row[pc] == 0.0)
                        continue;

                    double f = -row[pc];
                    for (int c = pc + 1; c < tableau.Cols; ++c)
                        row[c] += selected[c] * f;

                    row[pc] = 0.0;
                }
            }

            pr += 1;
        }
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

    internal static unsafe void InferStates(
        in Matrix matrix,
        MemorySpan<ConstraintState> states,
        [AssumeRange(0, int.MaxValue)] int equalities
    )
    {
        if (states.Length != matrix.Rows)
            BurstCrashHandler.Crash(Error.LinearPresolve_StatesSpanMismatch);

        int y = 0;
        for (; y < matrix.Rows; ++y)
        {
            double* row = matrix.GetRowPtr(y);
            double constant = row[matrix.Cols - 1];
            Relation relation = y < equalities ? Relation.Equal : Relation.LEqual;

            states[y] = InferState(new(row, matrix.Cols - 1), constant, relation);
        }
    }

    internal static unsafe ConstraintState InferState(
        MemorySpan<double> coefs,
        double constant,
        Relation relation
    )
    {
        ComputeRowBounds(coefs.Data, coefs.Length, out var positive, out var negative);
        bool zero = positive && negative;

        if (relation == Relation.GEqual)
            (positive, negative) = (negative, positive);

        if (zero)
        {
            if (relation == Relation.Equal)
            {
                if (constant == 0.0)
                    return ConstraintState.VACUOUS;
                else
                    return ConstraintState.UNSOLVABLE;
            }
            else
            {
                if (constant >= 0.0)
                    return ConstraintState.VACUOUS;
                else
                    return ConstraintState.UNSOLVABLE;
            }
        }
        else if (negative)
        {
            if (relation == Relation.Equal)
            {
                if (constant > 0.0)
                    return ConstraintState.UNSOLVABLE;
                else
                    return ConstraintState.VALID;
            }
            else
            {
                if (constant >= 0.0)
                    return ConstraintState.VACUOUS;
                else
                    return ConstraintState.VALID;
            }
        }
        else if (positive)
        {
            if (constant < 0.0)
                return ConstraintState.UNSOLVABLE;
            else
                return ConstraintState.VALID;
        }
        else
        {
            return ConstraintState.VALID;
        }
    }

    [BurstDiscard]
    private static unsafe void TraceInferZeros(in Matrix tableau)
    {
        LogUtil.Log($"Presolve matrix after InferZeros:\n{tableau}");
    }

    [BurstDiscard]
    private static unsafe void TraceInitialMatrix(in Matrix tableau)
    {
        LogUtil.Log($"Initial presolve matrix:\n{tableau}");
    }

    [BurstDiscard]
    private static unsafe void TraceMatrix(in Matrix tableau)
    {
        LogUtil.Log($"Presolve matrix after RowReduce:\n{tableau}");
    }

    private static void ThrowStopIndexOutOfRange(int stop) =>
        BurstCrashHandler.Crash(Error.LinearPresolve_StopIndexOutOfRange, stop);

    internal enum UnsolvableState
    {
        SOLVABLE,
        UNSOLVABLE,
    }
}
