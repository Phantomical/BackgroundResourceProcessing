#if BURST_TEST_OVERRIDE_AVX2
#define BURST_TEST_OVERRIDE_SSE41
#endif

#if BURST_TEST_OVERRIDE_SSE41
#define BURST_TEST_OVERRIDE_SSSE3
#endif

#if BURST_TEST_OVERRIDE_SSSE3
#define BURST_TEST_OVERRIDE_SSE2
#endif

using System;
using System.Runtime.CompilerServices;
using BackgroundResourceProcessing.Collections.Burst;
using BackgroundResourceProcessing.Utils;
using Unity.Burst;
using Unity.Burst.CompilerServices;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using static Unity.Burst.Intrinsics.X86.Avx;
using static Unity.Burst.Intrinsics.X86.Avx2;
using static Unity.Burst.Intrinsics.X86.Fma;
using static Unity.Burst.Intrinsics.X86.Sse2;
using static Unity.Burst.Intrinsics.X86.Ssse3;

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

#if BURST_TEST_OVERRIDE_SSE41
    private static bool IsSse41Supported => true;
#endif

#if BURST_TEST_OVERRIDE_SSSE3
    private static bool IsSsse3Supported => true;
#endif

#if BURST_TEST_OVERRIDE_SSE2
    private static bool IsSse2Supported => true;
