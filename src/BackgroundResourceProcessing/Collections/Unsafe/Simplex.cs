using System.Runtime.CompilerServices;
using BackgroundResourceProcessing.Solver;
using BackgroundResourceProcessing.Tracing;
using Unity.Burst;

namespace BackgroundResourceProcessing.Collections.Unsafe;

[BurstCompile]
internal static class Simplex
{
    const double Epsilon = 1e-9;
    const int MaxIterations = 1000;

    private static bool Trace
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            bool trace = false;
            Method(ref trace);
            return trace;

            [BurstDiscard]
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            static void Method(ref bool trace)
            {
                trace = DebugSettings.Instance?.SolverTrace ?? false;
            }
        }
    }

    internal static void SolveTableau(Solver.Matrix tableau, BitSet selected)
    {
        using var iterSpan = new TraceSpan("Unsafe.Simplex.SolveTableau");

        unsafe
        {
            fixed (double* tvals = tableau.Values)
            {
                fixed (ulong* bits = selected.Bits)
                {
                    var solved = SolveTableauBurst(
                        tvals,
                        tableau.Width,
                        tableau.Height,
                        bits,
                        selected.Bits.Length
                    );

                    if (!solved)
                        throw new UnsolvableProblemException("LP problem has unbounded solutions");
                }
            }
        }
    }

    [BurstCompile]
    private static unsafe bool SolveTableauBurst(
        [NoAlias] double* tableau,
        int width,
        int height,
        [NoAlias] ulong* bits,
        int length
    )
    {
        return SolveTableau(new Matrix(tableau, width, height), new BitSpan(new(bits, length)));
    }

    internal static bool SolveTableau(Matrix tableau, BitSpan selected)
    {
        for (int iter = 0; iter < MaxIterations; ++iter)
        {
            int col = SelectPivot(tableau);
            if (col < 0)
                break;

            int row = SelectRow(tableau, col);
            if (row < 0)
                return false;

            TracePivot(col, row, tableau);

            selected[col] = true;
            tableau.InvScaleRow(row, tableau[col, row]);

            for (int y = 0; y < tableau.Height; ++y)
            {
                if (y == row)
                    continue;

                tableau.ScaleReduce(y, row, col);
            }
        }

        TraceFinal(tableau);

        return true;
    }

    static int SelectPivot(Matrix tableau)
    {
        int pivot = -1;
        double value = double.PositiveInfinity;

        // Here we just pick the most-negative value
        for (int x = 0; x < tableau.Width - 1; ++x)
        {
            var current = tableau[x, 0];
            if (current >= -Epsilon)
                continue;

            if (current < value)
            {
                pivot = x;
                value = current;
            }
        }

        return pivot;
    }

    static int SelectRow(Matrix tableau, int pivot)
    {
        int index = -1;
        var value = double.PositiveInfinity;

        for (int y = 1; y < tableau.Height; ++y)
        {
            var den = tableau[pivot, y];
            var num = tableau[tableau.Width - 1, y];
            var ratio = num / den;

            if (den <= 0)
                continue;

            // We ignore all negative ratios
            if (ratio < 0.0)
                continue;

            TraceSelectRow(y, num, den, ratio);

            if (ratio < value)
            {
                index = y;
                value = ratio;
            }
        }

        return index;
    }

    [BurstDiscard]
    static void TraceSelectRow(double y, double num, double den, double ratio)
    {
        if (Trace)
            LogUtil.Log($"Considering row {y} {num:g4}/{den:g4} = {ratio:g4}");
    }

    [BurstDiscard]
    static void TracePivot(int col, int row, Matrix tableau)
    {
        if (Trace)
            LogUtil.Log($"Pivoting on column {col}, row {row}:\n{tableau}");
    }

    [BurstDiscard]
    static void TraceFinal(Matrix tableau)
    {
        if (Trace)
            LogUtil.Log($"Final:\n{tableau}");
    }
}
