using System;
using BackgroundResourceProcessing.Solver;
using BackgroundResourceProcessing.Tracing;
using Unity.Burst;

namespace BackgroundResourceProcessing.Collections.Unsafe;

[BurstCompile]
internal static class LinearPresolve
{
    enum Result
    {
        SUCCESS,
        UNSOLVABLE,
        NAN,
    }

    public static void Presolve(
        Solver.Matrix equations,
        Collections.BitSet zeros,
        int equalities,
        int inequalities
    )
    {
        using var span = new TraceSpan("LinearPresolve.Presolve");

        if (equalities < 0)
            throw new ArgumentOutOfRangeException(nameof(equalities));
        if (inequalities < 0)
            throw new ArgumentOutOfRangeException(nameof(inequalities));

        unsafe
        {
            fixed (double* eqdata = equations.Values)
            {
                fixed (ulong* zdata = zeros.Bits)
                {
                    var result = LinearPresolveBurst(
                        eqdata,
                        (uint)equations.Width,
                        (uint)equations.Height,
                        zdata,
                        (uint)zeros.Bits.Length,
                        (uint)equalities,
                        (uint)inequalities
                    );

                    switch (result)
                    {
                        case Result.SUCCESS:
                            break;
                        case Result.UNSOLVABLE:
                            throw new UnsolvableProblemException();
                        case Result.NAN:
                            throw new Exception("LP constraint had a NaN constant");
                    }
                }
            }
        }
    }

    [BurstCompile]
    private static unsafe Result LinearPresolveBurst(
        [NoAlias] double* equations,
        uint width,
        uint height,
        [NoAlias] ulong* zeros,
        uint length,
        uint equalities,
        uint inequalities
    )
    {
        return Presolve(
            new Matrix(equations, width, height),
            new BitSpan(zeros, length),
            equalities,
            inequalities
        );
    }

    private static Result Presolve(
        Matrix equations,
        BitSpan zeros,
        uint equalities,
        uint inequalities
    )
    {
        var result = Result.SUCCESS;

        InferZeros(equations, zeros, equalities, inequalities, ref result);

        do
        {
            if (result != Result.SUCCESS)
                return result;

            equations.PartialRowReduce(equalities);
        } while (InferZeros(equations, zeros, equalities, inequalities, ref result));

        return result;
    }

    private static bool InferZeros(
        Matrix equations,
        BitSpan zeros,
        uint equalities,
        uint inequalities,
        ref Result result
    )
    {
        var ineqStop = equalities + inequalities;
        var found = false;

        for (uint y = 0; y < ineqStop; ++y)
        {
            var constant = equations[equations.Width - 1, y];
            if (double.IsNaN(constant))
            {
                result = Result.NAN;
                return false;
            }

            bool positive = true;
            bool negative = true;

            // We can only infer zeros if all the coefficients are positive.
            for (uint x = 0; x < equations.Width - 1; ++x)
            {
                positive &= equations[x, y] >= 0.0;
                negative &= equations[x, y] <= 0.0;
            }

            bool allzero = negative && positive;

            if (allzero)
            {
                if ((y < equalities && constant != 0.0) || constant < 0.0)
                {
                    result = Result.UNSOLVABLE;
                    return false;
                }
            }
            else if (negative)
            {
                if (constant < 0.0)
                    continue;
                if (constant > 0.0)
                {
                    result = Result.UNSOLVABLE;
                    return false;
                }
            }
            else if (positive)
            {
                if (constant > 0.0)
                    continue;
                if (constant < 0.0)
                {
                    result = Result.UNSOLVABLE;
                    return false;
                }
            }
            else
            {
                continue;
            }

            for (uint x = 0; x < equations.Width - 1; ++x)
            {
                if (equations[x, y] == 0.0)
                    continue;

                zeros[x] = true;
                found = true;

                for (uint i = 0; i < equations.Height; ++i)
                    equations[x, i] = 0.0;
            }
        }

        return found;
    }
}
