using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using BackgroundResourceProcessing.Collections;
using BackgroundResourceProcessing.Utils;

namespace BackgroundResourceProcessing.Solver
{
    internal class LinearProblem
    {
        const double M = 1e9;
        internal const uint BinaryStart = 0x10000000;

        // The number of linear variables that have been created.
        uint LinearCount = 0;

        // The number of binary variables that have been created.
        uint BinaryCount = 0;

        // The number of slack variables that have been created.
        uint SlackCount = 0;

        // Known substitutions within the problem.
        Dictionary<uint, LinearEquality> substitutions = [];

        // Constraints making up the problem.
        List<LinearConstraint> constraints = [];

        public LinearProblem() { }

        public Variable CreateVariable()
        {
            var index = LinearCount;
            LinearCount += 1;
            return new(index);
        }

        public VariableSet CreateVariables(uint count)
        {
            if (count >= BinaryStart - LinearCount)
                throw new ArgumentException($"Cannot create more than {BinaryCount} variables");

            var set = new VariableSet(LinearCount, count);
            LinearCount += count;
            return set;
        }

        public VariableSet CreateVariables(int count)
        {
            if (count < 0)
                throw new ArgumentException($"Cannot create a negative number of converters");

            return CreateVariables((uint)count);
        }

        private Variable CreateBinaryVariable()
        {
            var index = BinaryCount + BinaryStart;
            BinaryCount += 1;
            return new(index);
        }

        private Variable CreateSlackVariable()
        {
            var index = SlackCount;
            SlackCount += 1;
            return new(index);
        }

        /// <summary>
        /// Add a single constraint to this linear problem.
        /// </summary>
        ///
        /// <remarks>
        /// This method will do some pre-processing in case the added constraint
        /// gives a known value for a variable.
        /// </remarks>
        public void AddConstraint(LinearConstraint constraint)
        {
            if (constraint == null)
                throw new ArgumentNullException("constraint");

            constraints.Add(constraint);
        }

        /// <summary>
        /// Add constraints such that <paramref name="a"/> OR <paramref name="b"/>
        /// can be true.
        /// </summary>
        /// <param name="a"></param>
        /// <param name="b"></param>
        public void AddOrConstraint(LinearConstraint a, LinearConstraint b)
        {
            if (a == null)
                throw new ArgumentNullException("a");
            if (b == null)
                throw new ArgumentNullException("b");

            if (a.KnownInconsistent && b.KnownInconsistent)
                throw new UnsolvableProblemException();

            // In order to convert this OR constraint into a linear constraints
            // we use the "Big M" method. This isn't really the best way to do
            // this, but it lets us come up with a solution using a single run
            // of the simplex algorithm, so it is good enough for KSP.
            //
            // The way we represent an OR constraint here is that we introduce
            // two new binary variables za and zb with the constraint that
            // za + zb <= 1.
            //
            // Then, depending on the relation in the constraint we introduce
            // a M*za or M*zb term, where M is a large enough value that it
            // overrides the values of the term. We then introduce this as
            // follows, depending on the relation:
            //
            // (<=) => a - M*za <= c
            // (>=) => a + M*za >= c
            // (==) => split a in to >= and <= terms, then do the above
            //
            // Note that we are defining an OR constraint. If we wanted XOR
            // we could do za + zb == 1.

            var za = CreateBinaryVariable();
            var zb = CreateBinaryVariable();

            AddOrTerm(a, za);
            AddOrTerm(b, zb);
            constraints.Add(za + zb <= 1.0);
        }

        private void AddOrTerm(LinearConstraint constraint, Variable z)
        {
            switch (constraint.relation)
            {
                case Relation.LEqual:
                    constraint.variables.Add(-M * z);
                    constraints.Add(constraint);
                    break;
                case Relation.GEqual:
                    constraint.variables.Add(M * z);
                    constraints.Add(constraint);
                    break;
                case Relation.Equal:
                    var copy = constraint.Clone();

                    constraint.variables.Add(-M * z);
                    constraint.relation = Relation.LEqual;

                    copy.variables.Add(M * z);
                    copy.relation = Relation.GEqual;

                    constraints.Add(constraint);
                    constraints.Add(copy);
                    break;
            }
        }

