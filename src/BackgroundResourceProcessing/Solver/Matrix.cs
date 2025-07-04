using System;
using System.Text;

namespace BackgroundResourceProcessing.Solver;

/// <summary>
/// A matrix. The available helpers are mostly meant for performing
/// row-reduction.
/// </summary>
internal class Matrix
{
    public readonly int Width;
    public readonly int Height;
    private readonly double[] values;

    // Internal accessor for debugging purposes
    private double[][] Values
    {
        get
        {
            double[][] output = new double[Height][];
            for (int y = 0; y < Height; ++y)
            {
                output[y] = new double[Width];
                for (int x = 0; x < Width; ++x)
                {
                    output[y][x] = this[x, y];
                }
            }

            return output;
        }
    }

    public Matrix(int width, int height)
    {
        if (width < 0)
            throw new ArgumentException("width was less than 0");
        if (height < 0)
            throw new ArgumentException("height was less than 0");

        Width = width;
        Height = height;
        values = new double[width * height];
    }

    public double this[int x, int y]
    {
        get
        {
            if (x < 0 || y < 0)
                throw new IndexOutOfRangeException($"index ({x}, {y}) was out of bounds");

            return this[(uint)x, (uint)y];
        }
        set
        {
            if (x < 0 || y < 0)
                throw new IndexOutOfRangeException($"index ({x}, {y}) was out of bounds");

            this[(uint)x, (uint)y] = value;
        }
    }

    public double this[uint x, uint y]
    {
        get
        {
            if (x >= Width || y >= Height)
                throw new IndexOutOfRangeException($"index ({x}, {y}) was out of bounds");

            return values[y * Width + x];
        }
        set
        {
            if (x >= Width || y >= Height)
                throw new IndexOutOfRangeException($"index ({x}, {y}) was out of bounds");

            values[y * Width + x] = value;
        }
    }

    public Span<double> GetRow(int y)
    {
        if (y < 0 || y > Height)
            throw new IndexOutOfRangeException($"row index {y} was out of bounds");

        Span<double> data = values;
        return data.Slice(y * Width, Width);
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

    public void Reduce(int dst, int src, double scale)
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

    public void ScaleReduce(int dst, int src, int pivot)
    {
        if (dst == src)
            throw new ArgumentException("cannot row-reduce a row against itself");
        if (pivot >= Width || pivot < 0)
            throw new ArgumentOutOfRangeException(
                "pivot",
                "Pivot index was larger than width of matrix"
            );

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
