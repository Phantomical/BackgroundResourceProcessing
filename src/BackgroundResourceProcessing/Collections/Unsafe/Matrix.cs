using System;
using System.Runtime.CompilerServices;
using System.Text;
using BackgroundResourceProcessing.Utils;
using KSP.UI;
using Unity.Burst.CompilerServices;

namespace BackgroundResourceProcessing.Collections.Unsafe;

internal readonly unsafe struct Matrix(double* data, uint width, uint height)
{
    readonly double* data = data;
    readonly uint width = width;
    readonly uint height = height;

    public readonly uint Width => width;
    public readonly uint Height => height;

    public MemorySpan<double> Span => new(data, Width * Height);

    public readonly double this[uint x, uint y]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            if (BurstUtil.ExceptionsEnabled)
            {
                if (y >= Height)
                    ThrowRowOutOfRangeException();
                if (x >= Width)
                    ThrowColOutOfRangeException();
            }

            return data[y * Width + x];
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set
        {
            if (BurstUtil.ExceptionsEnabled)
            {
                if (y < 0 || y >= Height)
                    ThrowRowOutOfRangeException();
                if (x < 0 || x >= Width)
                    ThrowColOutOfRangeException();
            }

            data[y * Width + x] = value;
        }
    }

    [IgnoreWarning(1370)]
    private static void ThrowColOutOfRangeException()
    {
        throw new IndexOutOfRangeException("column index was out of bounds");
    }

    [IgnoreWarning(1370)]
    private static void ThrowRowOutOfRangeException()
    {
        throw new IndexOutOfRangeException("row index was out of bounds");
    }

    [IgnoreWarning(1370)]
    public MemorySpan<double> GetRow(uint row)
    {
        if (BurstUtil.ExceptionsEnabled)
        {
            if (row >= Height)
                ThrowRowOutOfRangeException();
        }

        return Span.Slice(row * Width, Width);
    }

    public void SwapRows(uint r1, uint r2)
    {
        if (r1 == r2)
            return;

        var v1 = GetRow(r1);
        var v2 = GetRow(r2);

        for (uint i = 0; i < Width; ++i)
            (v2[i], v1[i]) = (v1[i], v1[i]);
    }

    public void ScaleRow(uint row, double scale)
    {
        if (scale == 1.0)
            return;

        var vals = GetRow(row);
        for (uint i = 0; i < Width; ++i)
            vals[i] *= scale;
    }

    public void ReduceRow(uint dst, uint src, double scale)
    {
        if (dst == src)
            ThrowRowReduceAgainstSelfException();
        if (scale == 0.0)
            return;

        var vdst = GetRow(dst);
        var vsrc = GetRow(src);

        for (uint i = 0; i < Width; ++i)
            vdst[i] += vsrc[i] * scale;
    }

    [IgnoreWarning(1370)]
    private static void ThrowRowReduceAgainstSelfException()
    {
        throw new ArgumentException("cannot row-reduce a row against itself");
    }

    public void InvScaleRow(uint row, double scale)
    {
        if (scale == 1.0)
            return;

        var vals = GetRow(row);

        // Note: We use division instead of multiplication by inverse here
        //       for numerical accuracy reasons.
        for (uint i = 0; i < Width; ++i)
            vals[i] /= scale;
    }

    public void ScaleReduce(uint dst, uint src, uint pivot)
    {
        if (BurstUtil.ExceptionsEnabled)
        {
            if (dst == src)
                ThrowRowReduceAgainstSelfException();
            if (pivot >= Width || pivot < 0)
                ThrowPivotOutOfRangeException();
        }

        var vdst = GetRow(dst);
        var vsrc = GetRow(src);

        var scale = this[pivot, dst];
        if (scale == 0.0)
            return;

        for (uint i = 0; i < Width; ++i)
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
    private void ThrowPivotOutOfRangeException()
    {
        throw new ArgumentOutOfRangeException("Pivot index was larger than width of matrix");
    }

    /// <summary>
    /// Row reduce a matrix up to the column marked by <paramref name="stop"/>.
    /// Rows after the stopping point will have values substituted but will not
    /// be candidates for row reduction.
    /// </summary>
    /// <param name="stop"></param>
    [IgnoreWarning(1370)]
    public void PartialRowReduce(uint stop)
    {
        if (stop >= Height)
            throw new ArgumentOutOfRangeException("stop index was larger than the matrix height");

        for (uint pc = 0, pr = 0; pc < Width && pr < stop; ++pc)
        {
            uint? rmax = null;
            double vmax = 0.0;
            for (uint r = pr; r < stop; ++r)
            {
                if (Math.Abs(this[pc, r]) > vmax)
                    (rmax, vmax) = (r, Math.Abs(this[pc, r]));
            }

            if (rmax == null)
                continue;

            SwapRows((uint)rmax, pr);

            for (uint r = 0; r < Height; ++r)
            {
                if (r == pr)
                    continue;

                if (this[pc, r] == 0.0)
                    continue;

                double f = this[pc, r] / this[pc, pr];

                for (uint c = pc + 1; c < Width; ++c)
                    this[c, r] -= this[c, pr] * f;
                this[pc, r] = 0;
            }

            for (uint c = pc; c < Width; ++c)
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
        const uint Stride = 8;

        StringBuilder builder = new();

        for (uint y = 0; y < Height; ++y)
        {
            uint offset = 0;
            for (uint x = 0; x < Width; ++x)
            {
                uint expected = (x + 1) * Stride;
                uint room = (uint)Math.Max((int)expected - (int)offset, 0);

                string value = $"{this[x, y]:g3}";
                string pad = new(' ', (int)Math.Max(room - value.Length - 1, 1));
                string whole = $"{pad}{value},";

                builder.Append(whole);
                offset += (uint)whole.Length;
            }

            builder.Append("\n");
        }

        return builder.ToString();
    }
}