        public LinearSolution Maximize(LinearEquation func)
        {
            PresolveEqualityConstraints();

            // Apply all substitutions to the objective function.
            //
            // We ignore the constants here because they don't make a difference
            // to the relative values of the objective function.
            foreach (var sub in substitutions.Values)
                func.Substitute(sub.variable.Index, sub.equation);

            LogUtil.Log($"After preprocessing:\nMaximize Z = {func}\nsubject to\n{this}");

            AssignSlackVariables();

            var varMap = BuildVarMap();
            var tableau = BuildSimplexTableau(func, varMap);
            Simplex.SolveTableau(tableau);

            var soln = ExtractTableauSolution(tableau, varMap);
            LogUtil.Log($"Solution: {soln:G4}");

#if DEBUG
            CheckSolution(soln);
#endif
            return soln;
        }

        /// <summary>
        /// Equality constraints cannot go as-is into the simplex solver.
        /// However, each equality constraint gets us
        /// </summary>
        private void PresolveEqualityConstraints()
        {
            List<LinearConstraint> known = [];

            // Remove everything that is an equality constraint or gives us
            // enough information to infer the variable values.
            constraints.RemoveAll(constraint =>
            {
                if (constraint.KnownInconsistent)
                    throw new UnsolvableProblemException();

                if (constraint.variables.Count == 0)
                    return true;

                switch (constraint.relation)
                {
                    case Relation.Equal:
                        known.Add(constraint);
                        return true;
                    case Relation.LEqual:
                        if (constraint.constant > 0.0)
                            return false;
                        if (!constraint.variables.All(v => v.Coef >= 0.0))
                            return false;
                        if (constraint.constant < 0.0)
                            throw new UnsolvableProblemException();

                        foreach (var var in constraint.variables)
                            known.Add(var == 0.0);
                        return true;
                    case Relation.GEqual:
                        if (constraint.constant < 0.0)
                            return false;
                        if (!constraint.variables.All(v => v.Coef <= 0.0))
                            return false;
                        if (constraint.constant > 0.0)
                            throw new UnsolvableProblemException();

                        foreach (var var in constraint.variables)
                            known.Add(var == 0.0);
                        return true;
                }

                return false;
            });

            // This just helps make the solution look nicer.
            known.Sort();

            Matrix matrix = new((int)LinearCount + 1, known.Count);
            for (int y = 0; y < known.Count; ++y)
            {
                var constraint = known[y];
                foreach (var var in constraint.variables)
                    matrix[(int)var.Index, y] = var.Coef;

                matrix[matrix.Width - 1, y] = constraint.constant;
            }

            LinAlg.GaussianEliminationOrdered(matrix);

            for (int y = 0; y < matrix.Height; ++y)
            {
                int index = LinAlg.FindFirstNonZeroInRow(matrix, y);

                // This entire row ended up being zeros, nothing to do here.
                if (index == -1)
                    continue;

                // All variables have zero coefficients but the constant is
                // non-zero. The LP is has no solutions.
                if (index == matrix.Width - 1)
                    throw new UnsolvableProblemException();

                Variable var = new((uint)index);
                LinearEquation eq = new(matrix.Width - index);

                // Note: we use Sub here because we are semantically moving the
                //       variables to the other side of the == sign.
                for (int x = index + 1; x < matrix.Width - 1; ++x)
                    eq.Sub(new((uint)x, matrix[x, y] / matrix[index, y]));

                substitutions.Add(
                    var.Index,
                    new LinearEquality()
                    {
                        variable = var,
                        equation = eq,
                        constant = matrix[matrix.Width - 1, y] / matrix[index, y],
                    }
                );
            }

            constraints.RemoveAll(constraint =>
            {
                foreach (var equality in substitutions.Values)
                {
                    constraint.Substitute(
                        equality.variable.Index,
                        equality.equation,
                        equality.constant
                    );
                }

                if (constraint.KnownInconsistent)
                    throw new UnsolvableProblemException();

                return constraint.variables.Count == 0;
            });
        }

        private void AssignSlackVariables()
        {
            SlackCount = 0;

            foreach (var constraint in constraints)
            {
                switch (constraint.relation)
                {
                    case Relation.Equal:
                        break; // Nothing to do here

                    case Relation.LEqual:
                        constraint.slack = CreateSlackVariable();
                        break;

                    case Relation.GEqual:
                        constraint.slack = -CreateSlackVariable();
                        break;
                }
            }
        }

