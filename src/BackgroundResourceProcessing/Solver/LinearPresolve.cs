using System;
using BackgroundResourceProcessing.Collections;
using BackgroundResourceProcessing.Tracing;
using Steamworks;

namespace BackgroundResourceProcessing.Solver;

internal static class LinearPresolve
{
    public static bool Presolve(Matrix equations, BitSet zeros, int equalities, int inequalities)
    {
        using var span = new TraceSpan("LinearPresolve.Presolve");

        if (equalities < 0)
            throw new ArgumentOutOfRangeException(nameof(equalities));
        if (inequalities < 0)
            throw new ArgumentOutOfRangeException(nameof(inequalities));

        if (!InferZeros(equations, zeros, equalities, inequalities) && equalities == 0)
            return false;

        do
        {
            PartialRowReduce(equations, equalities);
        } while (InferZeros(equations, zeros, equalities, inequalities));

        return true;
    }

    private static bool InferZeros(Matrix equations, BitSet zeros, int equalities, int inequalities)
    {
        if (equalities < 0)
            throw new ArgumentOutOfRangeException(nameof(equalities));
        if (inequalities < 0)
            throw new ArgumentOutOfRangeException(nameof(inequalities));

        unsafe
        {
            fixed (double* matrix = equations.Values)
            {
                return InferZeros(
                    matrix,
                    equations.Width,
                    equations.Height,
                    zeros,
                    equalities,
                    inequalities,
                    equations
                );
            }
        }
    }

    private static unsafe bool InferZeros(
        double* matrix,
        int width,
        int height,
        BitSet zeros,
        int equalities,
        int inequalities,
        Matrix tableau
    )
    {
        int ineqStop = equalities + inequalities;
        var found = false;

        for (int y = 0; y < ineqStop; ++y)
        {
            double* row = &matrix[y * width];
            double constant = row[width - 1];
            if (double.IsNaN(constant))
                throw new Exception("LP constraint had a NaN constant");

            bool positive = true;
            bool negative = true;
            for (int x = 0; x < width - 1; ++x)
            {
                positive &= row[x] >= 0.0;
                negative &= row[x] <= 0.0;
            }

            bool zero = positive && negative;

            if (y < equalities)
            {
                if (zero)
                {
                    if (constant != 0.0)
                        throw new UnsolvableProblemException();
                    continue;
                }
                else if (negative)
                {
                    if (constant > 0.0)
                        throw new UnsolvableProblemException();
                    if (constant < 0.0)
                        continue;
                }
                else if (positive)
                {
                    if (constant < 0.0)
                        throw new UnsolvableProblemException();
                    if (constant > 0.0)
                        continue;
                }
                else
                {
                    continue;
                }
            }
            else
            {
                if (zero)
                {
                    if (constant < 0.0)
                        throw new UnsolvableProblemException();
                    continue;
                }
                else if (positive)
                {
                    if (constant < 0.0)
                        throw new UnsolvableProblemException();
                    if (constant > 0.0)
                        continue;
                }
                else
                {
                    continue;
                }
            }

            for (int x = 0; x < width - 1; ++x)
            {
                if (row[x] == 0.0)
                    continue;

                zeros[x] = true;
                found = true;

                for (int i = 0; i < height; ++i)
                    matrix[width * i + x] = 0.0;
            }
        }

        return found;
    }

    private static void PartialRowReduce(Matrix matrix, int stop)
    {
        unsafe
        {
            fixed (double* data = matrix.Values)
            {
                PartialRowReduce(data, matrix.Width, matrix.Height, stop);
            }
        }
    }

    private static unsafe void PartialRowReduce(double* matrix, int width, int height, int stop)
    {
        if (stop >= height)
            throw new ArgumentOutOfRangeException("stop index was larger than the matrix height");

        for (int pc = 0, pr = 0; pc < width && pr < stop; ++pc)
        {
            int rmax = -1;
            double vmax = 0.0;

            for (int r = pr; r < stop; ++r)
            {
                var elem = matrix[r * width + pc];
                var abs = elem < 0.0 ? -elem : elem;

                if (abs > vmax)
                {
                    rmax = r;
                    vmax = abs;
                }
            }

            if (rmax == -1)
                continue;

            if (rmax != pr)
            {
                double* src = &matrix[rmax * width];
                double* dst = &matrix[pr * width];

                for (int i = 0; i < width; ++i)
                    (src[i], dst[i]) = (dst[i], src[i]);
            }

            double* selected = &matrix[pr * width];
            for (int r = 0; r < height; ++r)
            {
                if (r == pr)
                    continue;

                double* row = &matrix[r * width];
                if (row[pc] == 0.0)
                    continue;

                double f = row[pc] / selected[pc];
                for (int c = pc + 1; c < width; ++c)
                    row[c] -= selected[c] * f;
                row[pc] = 0.0;
            }

            double coef = selected[pc];
            for (int c = pc; c < width; ++c)
                selected[c] /= coef;

            pr += 1;
        }
    }
}
