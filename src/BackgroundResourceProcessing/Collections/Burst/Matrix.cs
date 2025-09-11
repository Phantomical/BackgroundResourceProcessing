using System;
using System.Text;
using BackgroundResourceProcessing.Utils;
using Unity.Burst.CompilerServices;
using Unity.Burst.Intrinsics;
using Unity.Mathematics;
using static Unity.Burst.Intrinsics.X86.Avx;
using static Unity.Burst.Intrinsics.X86.Avx2;

namespace BackgroundResourceProcessing.Collections.Burst;

internal readonly unsafe struct Matrix
{
    readonly double* values;
    readonly uint rows;
    readonly uint cols;

    public readonly int Rows
    {
        [return: AssumeRange(0, int.MaxValue)]
        get => (int)rows;
    }
    public readonly int Cols
    {
        [return: AssumeRange(0, int.MaxValue)]
        get => (int)cols;
    }
    public readonly MemorySpan<double> Span => new(values, Rows * Cols);
    public readonly unsafe double* Ptr => values;

    public readonly MemorySpan<double> this[int r]
    {
        [IgnoreWarning(1370)]
        get
        {
            if (r < 0 || r >= Rows)
                throw new IndexOutOfRangeException("row index was out of range");

            return Span.Slice(r * Cols, Cols);
        }
    }

    public readonly ref double this[int r, int c]
    {
        [IgnoreWarning(1370)]
        get
        {
            if (r < 0 || r >= Rows)
                throw new IndexOutOfRangeException("row index was out of range");
            if (c < 0 || c >= Cols)
                throw new IndexOutOfRangeException("column index was out of range");

            return ref this[r][c];
        }
    }

    public Matrix(RawArray<double> values, int rows, int cols)
    {
        if (rows < 0)
            ThrowNegativeRowsException();
        if (cols < 0)
            ThrowNegativeColsException();
        if (values.Length < rows * cols)
            ThrowArrayTooSmallException();

        this.values = values.Ptr;
        this.rows = (uint)rows;
        this.cols = (uint)cols;
    }

    public Matrix(int rows, int cols, AllocatorHandle allocator)
        : this(new RawArray<double>(rows * cols, allocator), rows, cols) { }

    [IgnoreWarning(1370)]
    public readonly unsafe double* GetRowPtr(int r)
    {
        if (r < 0 || r >= Rows)
            throw new IndexOutOfRangeException("row index was out of range");

        return &Ptr[r * Cols];
    }

    public readonly void SwapRows(int r1, int r2)
    {
        if (r1 == r2)
            return;

        var v1 = this[r1];
        var v2 = this[r2];

        for (int i = 0; i < Cols; ++i)
            (v2[i], v1[i]) = (v1[i], v2[i]);
    }

    public readonly void ScaleRow(int r, double scale)
    {
        if (scale == 1.0)
            return;

        var row = this[r];
        foreach (ref var value in row)
            value *= scale;
    }

    [IgnoreWarning(1370)]
    public readonly void Reduce(int dst, int src, double scale)
    {
        if (dst == src)
            throw new ArgumentException("cannot row-reduce a row against itself");
        if (scale == 0.0)
            return;

        var vdst = this[dst];
        var vsrc = this[src];

        for (int i = 0; i < vdst.Length; ++i)
            vdst[i] = MathUtil.Fma(vsrc[i], scale, vdst[i]);
    }

    public readonly void InvScaleRow(int row, double scale)
    {
        if (scale == 1.0)
            return;

        // Note: We use division instead of multiplication by inverse here
        //       for numerical accuracy reasons.
        foreach (ref var val in this[row])
            val /= scale;
    }

    [IgnoreWarning(1370)]
    public readonly void ScaleReduce(int dst, int src, int pivot)
    {
        if (dst == src)
            throw new ArgumentException("cannot row-reduce a row against itself");
        if (pivot < 0 || pivot >= Cols)
            throw new ArgumentOutOfRangeException("pivot index was outside of matrix");

        var vdst = GetRowPtr(dst);
        var vsrc = GetRowPtr(src);
        var scale = vdst[pivot];
        if (scale == 0.0)
            return;

        int i = 0;
        if (IsAvx2Supported)
        {
            v256 vscale = mm256_set1_pd(scale);
            v256 epsilon = mm256_set1_pd(1e-9);

            for (; i + 4 <= Cols; i += 4)
            {
                var d = mm256_loadu_pd(&vdst[i]);
                var s = mm256_mul_pd(mm256_loadu_pd(&vsrc[i]), vscale);
                var r = mm256_sub_pd(d, s);

                var absd = mm256_abs_pd(d);
                var abss = mm256_abs_pd(s);
                var absr = mm256_abs_pd(r);
                var rel = mm256_div_pd(absr, mm256_add_pd(abss, absd));

                var mask1 = mm256_cmp_pd(absr, epsilon, (int)CMP.LT_OS);
                var mask2 = mm256_cmp_pd(rel, epsilon, (int)CMP.LT_OS);
                var mask = mm256_and_pd(mask1, mask2);

                mm256_storeu_pd(&vdst[i], mm256_andnot_pd(mask, r));
            }
        }

        for (; i < Cols; ++i)
        {
            var d = vdst[i];
            var s = vsrc[i] * scale;
            var r = d - s;

            var da = Math.Abs(d);
            var sa = Math.Abs(s);
            var ra = Math.Abs(r);

            // Some hacks to attempt to truncate numerical errors down to
            // 0 without breaking the simplex algorithm when working with
            // big-M constants.
            //
            // If d and s almost perfectly cancel out then just truncate to 0.
            if (ra < 1e-9 && ra / (da + sa) < 1e-9)
                r = 0.0;

            vdst[i] = r;
        }
    }

    public override readonly string ToString()
    {
        const int Stride = 8;

        StringBuilder builder = new();

        for (int y = 0; y < Rows; ++y)
        {
            int offset = 0;
            for (int x = 0; x < Cols; ++x)
            {
                int expected = (x + 1) * Stride;
                int room = Math.Max(expected - offset, 0);

                string value = $"{this[y, x]:g3}";
                string pad = new(' ', Math.Max(room - value.Length - 1, 1));
                string whole = $"{pad}{value},";

                builder.Append(whole);
                offset += whole.Length;
            }

            builder.Append("\n");
        }

        return builder.ToString();
    }

    [IgnoreWarning(1370)]
    static void ThrowNegativeRowsException() => throw new ArgumentOutOfRangeException("rows");

    [IgnoreWarning(1370)]
    static void ThrowNegativeColsException() => throw new ArgumentOutOfRangeException("cols");

    [IgnoreWarning(1370)]
    static void ThrowArrayTooSmallException() =>
        throw new ArgumentException("provided array is too small for matrix size");

    [IgnoreWarning(1370)]
    private static v256 mm256_abs_pd(v256 x)
    {
        if (IsAvx2Supported)
        {
            return mm256_andnot_pd(mm256_set1_pd(-0.0), x);
        }
        else
        {
            throw new NotSupportedException();
        }
    }
}
