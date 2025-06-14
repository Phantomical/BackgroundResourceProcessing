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

                // LogUtil.Log($"Pivoting on column {pivot}:\n{tableau}");

                if (!Pivot(tableau, pivot))
                    break;
            }

            // LogUtil.Log($"Final:\n{tableau}");
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

            // LogUtil.Log($"Selecting row {index}");

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
}
