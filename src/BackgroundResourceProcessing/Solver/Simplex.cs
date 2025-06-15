using System;
using System.Collections.Generic;
using System.Diagnostics;
using BackgroundResourceProcessing.Collections;

namespace BackgroundResourceProcessing.Solver
{
    internal class Simplex2
    {
        // Keep a low iteration limit so that even if we run into a degenerate
        // case then we don't cause too much stutter.
        const int MaxIterations = 1000;

        public static void SolveTableau(Matrix tableau)
        {
            for (int iter = 0; iter < MaxIterations; ++iter)
            {
                int pivot = SelectPivot(tableau);
                if (pivot < 0)
                    break;

                if (!Pivot(tableau, pivot))
                    break;
            }

            LogUtil.Log($"Final:\n{tableau}");
        }

        private static int SelectPivot(Matrix tableau)
        {
            int pivot = -1;
            var value = double.PositiveInfinity;

            for (int i = 0; i < tableau.Width - 1; ++i)
            {
                var current = tableau[i, 0];
                if (current >= 0.0)
                    continue;

                if (current < value)
                {
                    pivot = i;
                    value = current;
                }
            }

            return pivot;
        }

        private static bool Pivot(Matrix tableau, int pivot)
        {
            int index = -1;
            var value = double.PositiveInfinity;

            for (int i = 1; i < tableau.Height; ++i)
            {
                var denom = tableau[pivot, i];
                var numer = tableau[tableau.Width - 1, i];
                var quotient = numer / denom;

                // LogUtil.Log($"Checking row {i}: {numer:G3}/{denom:G3} = {quotient:G3}");

                if (denom <= 0.0)
                    continue;
                if (quotient < 0.0)
                    continue;
                if (quotient >= value)
                    continue;

                index = i;
                value = quotient;
            }

            if (index < 0)
                return false;
            Debug.Assert(value != double.PositiveInfinity);

            LogUtil.Log($"Pivoting on column {pivot}, row {index}:\n{tableau}");

            tableau.InvScaleRow(index, tableau[pivot, index]);
            for (int i = 0; i < tableau.Height; ++i)
            {
                if (i == index)
                    continue;

                tableau.ScaleReduce(i, index, pivot);
                // tableau.Reduce(i, index, -tableau[pivot, i]);
            }

            return true;
        }
    }

    internal static class Simplex3
    {
        const double Epsilon = 1e-9;
        const uint MaxIterations = 1000;

        internal static void SolveTableau(Matrix tableau)
        {
            for (uint iter = 0; iter < MaxIterations; ++iter)
            {
                int col = SelectPivot(tableau);
                if (col < 0)
                    break;

                int row = SelectRow(tableau, col);
                if (row < 0)
                    throw new UnsolvableProblemException("LP problem has unbounded solutions");

                LogUtil.Log($"Pivoting on column {col}, row {row}:\n{tableau}");

                tableau.InvScaleRow(row, tableau[col, row]);
                for (int y = 0; y < tableau.Height; ++y)
                {
                    if (y == row)
                        continue;

                    tableau.ScaleReduce(y, row, col);
                }
            }

            LogUtil.Log($"Final:\n{tableau}");
        }

        static int SelectPivot(Matrix tableau)
        {
            int pivot = -1;
            double value = double.PositiveInfinity;

            // Here we use the steepest-edge criterion in order to select the
            // pivot column.

            for (int x = 0; x < tableau.Width - 1; ++x)
            {
                var current = tableau[x, 0];
                if (current >= -Epsilon)
                    continue;

                var norm = 0.0;
                for (int y = 1; y < tableau.Height; ++y)
                {
                    var v = tableau[x, y];
                    norm += v * v;
                }

                if (norm < Epsilon)
                    throw new UnsolvableProblemException();

                var steepness = current / Math.Sqrt(norm);
                if (steepness < value)
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

                // LogUtil.Log($"Inspecting row {y} {num:g4}/{den:g4} = {ratio:g4}");

                // We ignore all negative ratios
                if (ratio < 0.0)
                    continue;

                // Ignore zero ratios if the denominator is negative
                if (ratio == 0.0 && den < 0.0)
                    continue;

                if (ratio < value)
                {
                    index = y;
                    value = ratio;
                }
            }

            return index;
        }
    }
}
