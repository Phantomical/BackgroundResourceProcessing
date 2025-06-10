using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
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

        public void InvScaleRow(int row, double scale)
        {
            if (scale == 1.0)
                return;

            // Note: We use division instead of multiplication by inverse here
            //       for numerical accuracy reasons.
            for (int i = 0; i < width; ++i)
                this[i, row] /= scale;
        }

        public void ScaleReduce(int dst, int src, int pivot)
        {
            if (dst == src)
                throw new ArgumentException("cannot row-reduce a row against itself");
            if (pivot >= Width || pivot < 0)
                throw new ArgumentOutOfRangeException(
                    "pivot",
                    "Pivot index was larger than width of matrix"
                );

            var scale = this[pivot, dst];
            if (scale == 0.0)
                return;

            for (int i = 0; i < Width; ++i)
            {
                this[i, dst] -= this[i, src] * scale;

                // This is a bit of a hack. Numerical imprecision can result in
                // certain values failing to cancel out when they really should.
                // By clamping values to 0 when they are too small, we can avoid
                // that problem at the expense of potentially truncating some
                // columns.
                //
                // However, problems in KSP tend to be pretty well-behaved
                // numerically, so this is preferable to numerical errors
                // causing unexpected non-optimal solutions.
                if (Math.Abs(this[i, dst]) < 1e-9)
                    this[i, dst] = 0.0;
            }
        }

        public override string ToString()
        {
            const int Stride = 8;

            StringBuilder builder = new();

            for (int y = 0; y < Height; ++y)
            {
                int offset = 0;
                for (int x = 0; x < Width; ++x)
                {
                    int expected = (x + 1) * Stride;
                    int room = Math.Max(expected - offset, 0);

                    string value = $"{this[x, y]:g3}";
                    string pad = new(' ', Math.Max(room - value.Length - 1, 1));
                    string whole = $"{pad}{value},";

                    builder.Append(whole);
                    offset += whole.Length;
                }

                builder.Append("\n");
            }

            return builder.ToString();
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
            // LogUtil.Log($"Attempting to solve problem:\n{this}");

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

        public override string ToString()
        {
            StringBuilder builder = new();

            builder.Append("maximize Z = ");

            {
                bool first = true;
                for (int i = 0; i < func.Length; ++i)
                {
                    if (!RenderCoef(builder, func[i], $"x{i}", ref first))
                        continue;
                }

                if (first)
                    builder.Append("0.0");
            }

            builder.Append("\nsubject to\n");
            foreach (var constraint in constraints)
            {
                bool first = true;
                for (int i = 0; i < constraint.coefs.Length; ++i)
                    if (!RenderCoef(builder, constraint.coefs[i], $"x{i}", ref first))
                        continue;

                // if (constraint.slack != -1)
                //     RenderCoef(builder, constraint.scoef, $"S{constraint.slack}", ref first);

                if (first)
                    builder.Append("0.0");

                if (constraint.slack == -1)
                    builder.Append(" == ");
                else if (constraint.scoef < 0.0)
                    builder.Append(" >= ");
                else
                    builder.Append(" <= ");

                builder.Append($"{constraint.value:G}\n");
            }

            {
                bool first = true;
                for (int i = 0; i < nvars; ++i)
                {
                    if (first)
                        first = false;
                    else
                        builder.Append(",");
                    builder.Append($"x{i}");
                }

                // for (int i = 0; i < slackCount; ++i)
                // {
                //     if (first)
                //         first = false;
                //     else
                //         builder.Append(",");
                //     builder.Append($"S{i}");
                // }

                if (!first)
                    builder.Append(" >= 0");
            }

            return builder.ToString();
        }

        private bool RenderCoef(StringBuilder builder, double coef, string var, ref bool first)
        {
            if (coef == 0.0)
                return false;

            if (Math.Abs(coef) != 1.0)
            {
                if (first)
                {
                    builder.Append($"{coef:G3}");
                    first = false;
                }
                else if (coef < 0)
                    builder.Append($" - {-coef:G3}");
                else
                    builder.Append($" + {coef:G3}");

                builder.Append("*");
                builder.Append(var);
            }
            else
            {
                if (first)
                {
                    if (coef < 0.0)
                        builder.Append('-');
                    first = false;
                }
                else if (coef < 0.0)
                    builder.Append(" - ");
                else
                    builder.Append(" + ");

                builder.Append(var);
            }

            return true;
        }
    }

    internal class OverconstrainedLinearProblemException(string message) : Exception(message) { }

    internal class UnconstrainedVariableException(string message) : Exception(message) { }
}
