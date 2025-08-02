using System;
using System.Runtime.CompilerServices;
using System.Text;
using Unity.Burst.CompilerServices;

namespace BackgroundResourceProcessing.Collections.Unsafe;

internal readonly unsafe struct Matrix
{
    readonly double* data;
    readonly int width;
    readonly int height;

    public readonly int Width => width;
    public readonly int Height => height;

    public MemorySpan<double> Span => new(data, Width * Height);

    public readonly double this[int x, int y]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [IgnoreWarning(1370)]
        get
        {
            if (y < 0 || y >= Height)
                throw new IndexOutOfRangeException("row index was out of bounds");
            if (x < 0 || x >= Width)
                throw new IndexOutOfRangeException("column index was out of bounds");

            return data[y * Width + x];
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [IgnoreWarning(1370)]
        set
        {
            if (y < 0 || y >= Height)
                throw new IndexOutOfRangeException("row index was out of bounds");
            if (x < 0 || x >= Width)
                throw new IndexOutOfRangeException("column index was out of bounds");

            data[y * Width + x] = value;
        }
    }

    public unsafe Matrix(double* data, int width, int height)
    {
        if (width < 0)
            ThrowWidthOutOfRangeException();
        if (height < 0)
            ThrowHeightOutOfRangeException();

        this.data = data;
        this.width = width;
        this.height = height;
    }

    [IgnoreWarning(1370)]
    private static void ThrowWidthOutOfRangeException()
    {
        throw new ArgumentOutOfRangeException("width");
    }

    [IgnoreWarning(1370)]
    private static void ThrowHeightOutOfRangeException()
    {
        throw new ArgumentOutOfRangeException("height");
    }

    [IgnoreWarning(1370)]
    public MemorySpan<double> GetRow(int row)
    {
        if (row < 0 || row >= Height)
            throw new IndexOutOfRangeException("row index was out of bounds");

        return Span.Slice(row * Width, Width);
    }

    public void SwapRows(int r1, int r2)
    {
        if (r1 == r2)
            return;

        var v1 = GetRow(r1);
        var v2 = GetRow(r2);

        for (int i = 0; i < Width; ++i)
            (v2[i], v1[i]) = (v1[i], v1[i]);
    }

    public void ScaleRow(int row, double scale)
    {
        if (scale == 1.0)
            return;

        var vals = GetRow(row);
        for (int i = 0; i < Width; ++i)
            vals[i] *= scale;
    }

    [IgnoreWarning(1370)]
    public void ReduceRow(int dst, int src, double scale)
    {
        if (dst == src)
            throw new ArgumentException("cannot row-reduce a row against itself");
        if (scale == 0.0)
            return;

        var vdst = GetRow(dst);
        var vsrc = GetRow(src);

        for (int i = 0; i < Width; ++i)
            vdst[i] += vsrc[i] * scale;
    }

    public void InvScaleRow(int row, double scale)
    {
        if (scale == 1.0)
            return;

        var vals = GetRow(row);

        // Note: We use division instead of multiplication by inverse here
        //       for numerical accuracy reasons.
        for (int i = 0; i < Width; ++i)
            vals[i] /= scale;
    }

    [IgnoreWarning(1370)]
    public void ScaleReduce(int dst, int src, int pivot)
    {
        if (dst == src)
            throw new ArgumentException("cannot row-reduce a row against itself");
        if (pivot >= Width || pivot < 0)
            throw new ArgumentOutOfRangeException("Pivot index was larger than width of matrix");

        var vdst = GetRow(dst);
        var vsrc = GetRow(src);

        var scale = this[pivot, dst];
        if (scale == 0.0)
            return;

        for (int i = 0; i < Width; ++i)
        {
            var d = vdst[i];
            var s = vsrc[i] * scale;
            var r = d - s;

            vdst[i] = r;

            // Some hacks to attempt to truncate numerical errors down to
            // 0 without breaking the simplex algorithm when working with
            // big-M constants.
            if (Math.Abs(r) >= 1e-9)
                continue;

            // If d and s almost perfectly cancel out then just truncate to 0.
            if (Math.Abs(r) / (Math.Abs(d) + Math.Abs(s)) < 1e-9)
                vdst[i] = 0.0;
        }
    }

    [IgnoreWarning(1370)]
    public Matrix Slice(int start, int length)
    {
        if (start < 0 || start + length > Height)
            throw new IndexOutOfRangeException("slice index was out of range");

        return new(data + start * Width, Width, length);
    }

    /// <summary>
    /// Row reduce a matrix up to the column marked by <paramref name="stop"/>.
    /// Rows after the stopping point will have values substituted but will not
    /// be candidates for row reduction.
    /// </summary>
    /// <param name="stop"></param>
    [IgnoreWarning(1370)]
    public void PartialRowReduce(int stop)
    {
        if (stop >= Height)
            throw new ArgumentOutOfRangeException("stop index was larger than the matrix height");

        for (int pc = 0, pr = 0; pc < Width && pr < stop; ++pc)
        {
            int rmax = -1;
            double vmax = 0.0;
            for (int r = pr; r < stop; ++r)
            {
                if (Math.Abs(this[pc, r]) > vmax)
                    (rmax, vmax) = (r, Math.Abs(this[pc, r]));
            }

            if (rmax == -1)
                continue;

            SwapRows(rmax, pr);

            for (int r = 0; r < Height; ++r)
            {
                if (r == pr)
                    continue;

                if (this[pc, r] == 0.0)
                    continue;

                double f = this[pc, r] / this[pc, pr];

                for (int c = pc + 1; c < Width; ++c)
                    this[c, r] -= this[c, pr] * f;
                this[pc, r] = 0;
            }

            for (int c = pc; c < Width; ++c)
                this[c, pr] /= this[pc, pr];

            pr += 1;
        }
    }

    public void RowReduce()
    {
        PartialRowReduce(Height);
    }

    public override string ToString()
    {
        const int Stride = 8;

        StringBuilder builder = new();

        for (int y = 0; y < Height; ++y)
        {
            int offset = 0;
            for (int x = 0; x < Width; ++x)
            {
                int expected = (x + 1) * Stride;
                int room = Math.Max(expected - offset, 0);

                string value = $"{this[x, y]:g3}";
                string pad = new(' ', Math.Max(room - value.Length - 1, 1));
                string whole = $"{pad}{value},";

                builder.Append(whole);
                offset += whole.Length;
            }

            builder.Append("\n");
        }

        return builder.ToString();
    }
}
