using System;
using BackgroundResourceProcessing.Collections.Unsafe;

namespace BackgroundResourceProcessing.Solver.Burst;

internal static partial class Simplex
{
    internal static class Base
    {
        internal static unsafe void SolveTableau(
            double* tableau,
            int width,
            int height,
            BitSpan selected
        )
        {
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

        internal static unsafe int SelectPivot(double* tableau, int width)
        {
            int pivot = -1;
            double value = 0.0;

            for (int x = 0; x < width - 1; ++x)
            {
                var current = tableau[x];
                if (current < value)
                {
                    pivot = x;
                    value = current;
                }
            }

            return pivot;
        }

        internal static unsafe int SelectRow(double* tableau, int width, int height, int pivot)
        {
            int index = -1;
            double value = double.PositiveInfinity;

            for (int y = 1; y < height; ++y)
            {
                double* row = &tableau[y * width];
                double den = row[pivot];
                double num = row[width - 1];
                double ratio = num / den;

                if (den <= 0.0)
                    continue;

                // We ignore all negative ratios
                if (ratio < 0.0)
                    continue;

                if (ratio < value)
                {
                    index = y;
                    value = ratio;
                }
            }

            return index;
        }

        internal static unsafe void InvScaleRow(double* row, double length, double scale)
        {
            if (scale == 1.0)
                return;

            for (int i = 0; i < length; ++i)
                row[i] /= scale;
        }

        internal static unsafe void ScaleReduce(double* src, double* dst, double scale, int length)
        {
            if (scale == 0.0)
                return;

            for (int i = 0; i < length; ++i)
            {
                var d = dst[i];
                var s = src[i] * scale;
                var r = d - s;

                dst[i] = r;

                // Some hacks to attempt to truncate numerical errors down to
                // 0 without breaking the simplex algorithm when working with
                // big-M constants.
                if (Math.Abs(r) >= 1e-9)
                    continue;

                // If d and s almost perfectly cancel out then just truncate to 0.
                if (Math.Abs(r) / (Math.Abs(d) + Math.Abs(s)) < 1e-9)
                    dst[i] = 0.0;
            }
        }
    }
}
