using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using BackgroundResourceProcessing.Solver;
using BackgroundResourceProcessing.Tracing;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using static Unity.Burst.Intrinsics.X86;

namespace BackgroundResourceProcessing.Collections.Unsafe;

[BurstCompile]
internal static class Simplex
{
    const double Epsilon = 1e-9;
    const uint MaxIterations = 1000;

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
                        (uint)tableau.Width,
                        (uint)tableau.Height,
                        bits,
                        (uint)selected.Bits.Length
                    );

                    if (!solved)
                        throw new UnsolvableProblemException("LP problem has unbounded solutions");
                }
            }
        }
    }

    [BurstCompile]
    private static unsafe bool SolveTableauBurst(
        [NoAlias] double* _tableau,
        uint _width,
        uint _height,
        [NoAlias] ulong* _bits,
        uint _length
    )
    {
        Matrix tableau = new(_tableau, _width, _height);
        BitSpan selected = new(_bits, _length);

        return SolveTableau(tableau, selected);
    }

    internal static bool SolveTableau(Matrix tableau, BitSpan selected)
    {
        if (tableau.Width <= 1 || tableau.Height <= 1)
            return false;

        if (selected.Capacity < tableau.Width - 1)
            throw new ArgumentException("selected bitset capacity too small for tableau");

        for (uint iter = 0; iter < MaxIterations; ++iter)
        {
            uint? _col = SelectPivot(tableau);
            if (_col == null)
                break;
            uint col = _col.Value;

            uint? _row = SelectRow(tableau, col);
            if (_row == null)
                return false;
            uint row = _row.Value;

            if (Trace)
                TracePivot(col, row, tableau);

            selected[col] = true;
            tableau.InvScaleRow(row, tableau[col, row]);

            for (uint y = 0; y < tableau.Height; ++y)
            {
                if (y == row)
                    continue;

                tableau.ScaleReduce(y, row, col);
            }
        }

        if (Trace)
            TraceFinal(tableau);

        return true;
    }

    static uint? SelectPivot(Matrix tableau)
    {
        uint? pivot = null;
        double value = double.PositiveInfinity;

        // Here we just pick the most-negative value
        for (uint x = 0; x < tableau.Width - 1; ++x)
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

    static uint? SelectRow(Matrix tableau, uint pivot)
    {
        uint? index = null;
        var value = double.PositiveInfinity;

        for (uint y = 1; y < tableau.Height; ++y)
        {
            var den = tableau[pivot, y];
            var num = tableau[tableau.Width - 1, y];
            var ratio = num / den;

            if (den <= 0)
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

    [BurstDiscard]
    static void TraceSelectRow(double y, double num, double den, double ratio)
    {
        if (Trace)
            LogUtil.Log($"Considering row {y} {num:g4}/{den:g4} = {ratio:g4}");
    }

    [BurstDiscard]
    static void TracePivot(uint col, uint row, Matrix tableau)
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
