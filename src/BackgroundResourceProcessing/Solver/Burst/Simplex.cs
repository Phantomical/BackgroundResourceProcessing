using System;
using BackgroundResourceProcessing.Collections.Unsafe;
using BackgroundResourceProcessing.Tracing;
using BackgroundResourceProcessing.Utils;
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

    public static void SolveTableau(Collections.Matrix tableau, Collections.BitSet selected)
    {
        using var span = new TraceSpan("Burst.Simplex.SolveTableau");

        unsafe
        {
            fixed (double* _tableau = tableau.Values)
            {
                fixed (ulong* _selected = selected.Bits)
                {
                    SolveTableau(
                        _tableau,
                        tableau.Width,
                        tableau.Height,
                        _selected,
                        selected.Bits.Length
                    );
                }
            }
        }
    }

    [BurstCompile]
    private static unsafe void SolveTableau(
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
            int pivot = SelectPivot(tableau, width);
            if (pivot < 0)
                break;

            int index = SelectRow(tableau, width, height, pivot);
            if (index < 0)
                break;

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
    }

    private static unsafe int SelectPivot([NoAlias] double* row, int length)
    {
        if (IsAvx2Supported)
        {
            v256 offsets = mm256_set_epi64x(0, 1, 2, 3);
            v256 minv = mm256_set1_pd(0.0);
            v256 mini = mm256_set1_epi64x(-1);

            uint i = 0;
            for (; i + 4 <= length; i += 4)
            {
                v256 current = mm256_loadu_pd(&row[i]);
                v256 indices = mm256_add_epi64(mm256_set1_epi64x(i), offsets);

                v256 mask = mm256_cmp_pd(current, minv, (int)CMP.LT_OS);

                minv = mm256_min_pd(minv, current);
                mini = mm256_select_si256(mask, indices, mini);
            }

            if (i < length)
            {
                v256 indices = mm256_add_epi64(mm256_set1_epi64x(i), offsets);
                v256 cmask = mm256_cmpge_epi64(indices, mm256_set1_epi64x(length));
                v256 current = mm256_maskload_pd(&row[i], cmask);

                v256 mask = mm256_cmp_pd(current, minv, (int)CMP.LT_OS);
                minv = mm256_min_pd(minv, current);

                indices = mm256_and_si256(mask, indices);
                mini = mm256_andnot_si256(mask, mini);
                mini = mm256_or_si256(mini, indices);
            }

            {
                v128 lov = mm256_extractf128_pd(minv, 0);
                v128 hiv = mm256_extractf128_pd(minv, 1);
                v128 loi = mm256_extracti128_si256(mini, 0);
                v128 hii = mm256_extracti128_si256(mini, 1);

                v128 lmask = cmp_pd(lov, hiv, (int)CMP.LT_OS);
                v128 values = min_pd(lov, hiv);
                v128 indices = or_si128(and_si128(lmask, hii), andnot_si128(lmask, loi));

                if (values.Double0 < values.Double1)
                    return (int)extract_epi64(indices, 0);
                else
                    return (int)extract_epi64(indices, 1);
            }
        }

        if (IsSse2Supported)
        {
            v128 offsets = set_epi64x(0, 1);
            v128 minv = set1_pd(0.0);
            v128 mini = set1_epi64x(-1);

            int i = 0;
            for (; i + 2 <= length; i += 2)
            {
                v128 current = loadu_si128(&row[i]);
                v128 indices = add_epi64(set1_epi64x(i), offsets);
                v128 mask = cmplt_pd(current, minv);

                minv = min_pd(minv, current);
                indices = select_si128(mask, indices, mini);
            }

            var valueLo = minv.Double0;
            var valueHi = minv.Double1;

            int pivot;
            double value;

            if (valueLo < valueHi)
                (pivot, value) = ((int)mini.SLong0, valueLo);
            else
                (pivot, value) = ((int)mini.SLong1, valueHi);

            if (i < length && row[i] < value)
                pivot = i;

            return pivot;
        }

        return Base.SelectPivot(row, length);
    }

    private static unsafe int SelectRow(double* tableau, int width, int height, int pivot) =>
        Base.SelectRow(tableau, width, height, pivot);

    private static unsafe void InvScaleRow([NoAlias] double* row, int length, double scale)
    {
        if (scale == 1.0)
            return;

        int i = 0;
        if (IsAvx2Supported)
        {
            v256 vscale = mm256_set1_pd(scale);
            for (; i + 4 <= length; i += 4)
                mm256_storeu_pd(&row[i], mm256_div_pd(mm256_loadu_pd(&row[i]), vscale));
        }

        if (IsSse2Supported)
        {
            v128 vscale = set1_pd(scale);
            for (; i + 2 <= length; i += 2)
                storeu_si128(&row[i], div_pd(loadu_si128(&row[i]), vscale));
        }

        for (; i < length; ++i)
            row[i] /= scale;
    }

    private static unsafe void ScaleReduce(
        [NoAlias] double* src,
        [NoAlias] double* dst,
        double scale,
        int length
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
                v256 d = mm256_loadu_pd(&dst[i]);
                v256 s = mm256_mul_pd(mm256_loadu_pd(&src[i]), vscale);
                v256 r = mm256_sub_pd(d, s);

                // This _should_ be a floating-point abs
                v256 absr = mm256_abs_pd(r);
                v256 rel = mm256_div_pd(absr, mm256_add_pd(mm256_abs_pd(d), mm256_abs_pd(s)));

                v256 mask1 = mm256_cmp_pd(absr, epsilon, (int)CMP.GE_OS);
                v256 mask2 = mm256_cmp_pd(rel, epsilon, (int)CMP.GE_OS);
                v256 mask = mm256_or_pd(mask1, mask2);

                r = mm256_and_pd(r, mask);
                mm256_storeu_pd(&dst[i], r);
            }
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
        }

        Base.ScaleReduce(src + i, dst + i, scale, length - i);
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
}
