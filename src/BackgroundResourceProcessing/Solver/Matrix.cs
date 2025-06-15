using System;
using System.Text;

namespace BackgroundResourceProcessing.Solver
{
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

        public void SwapRows(int r1, int r2)
        {
            if (r1 == r2)
                return;

            for (int i = 0; i < Width; ++i)
                (this[i, r2], this[i, r1]) = (this[i, r1], this[i, r2]);
        }

        public void ScaleRow(int row, double scale)
        {
            if (scale == 1.0)
                return;

            for (int i = 0; i < Width; ++i)
                this[i, row] *= scale;
        }

        public void Reduce(int dst, int src, double scale)
        {
            if (dst == src)
                throw new ArgumentException("cannot row-reduce a row against itself");
            if (scale == 0.0)
                return;

            for (int i = 0; i < Width; ++i)
                this[i, dst] += this[i, src] * scale;
        }

        public void InvScaleRow(int row, double scale)
        {
            if (scale == 1.0)
                return;

            // Note: We use division instead of multiplication by inverse here
            //       for numerical accuracy reasons.
            for (int i = 0; i < Width; ++i)
                this[i, row] /= scale;
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

            var scale = this[pivot, dst];
            if (scale == 0.0)
                return;

            for (int i = 0; i < Width; ++i)
            {
                this[i, dst] -= this[i, src] * scale;

                // This is a bit of a hack. Numerical imprecision can result in
                // certain values failing to cancel out when they really should.
                // By clamping values to 0 when they are too small, we can avoid
                // that problem at the expense of potentially truncating some
                // columns.
                //
                // However, problems in KSP tend to be pretty well-behaved
                // numerically, so this is preferable to numerical errors
                // causing unexpected non-optimal solutions.
                if (Math.Abs(this[i, dst]) < 1e-6)
                    this[i, dst] = 0.0;
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
}
