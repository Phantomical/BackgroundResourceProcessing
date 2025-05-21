using System;
using System.Collections.Generic;
using System.Diagnostics;
using BackgroundResourceProcessing.Collections;

namespace BackgroundResourceProcessing.Solver.Simplex
{
    // This file contains what is effectively a textbook implementation of a
    // simplex solver. It is not very efficient and will definitely fall over
    // if it ever encounters a problem that has more than, say, 100 variables.
    //
    // Luckily, ships tend to be rather simple, especially after identical
    // converters are all folded together. This means that there are usually
    // at most ~50 converters involved in the graph. The iteration limit has
    // been set accordingly.

    internal class Matrix
    {
        private readonly int width;
        private readonly int height;
        private readonly double[] values;

        public int Width => width;
        public int Height => height;

        // Internal accessor for debugging purposes
        private double[][] Values
        {
            get
            {
                double[][] output = new double[height][];
                for (int y = 0; y < height; ++y)
                {
                    output[y] = new double[width];
                    for (int x = 0; x < width; ++x)
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

            this.width = width;
            this.height = height;
            values = new double[width * height];
        }

        public double this[int x, int y]
        {
            get
            {
                if (x >= width || x < 0 || y >= height || y < 0)
                    throw new IndexOutOfRangeException($"index ({x}, {y}) was out of bounds");

                return values[y * width + x];
            }
            set
            {
                if (x >= width || x < 0 || y >= height || y < 0)
                    throw new IndexOutOfRangeException($"index ({x}, {y}) was out of bounds");

                values[y * width + x] = value;
            }
        }

        public void SwapRows(int r1, int r2)
        {
            if (r1 == r2)
                return;

            for (int i = 0; i < width; ++i)
                (this[i, r2], this[i, r1]) = (this[i, r1], this[i, r2]);
        }

        public void ScaleRow(int row, double scale)
        {
            if (scale == 1.0)
                return;

            for (int i = 0; i < width; ++i)
                this[i, row] *= scale;
        }

        public void Reduce(int dst, int src, double scale)
        {
            if (dst == src)
                throw new ArgumentException("cannot row-reduce a row against itself");
            if (scale == 0.0)
                return;

            for (int i = 0; i < width; ++i)
                this[i, dst] += this[i, src] * scale;
        }
    }

    internal class LinearProblem(double[] func)
    {
        /// <summary>
        /// A constraint of the form <c>coefs * X + slack_i == value</c>
        /// </summary>
        private struct Constraint()
        {
            // The index of the slack variable for this constraint, or -1 if no
            // slack variable is added.
            public int slack = -1;
            public double scoef = 1.0;
            public double[] coefs;
            public double value;
        }

        // Keep a low iteration limit so that even if we run into a degenerate
        // case then we don't cause too much stutter.
        const int MaxIterations = 1000;

        int nvars = func.Length;
        int slackCount = 0;

        double[] func = func;
        List<Constraint> constraints = [];

        /// <summary>
        /// Add a constraint of the form <c>coefs * X &lt;= value</c>
        /// </summary>
        public void AddLEqualConstraint(double[] coefs, double value)
        {
            if (coefs.Length != nvars)
                throw new ArgumentException(
                    $"expected {nvars} coefficients but got {coefs.Length} instead"
                );

            constraints.Add(
                new()
                {
                    slack = slackCount++,
                    scoef = 1.0,
                    coefs = coefs,
                    value = value,
                }
            );
        }

        /// <summary>
        /// Add a constraint of the form <c>coefs * X &gt;= value</c>
        /// </summary>
        public void AddGEqualConstraint(double[] coefs, double value)
        {
            if (coefs.Length != nvars)
                throw new ArgumentException(
                    $"expected {nvars} coefficients but got {coefs.Length} instead"
                );

            // For >= constraints we subtract the slack variable instead of add
            constraints.Add(
                new()
                {
                    slack = slackCount++,
                    scoef = -1.0,
                    coefs = coefs,
                    value = value,
                }
            );
        }

        /// <summary>
        /// Add a constraint of the form <c>coefs * X == value</c>.
        /// </summary>
        ///
        /// <remarks>
        /// This is more efficient than adding paired &lt;= and &gt;= constraints.
        /// </remarks>
        public void AddEqualityConstraint(double[] coefs, double value)
        {
            if (coefs.Length != nvars)
                throw new ArgumentException(
                    $"expected {nvars} coefficients but got {coefs.Length} instead"
                );

            constraints.Add(new() { coefs = coefs, value = value });
        }

        public void AddVariableLEqualConstraint(int index, double value)
        {
            double[] coefs = new double[nvars];
            coefs[index] = 1.0;
            AddLEqualConstraint(coefs, value);
        }

        public double[] Solve()
        {
            var tableau = BuildSimplexTableau();
            SolveTableau(tableau);

            var values = new double[nvars];
            foreach (var (x, y) in FindBasicVariables(tableau))
                values[x] = tableau[tableau.Width - 1, y];
            return values;
        }

        private Matrix BuildSimplexTableau()
        {
            if (nvars + slackCount < constraints.Count)
                throw new OverconstrainedLinearProblemException(
                    "Support for solving over-constrained linear problems is not implemented"
                );

            var tableau = new Matrix(nvars + slackCount + 1, constraints.Count + 1);

            for (int i = 0; i < func.Length; ++i)
                tableau[i, 0] = -func[i];

            for (int i = 0; i < constraints.Count; ++i)
            {
                var constraint = constraints[i];

                for (int j = 0; j < constraint.coefs.Length; ++j)
                    tableau[j, i + 1] = constraint.coefs[j];

                tableau[tableau.Width - 1, i + 1] = constraint.value;
                if (constraint.slack >= 0)
                    tableau[nvars + constraint.slack, i + 1] = constraint.scoef;
            }

            return tableau;
        }

        private IEnumerable<KVPair<int, int>> FindBasicVariables(Matrix tableau)
        {
            for (int x = 0; x < nvars; ++x)
            {
                int pivot = -1;

                for (int y = 0; y < tableau.Height; ++y)
                {
                    var elem = tableau[x, y];
                    if (elem == 0.0)
                        continue;
                    if (elem == 1.0)
                    {
                        if (pivot != -1)
                            goto OUTER;

                        pivot = y;
                    }
                    else
                    {
                        goto OUTER;
                    }
                }

                yield return new KVPair<int, int>(x, pivot);

                OUTER:
                ;
            }
        }

        private static void SolveTableau(Matrix tableau)
        {
            for (int iter = 0; iter < MaxIterations; ++iter)
            {
                int pivot = SelectPivot(tableau);
                if (pivot < 0)
                    break;

                if (!Pivot(tableau, pivot))
                    break;
            }
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
                var quotient = tableau[tableau.Width - 1, i] / tableau[pivot, i];
                // Note: this if statement is specifically this way so that we
                //       skip the element if quotient is NaN.
                if (!(quotient < value))
                    continue;

                index = i;
                value = quotient;
            }

            if (index < 0)
                return false;
            Debug.Assert(value != double.PositiveInfinity);

            tableau.ScaleRow(index, 1.0 / tableau[pivot, index]);
            for (int i = 0; i < tableau.Height; ++i)
            {
                if (i == index)
                    continue;

                tableau.Reduce(i, index, -tableau[pivot, i]);
            }

            return true;
        }
    }

    internal class OverconstrainedLinearProblemException(string message) : Exception(message) { }

    internal class UnconstrainedVariableException(string message) : Exception(message) { }
}