#endif

    [MustUseReturnValue]
    internal static Result<bool> Presolve(
        Matrix matrix,
        BitSpan zeros,
        ref int equalities,
        ref int inequalities,
        AllocatorHandle allocator
    )
    {
        if (Trace)
            TraceInitialMatrix(matrix);

        if (!InferZeros(matrix, zeros, equalities, inequalities).Match(out var found, out var err))
            return err;
        if (!found && equalities == 0)
            return InferConstants(in matrix, ref equalities, ref inequalities, allocator);

        do
        {
            if (Trace)
                TraceInferZeros(matrix);

            PartialRowReduce(in matrix, equalities);

            if (Trace)
                TraceMatrix(in matrix);

            if (!InferZeros(matrix, zeros, equalities, inequalities).Match(out found, out err))
                return err;
        } while (found);

        InferConstants(in matrix, ref equalities, ref inequalities, allocator);
        if (Trace)
            TraceFinal(in matrix);
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

    private static unsafe bool InferConstants(
        in Matrix matrix,
        ref int equalities,
        ref int inequalities,
        AllocatorHandle allocator
    )
    {
        Hint.Assume(equalities >= 0);
        Hint.Assume(inequalities >= 0);

        var indices = new RawArray<int>(
            MathUtil.RoundUp(matrix.Cols - 1, sizeof(v256) / sizeof(uint)),
            allocator,
            NativeArrayOptions.UninitializedMemory
        );
        indices.Fill(int.MaxValue);

        int count = matrix.Cols - 1;
        if (count == 0)
            return false;
        var _ = indices[count - 1];

        for (int y = 0; y < matrix.Rows - 1; ++y)
        {
            double* row = matrix.GetRowPtr(y);
            double* ptr = row;
            uint x = 0;

            if (IsAvx2Supported)
            {
                var zero = mm256_setzero_pd();
                var perm = mm256_setr_epi32(0, 2, 4, 6, 0, 2, 4, 6);
                var idxs = mm256_set1_epi32(y);

                for (; x + 8 <= count; x += 8, ptr += 8)
                {
                    v256 current = mm256_load_si256(&indices.Ptr[x]);
                    v256 d0 = mm256_cmp_pd(mm256_loadu_pd(&ptr[0]), zero, (int)CMP.EQ_OQ);
                    v256 d1 = mm256_cmp_pd(mm256_loadu_pd(&ptr[4]), zero, (int)CMP.EQ_OQ);

                    var p0 = mm256_permutevar8x32_epi32(d0, perm);
                    var p1 = mm256_permutevar8x32_epi32(d1, perm);
                    var p2 = mm256_blend_epi32(p0, p1, 0xF0);

                    var isZero = p2;
                    var noValue = mm256_cmpeq_epi32(current, mm256_set1_epi32(int.MaxValue));
                    var hasValue = mm256_andnot_si256(noValue, mm256_set1_epi32(-1));

                    // We want to
                    // - set current[x] to y if nonZero && noValue
                    // - set current[x] to -1 if nonZero && !noValue
                    // - keep current[x] if !knownNonZero
                    //
                    // This works roughly as
                    // invalid = y | !noValue
                    // current[x] = select(isZero, current[x] | !noValue, current[x])

                    current = mm256_blendv_ps(mm256_or_si256(idxs, hasValue), current, isZero);
                    mm256_storeu_si256(&indices.Ptr[x], current);
                }
            }

            for (; x < count; ++x, ptr += 1)
            {
                if (*ptr == 0.0)
                    continue;

                if (indices[x] != int.MaxValue)
                    indices[x] = -1;
                else
                    indices[x] = y;
            }
        }

        bool found = false;
        int limit = equalities + inequalities;
        Hint.Assume(limit <= matrix.Rows);

        int numIndices = 0;
        double* func = matrix.GetRowPtr(matrix.Rows - 1);
        for (int i = 0; i < indices.Count; ++i)
        {
            int index = indices[i];
            if (index < equalities || index >= limit)
                continue;

            var row = matrix.GetRowPtr(index);
            if (row[i] <= 0.0 || row[matrix.Cols - 1] <= 0.0 || func[i] <= 0.0)
                continue;

            var nzcnt = CountNonZero(new(row, matrix.Cols - 1));
            if (nzcnt != 1)
                continue;

            indices[numIndices++] = index;
            func[i] = 0.0;
        }

        var compacted = indices.Span.Slice(0, numIndices);
        compacted.Sort();

        foreach (var index in compacted)
        {
            if (index < equalities || index >= limit)
                continue;

            matrix.SwapRows(index, equalities);

            equalities += 1;
            inequalities -= 1;
            found = true;
        }

        return found;
    }

    [BurstCompile]
    private static unsafe uint CountNonZeroBurst(
        double* ptr,
        [AssumeRange(0, int.MaxValue)] int length
    ) => CountNonZero(new(ptr, length));

    private static unsafe uint CountNonZero(MemorySpan<double> span)
    {
        int i = 0;
        uint count = 0;
        double* ptr = span.Data;
        double* end = ptr + span.Length;

        if (IsAvx2Supported)
        {
            v256 vcount = mm256_setzero_si256();
            v256 zero = mm256_setzero_pd();

            for (; ptr + 8 <= end; ptr += 8)
            {
                v256 v0 = mm256_cmp_pd(mm256_loadu_pd(&ptr[0]), zero, (int)CMP.NEQ_OQ);
                v256 v1 = mm256_cmp_pd(mm256_loadu_pd(&ptr[4]), zero, (int)CMP.NEQ_OQ);

                // Now v2 contains packed 32-bit counters with 0 if ptr[x] is 0 and -1 otherwise.
                v256 v2 = mm256_packs_epi32(v0, v1);

                vcount = mm256_sub_epi32(vcount, v2);
            }

            if (ptr + 4 <= end)
            {
                v256 v0 = mm256_cmp_pd(mm256_loadu_pd(&ptr[0]), zero, (int)CMP.NEQ_OQ);
                v256 v2 = mm256_packs_epi32(v0, zero);

                vcount = mm256_sub_epi32(vcount, v2);

                ptr += 4;
            }

            vcount = mm256_hadd_epi32(vcount, zero);
            vcount = mm256_permute4x64_epi64(vcount, (0 << 0) | (2 << 2) | (1 << 4) | (3 << 6));
            vcount = mm256_hadd_epi32(vcount, zero);

            ulong lcount = (ulong)mm256_extract_epi64(vcount, 0);
            count += (uint)lcount + (uint)(lcount >> 32);

            for (int j = 0; j < 4 && ptr < end; j += 1, ptr += 1)
            {
                if (*ptr != 0.0)
                    count += 1;
            }
        }
        else if (IsSse2Supported)
        {
            v128 vcount = setzero_si128();
            v128 zero = setzero_si128();

            for (; ptr + 4 <= end; ptr += 4)
            {
                v128 v0 = cmpneq_pd(loadu_si128(&ptr[0]), zero);
                v128 v1 = cmpneq_pd(loadu_si128(&ptr[2]), zero);

                // Now v2 contains packed 32-bit counters with 0 if ptr[x] is 0 and -1 otherwise.
                v128 v2 = packs_epi32(v0, v1);

                vcount = sub_epi32(vcount, v2);
            }

            if (ptr + 2 <= end)
            {
                v128 v0 = cmpneq_pd(loadu_si128(&ptr[0]), zero);
                v128 v2 = packs_epi32(v0, zero);

                vcount = sub_epi32(vcount, v2);
                ptr += 2;
            }

            if (IsSsse3Supported)
            {
                vcount = hadd_epi32(vcount, vcount);
                vcount = hadd_epi32(vcount, vcount);
                count += vcount.UInt0;
            }
            else
            {
                vcount = add_epi32(vcount, shuffle_epi32(vcount, (1 << 0) | (3 << 2)));
                ulong lcount = vcount.ULong0;
                count += (uint)lcount + (uint)(lcount >> 32);
            }

            if (ptr < end)
            {
                if (*ptr != 0.0)
                    count += 1;
            }
        }
        else
        {
            for (; i < span.Length; i += 1, ptr += 1)
            {
                if (span[i] != 0.0)
                    count += 1;
            }
        }

        return count;
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
    private static void TraceInferZeros(in Matrix tableau)
    {
        LogUtil.Log($"Presolve matrix after InferZeros:\n{tableau}");
    }

    [BurstDiscard]
    private static void TraceInitialMatrix(in Matrix tableau)
    {
        LogUtil.Log($"Initial presolve matrix:\n{tableau}");
    }

    [BurstDiscard]
    private static void TraceMatrix(in Matrix tableau)
    {
        LogUtil.Log($"Presolve matrix after RowReduce:\n{tableau}");
    }

    [BurstDiscard]
    private static void TraceFinal(in Matrix tableau)
    {
        LogUtil.Log($"Presolve matrix after InferConstants:\n{tableau}");
    }

    private static void ThrowStopIndexOutOfRange(int stop) =>
        BurstCrashHandler.Crash(Error.LinearPresolve_StopIndexOutOfRange, stop);

    internal enum UnsolvableState
    {
        SOLVABLE,
        UNSOLVABLE,
    }
}
