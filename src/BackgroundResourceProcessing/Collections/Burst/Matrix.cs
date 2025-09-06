using System;
using System.Runtime.CompilerServices;
using System.Text;
using BackgroundResourceProcessing.Utils;
using Unity.Burst.CompilerServices;
using Unity.Collections;

namespace BackgroundResourceProcessing.Collections.Burst;

internal struct Matrix : IDisposable
{
    RawArray<double> values;
    uint rows;
    uint cols;

    public readonly int Rows
    {
        [return: AssumeRange(0, int.MaxValue)]
        get
        {
            AssumeSize();
            return (int)rows;
        }
    }
    public readonly int Cols
    {
        [return: AssumeRange(0, int.MaxValue)]
        get
        {
            AssumeSize();
            return (int)cols;
        }
    }
    public readonly Allocator Allocator => values.Allocator;
    public readonly Span<double> Span => values.Span.Slice(0, Rows * Cols);

    public readonly Span<double> this[int r]
    {
        [IgnoreWarning(1310)]
        get
        {
            AssumeSize();
            if (r < 0 || r >= Rows)
                throw new IndexOutOfRangeException("row index was out of range");

            return Span.Slice(r * Cols, Cols);
        }
    }

    public readonly ref double this[int r, int c]
    {
        [IgnoreWarning(1310)]
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

        this.values = values;
        this.rows = (uint)rows;
        this.cols = (uint)cols;
    }

    public Matrix(int rows, int cols, Allocator allocator)
        : this(new RawArray<double>(rows * cols, allocator), rows, cols) { }

    public void SwapRows(int r1, int r2)
    {
        if (r1 == r2)
            return;

        var v1 = this[r1];
        var v2 = this[r2];

        for (int i = 0; i < Cols; ++i)
            (v2[i], v1[i]) = (v1[i], v2[i]);
    }

    public void ScaleRow(int r, double scale)
    {
        if (scale == 1.0)
            return;

        var row = this[r];
        foreach (ref var value in row)
            value *= scale;
    }

    [IgnoreWarning(1310)]
    public void Reduce(int dst, int src, double scale)
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

    public void InvScaleRow(int row, double scale)
    {
        if (scale == 1.0)
            return;

        // Note: We use division instead of multiplication by inverse here
        //       for numerical accuracy reasons.
        foreach (ref var val in this[row])
            val /= scale;
    }

    [IgnoreWarning(1310)]
    public void ScaleReduce(int dst, int src, int pivot)
    {
        if (dst == src)
            throw new ArgumentException("cannot row-reduce a row against itself");
        if (pivot < 0 || pivot >= Cols)
            throw new ArgumentOutOfRangeException("pivot index was outside of matrix");

        var vdst = this[dst];
        var vsrc = this[src];
        var scale = vdst[pivot];
        if (scale == 0.0)
            return;

        for (int i = 0; i < vdst.Length; ++i)
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

    public void Dispose()
    {
        values.Dispose();
        rows = 0;
        cols = 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private readonly void AssumeSize() => Hint.Assume(rows * cols < values.Length);

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

    [IgnoreWarning(1310)]
    static void ThrowNegativeRowsException() => throw new ArgumentOutOfRangeException("rows");

    [IgnoreWarning(1310)]
    static void ThrowNegativeColsException() => throw new ArgumentOutOfRangeException("cols");

    [IgnoreWarning(1310)]
    static void ThrowArrayTooSmallException() =>
        throw new ArgumentException("provided array is too small for matrix size");
}
