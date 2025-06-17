using System;
using System.Diagnostics;

namespace BackgroundResourceProcessing.Solver
{
    internal static class Simplex
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

                Trace(() => $"Pivoting on column {col}, row {row}:\n{tableau}");

                tableau.InvScaleRow(row, tableau[col, row]);
                for (int y = 0; y < tableau.Height; ++y)
                {
                    if (y == row)
                        continue;

                    tableau.ScaleReduce(y, row, col);
                }
            }

            Trace(() => $"Final:\n{tableau}");
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

                // Trace(() => $"Inspecting row {y} {num:g4}/{den:g4} = {ratio:g4}");

                if (den <= 0)
                    continue;

                // We ignore all negative ratios
                if (ratio < 0.0)
                    continue;

                // // Ignore zero ratios if the denominator is negative
                // if (ratio == 0.0 && den < 0.0)
                //     continue;

                Trace(() => $"Considering row {y} {num:g4}/{den:g4} = {ratio:g4}");

                if (ratio < value)
                {
                    index = y;
                    value = ratio;
                }
            }

            return index;
        }

        [Conditional("SOLVERTRACE")]
        private static void Trace(Func<string> func)
        {
#if SOLVERTRACE
            LogUtil.Log(func());
#endif
        }
    }
}