        private IntMap<uint> BuildVarMap()
        {
            IntMap<uint> map = new((int)LinearCount);

            uint j = 0;
            for (uint i = 0; i < LinearCount; ++i)
            {
                if (substitutions.ContainsKey(i))
                    continue;
                map[i] = j++;
            }

            return map;
        }

        private Matrix BuildSimplexTableau(LinearEquation func, IntMap<uint> varMap)
        {
            var varCount = varMap.Count + (int)(BinaryCount + SlackCount);
            if (varCount < constraints.Count)
                throw new OverconstrainedLinearProblemException(
                    "Support for solving over-constrained linear problems is not implemented"
                );

            var tableau = new Matrix(varCount + 1, constraints.Count + 1);

            foreach (var var in func)
                tableau[varMap[var.Index], 0] = -var.Coef;

            for (int i = 0; i < constraints.Count; ++i)
            {
                var constraint = constraints[i];

                foreach (var var in constraint.variables.Values)
                {
                    var index = var.Index;
                    if (index >= BinaryStart)
                        index = index - BinaryStart + (uint)varMap.Count;
                    else
                        index = varMap[index];

                    tableau[index, (uint)i + 1] = var.Coef;
                }

                tableau[tableau.Width - 1, i + 1] = constraint.constant;
                if (constraint.slack != null)
                {
                    var slack = (Variable)constraint.slack;
                    var index = (uint)varMap.Count + BinaryCount + slack.Index;

                    tableau[index, (uint)i + 1] = slack.Coef;
                }
            }

            return tableau;
        }

        private LinearSolution ExtractTableauSolution(Matrix tableau, IntMap<uint> varMap)
        {
            IntMap<uint> inverse = new((int)LinearCount);
            foreach (var (src, tgt) in varMap)
                inverse[(int)tgt] = (uint)src;

            double[] values = new double[LinearCount];

            foreach (var (x, y) in FindBasicVariables(tableau, varMap.Count))
                values[inverse[x]] = tableau[tableau.Width - 1, y];

            var soln = new LinearSolution(values);

            foreach (var (index, sub) in substitutions.KSPEnumerate())
                values[index] = soln.Evaluate(sub.equation) + sub.constant;

            return new LinearSolution(values);
        }

        private IEnumerable<KVPair<int, int>> FindBasicVariables(Matrix tableau, int nvars)
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

        private void CheckSolution(LinearSolution soln, double tol = 1e-3)
        {
            foreach (var constraint in constraints)
            {
                double value = soln.Evaluate(constraint.variables);
                double constant = constraint.constant;

                switch (constraint.relation)
                {
                    case Relation.Equal:
                        if (MathUtil.ApproxEqual(value, constant, tol))
                            continue;
                        break;
                    case Relation.LEqual:
                        if (value <= constant + tol)
                            continue;
                        break;
                    case Relation.GEqual:
                        if (value >= constant - tol)
                            continue;
                        break;
                }

                throw new Exception(
                    $"LP solver solution did not satisfy constraint:\n    constraint: {constraint.ToString("R")} (got {value:g})\n    solution: {soln}"
                );
            }
        }

        public override string ToString()
        {
            StringBuilder builder = new();
            foreach (var sub in substitutions.Values)
            {
                builder.Append(sub);
                builder.Append("\n");
            }

            foreach (var constraint in constraints)
            {
                builder.Append(constraint);
                builder.Append("\n");
            }

            return builder.ToString();
        }

        internal static void RenderCoef(
            StringBuilder builder,
            double coef,
            string var,
            ref bool first
        )
        {
            if (coef == 0.0)
                return;

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
        }

        // var == equation + constant
        private struct LinearEquality
        {
            public Variable variable;
            public LinearEquation equation;
            public double constant;

            public override readonly string ToString()
            {
                if (equation.Count == 0)
                    return $"{variable} == {constant}";
                if (constant == 0.0)
                    return $"{variable} == {equation}";
                if (constant < 0.0)
                    return $"{variable} == {equation} - {-constant}";
                return $"{variable} == {equation} + {constant}";
            }
        }
    }

    internal class UnsolvableProblemException(
        string message = "Linear problem has no valid solutions"
    ) : Exception(message) { }

    internal class OverconstrainedLinearProblemException(string message) : Exception(message) { }
}
