using System;
using System.Runtime.CompilerServices;
using BackgroundResourceProcessing.Collections;
using BackgroundResourceProcessing.Tracing;

namespace BackgroundResourceProcessing.Solver;

internal static class Simplex
{
    const double Epsilon = 1e-9;
    const uint MaxIterations = 1000;

    private static bool Trace
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get { return DebugSettings.Instance?.SolverTrace ?? false; }
    }

    internal static void SolveTableau(Matrix tableau, BitSet selected)
    {
        using var span = new TraceSpan("Simplex.SolveTableau");

        unsafe
        {
            fixed (double* data = tableau.Values)
            {
                SolveTableau(tableau, data, tableau.Width, tableau.Height, selected);
            }
        }
    }

    private static unsafe void SolveTableau(
        Matrix matrix,
        double* tableau,
        int width,
        int height,
        BitSet selected
    )
    {
        for (uint iter = 0; iter < MaxIterations; ++iter)
        {
            int pivot = SelectPivot(tableau, width, height);
            if (pivot < 0)
                break;

            int index = SelectRow(tableau, width, height, pivot);
            if (index < 0)
                break;

            if (Trace)
                TracePivot(pivot, index, matrix);

            selected[pivot] = true;

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

        if (Trace)
            TraceFinal(matrix);
    }

    private static unsafe int SelectPivot(double* tableau, int width, int height)
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

    private static unsafe int SelectRow(double* tableau, int width, int height, int pivot)
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

            if (Trace)
                TraceSelectRow(y, num, den, ratio);

            if (ratio < value)
            {
                index = y;
                value = ratio;
            }
        }

        return index;
    }

    private static unsafe void InvScaleRow(double* row, double length, double scale)
    {
        if (scale == 1.0)
            return;

        for (int i = 0; i < length; ++i)
            row[i] /= scale;
    }

    private static unsafe void ScaleReduce(double* src, double* dst, double scale, int length)
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

    static void TraceSelectRow(double y, double num, double den, double ratio)
    {
        if (Trace)
            LogUtil.Log($"Considering row {y} {num:g4}/{den:g4} = {ratio:g4}");
    }

    static void TracePivot(int col, int row, Matrix tableau)
    {
        if (Trace)
            LogUtil.Log($"Pivoting on column {col}, row {row}:\n{tableau}");
    }

    static void TraceFinal(Matrix tableau)
    {
        if (Trace)
            LogUtil.Log($"Final:\n{tableau}");
    }
}
