using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using BackgroundResourceProcessing.Collections;
using BackgroundResourceProcessing.Tracing;
using KSPAchievements;

namespace BackgroundResourceProcessing.Solver
{
    internal class LinearProblem
    {
        const double M = 1e9;

        // The number of linear variables that have been created.
        int VariableCount = 0;

        // The number of slack variables that have been created.
        int SlackCount = 0;

        // Constraints in standard form (Ax <= b)
        List<SolverConstraint> constraints = [];

        // Equalities in equation form.
        List<SolverConstraint> equalities = [];

        List<OrConstraint> disjunctions = [];

        IntMap<LinearEquality> substitutions = null;

        public LinearProblem() { }

        #region variable creation
        public Variable CreateVariable()
        {
            var index = VariableCount;
            VariableCount += 1;
            return new(index);
        }

        public VariableSet CreateVariables(int count)
        {
            if (count < 0)
                throw new ArgumentException($"Cannot create a negative number of converters");

            var set = new VariableSet(VariableCount, count);
            VariableCount += count;
            return set;
        }

        private Variable CreateBinaryVariable()
        {
            return CreateVariable();
        }

        private Variable CreateSlackVariable()
        {
            var index = SlackCount;
            SlackCount += 1;
            return new(index);
        }
        #endregion

        #region add constraints
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
            if (constraint.KnownInconsistent)
                throw new UnsolvableProblemException();

            if (constraint.relation == Relation.Equal)
                equalities.Add(new SolverConstraint(constraint));
            else
                constraints.Add(StandardizeConstraint(constraint));
        }

        private static SolverConstraint StandardizeConstraint(LinearConstraint constraint)
        {
            if (constraint.relation == Relation.Equal)
                throw new ArgumentException("Cannot standardize an == constraint");

            if (constraint.relation == Relation.GEqual)
            {
                var eq = constraint.variables.Negated();
                return new() { variables = eq, constant = -constraint.constant };
            }

            return new() { variables = constraint.variables, constant = constraint.constant };
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

            var z = CreateBinaryVariable();
            var lhs = StandardizeConstraint(a);
            var rhs = StandardizeConstraint(b);

            disjunctions.Add(
                new OrConstraint()
                {
                    variable = z,
                    lhs = lhs,
                    rhs = rhs,
                }
            );
        }
        #endregion

        public LinearSolution Maximize(LinearEquation func)
        {
            using var span = new TraceSpan("LinearProblem.Maximize");
            Trace(() => $"\nMaximize Z = {func}\nsubject to\n{this}");

            var soln = SolveBranchAndBound(func);

            // This isn't strictly necessary but it is cheap and has proven to
            // be incredibly useful for catching bugs so I've enabled it
            // unconditionally.
            CheckSolution(soln);

            return soln;
        }

        #region presolve
        private void Presolve()
        {
            using var span = new TraceSpan("LinearProblem.Presolve");
            substitutions = new(VariableCount);

            InferZeros();

            do
            {
                equalities.Sort();
                equalities.Deduplicate();

                Matrix matrix = new(VariableCount + 1, equalities.Count);
                for (int y = 0; y < equalities.Count; ++y)
                {
                    var eq = equalities[y];
                    foreach (var var in eq.variables)
                        matrix[var.Index, y] = var.Coef;
                    matrix[matrix.Width - 1, y] = eq.constant;
                }

                LinAlg.GaussianEliminationOrdered(matrix);

                for (int y = 0; y < matrix.Height; ++y)
                {
                    int index = LinAlg.FindFirstNonZeroInRow(matrix, y);

                    // This entire row ended up being zeros, nothing to do here.
                    if (index == -1)
                        continue;

                    // All variables have zero coefficients but the constant is
                    // non-zero. The LP has no solutions.
                    if (index == matrix.Width - 1)
                        throw new UnsolvableProblemException();

                    Variable var = new(index);
                    LinearEquation eq = new(matrix.Width - index);

                    // Note: we use Sub here because we are semantically moving the
                    //       variables to the other side of the == sign.
                    for (int x = index + 1; x < matrix.Width - 1; ++x)
                        eq.Sub(new(x, matrix[x, y] / matrix[index, y]));

                    substitutions[var.Index] = new()
                    {
                        variable = var.Index,
                        equation = eq,
                        constant = matrix[matrix.Width - 1, y] / matrix[index, y],
                    };
                }

                foreach (var constraint in constraints)
                {
                    foreach (var sub in substitutions.Values)
                        constraint.Substitute(sub.variable, sub.equation, sub.constant);
                }

                foreach (var dis in disjunctions)
                {
                    foreach (var sub in substitutions.Values)
                    {
                        dis.lhs.Substitute(sub.variable, sub.equation, sub.constant);
                        dis.rhs.Substitute(sub.variable, sub.equation, sub.constant);
                    }
                }
            } while (InferZeros());
        }

        private bool InferZeros()
        {
            using var span = new TraceSpan("LinearProblem.InferZeros");

            int count = constraints.RemoveAll(constraint =>
            {
                // If the constraint is empty then either it is trivial or the
                // problem is unsolvable.
                if (constraint.variables.Count == 0)
                {
                    if (constraint.constant < 0)
                        throw new UnsolvableProblemException();
                    return true;
                }

                if (constraint.constant > 0)
                    return false;
                if (!constraint.variables.All(v => v.Coef >= 0.0))
                    return false;

                if (constraint.constant < 0)
                    throw new UnsolvableProblemException();
                if (double.IsNaN(constraint.constant))
                    throw new Exception("LP constraint had a NaN constant");

                equalities.Add(constraint);
                return true;
            });

            return count != 0;
        }
        #endregion

        #region branch & bound
        private LinearSolution SolveBranchAndBound(LinearEquation func)
        {
            using var span = new TraceSpan("LinearProblem.SolveBranchAndBound");

            IntMap<int> varMap = new(VariableCount);
            IntMap<int> binaryIndices = new(VariableCount);

            for (int i = 0; i < disjunctions.Count; ++i)
                binaryIndices.Add(disjunctions[i].variable.Index, i);

            Presolve();

            foreach (var sub in substitutions.Values)
                func.Substitute(sub.variable, sub.equation);

            Trace(() => $"After presolve:\nMaximize Z = {func}\nsubject to\n{this}");

            // Do a depth-first search but order by score in order to break depth ties.
            PriorityQueue<QueueEntry, KeyValuePair<int, double>> entries = new(
                new InverseComparer()
            );
            entries.Enqueue(
                new()
                {
                    score = double.NegativeInfinity,
                    depth = 0,
                    choices = new BinaryChoice[disjunctions.Count],
                },
                new(0, double.NegativeInfinity)
            );

            // The best score seen so far for a fully completed variable selection.
            double best = double.NegativeInfinity;
            LinearSolution? soln = null;

            while (entries.TryDequeue(out var entry, out var _))
            {
                using var iterSpan = new TraceSpan("LinearProblem.SolveBranchAndBound.Iter");

                // The best possible score for the relaxed version of this entry
                // is still worse than the best score we've seen so far. There
                // is no point in examining it any further.
                if (entry.score < best)
                    continue;

                if (entry.depth < disjunctions.Count)
                {
                    // An optimization. If we've already determined that some
                    // choice is optimal then we immediately just go deeper
                    // since there is no point in running simplex at this step.
                    if (entry.choices[entry.depth] != BinaryChoice.Unknown)
                    {
                        entry.depth += 1;
                        entries.Enqueue(entry, new(entry.depth, entry.score));
                        continue;
                    }
                }

                Trace(() =>
                    $"Solving relaxation with choice variables: {RenderChoices(entry.choices)}"
                );

                BuildVarMap(varMap, entry.choices, binaryIndices);
                var tableau = BuildSimplexTableau(func, entry.choices, varMap);
                BitSet selected = new(tableau.Width);

                try
                {
                    Simplex.SolveTableau(tableau, selected);
                }
                catch (UnsolvableProblemException)
                {
                    // Some variable choices may not be solvable. That's not an
                    // issue, it just means that we don't need to consider
                    // any more changes here.
                    continue;
                }

                var score = tableau[tableau.Width - 1, 0];

                Trace(() =>
                {
                    var current = ExtractTableauSolution(tableau, entry.choices, varMap, selected);
                    return $"Step solution {current} with score {score}";
                });

                // The relaxation we got here (or solution) is not better than
                // the current incumbent solution, so we have no extra work to
                // do here.
                if (score <= best)
                    continue;

                var current = ExtractTableauSolution(tableau, entry.choices, varMap, selected);
                if (entry.depth == disjunctions.Count)
                {
                    // We've reached the bottom of the search tree. This means
                    // that we have a valid solution and also that it is the
                    // best one we've seen so far.

                    soln = current;
                    best = score;
                }
                else
                {
                    // We still have more work to do, and we need to continue
                    // exploring down the search tree.

                    var var = disjunctions[entry.depth].variable;
                    var value = current[var];

                    if (value == 0.0)
                    {
                        entry.choices[entry.depth] = BinaryChoice.Left;
                        entry.depth += 1;
                        entries.Enqueue(entry, new(entry.depth, score));
                    }
                    else if (value == 1.0)
                    {
                        entry.choices[entry.depth] = BinaryChoice.Right;
                        entry.depth += 1;
                        entries.Enqueue(entry, new(entry.depth, score));
                    }
                    else
                    {
                        var lc = (BinaryChoice[])entry.choices.Clone();
                        var rc = entry.choices;

                        lc[entry.depth] = BinaryChoice.Left;
                        rc[entry.depth] = BinaryChoice.Right;

                        var left = new QueueEntry()
                        {
                            score = score,
                            depth = entry.depth + 1,
                            choices = rc,
                        };
                        var right = new QueueEntry()
                        {
                            score = score,
                            depth = entry.depth + 1,
                            choices = lc,
                        };

                        if (value < 0.5)
                        {
                            // prioritize setting z=0 next
                            entries.Enqueue(right, new(entry.depth + 1, score));
                            entries.Enqueue(left, new(entry.depth + 1, score));
                        }
                        else
                        {
                            // prioritize setting z=1 next
                            entries.Enqueue(right, new(entry.depth + 1, score));
                            entries.Enqueue(left, new(entry.depth + 1, score));
                        }
                    }
                }
            }

            if (soln == null)
                throw new UnsolvableProblemException();

            Trace(() => $"Final solution {soln} with score {best}");

            return (LinearSolution)soln;
        }

        private Matrix BuildSimplexTableau(
            LinearEquation func,
            BinaryChoice[] choices,
            IntMap<int> varMap
        )
        {
            using var span = new TraceSpan("LinearProblem.BuildSimplexTableau");

            int constraintCount = constraints.Count;
            constraintCount += choices
                .Select(choice => choice == BinaryChoice.Unknown ? 3 : 1)
                .Sum();

            var tableau = new Matrix(varMap.Count + constraintCount + 1, constraintCount + 1);

            foreach (var var in func)
                tableau[varMap[var.Index], 0] = -var.Coef;

            int y = 1;
            foreach (var constraint in constraints)
                WriteConstraintToTableau(tableau, constraint, varMap, ref y);

            for (int i = 0; i < disjunctions.Count; ++i)
            {
                var constraint = disjunctions[i];
                var choice = choices[i];

                switch (choice)
                {
                    case BinaryChoice.Left:
                        WriteConstraintToTableau(tableau, constraint.lhs, varMap, ref y);
                        break;
                    case BinaryChoice.Right:
                        WriteConstraintToTableau(tableau, constraint.rhs, varMap, ref y);
                        break;
                    case BinaryChoice.Unknown:
                        // In order to represent an OR constraint we use what's called
                        // the "bit M" method. This doesn't directly give us a solution
                        // here but the relaxed problem (e.g. allow binary variables to
                        // take non-integer values) does give us an upper bound on any
                        // the solution further down in the search tree.
                        //
                        // The way this works is like this: we have the constraint
                        // lhs <= bl OR rhs <= br
                        //
                        // We convert that into three constraints
                        // - lhs <= bl + M*z
                        // - rhs <= br + M*(1 - z)
                        // - z in {0,1}
                        //
                        // When relaxed and standardized we end up with
                        // - lhs - M*z <= bl
                        // - rhs + M*z <= br + M
                        // - z <= 1

                        var z = constraint.variable;

                        // We arbitrarily pick lhs to be active when z is 0.
                        WriteConstraintToTableau(tableau, constraint.lhs, varMap, ref y);
                        tableau[varMap[z.Index], y - 1] = -M * z.Coef;

                        WriteConstraintToTableau(tableau, constraint.rhs, varMap, ref y);
                        tableau[varMap[z.Index], y - 1] = M * z.Coef;
                        tableau[tableau.Width - 1, y - 1] += M;

                        WriteConstraintToTableau(
                            tableau,
                            new SolverConstraint()
                            {
                                constant = 1.0,
                                variables = new LinearEquation(z),
                            },
                            varMap,
                            ref y
                        );

                        break;
                    default:
                        throw new NotImplementedException($"Invalid BinaryChoice value {choice}");
                }
            }

            return tableau;
        }

        private void WriteConstraintToTableau(
            Matrix tableau,
            SolverConstraint constraint,
            IntMap<int> varMap,
            ref int y
        )
        {
            foreach (var var in constraint.variables)
                tableau[varMap[var.Index], y] = var.Coef;

            tableau[varMap.Count + y - 1, y] = 1.0;
            tableau[tableau.Width - 1, y] = constraint.constant;
            y += 1;
        }

        private void BuildVarMap(
            IntMap<int> varMap,
            BinaryChoice[] choices,
            IntMap<int> binaryIndices
        )
        {
            varMap.Clear();
            Debug.Assert(varMap.Capacity == VariableCount);

            int index = 0;
            for (int i = 0; i < VariableCount; ++i)
            {
                if (substitutions.ContainsKey(i))
                    continue;
                if (binaryIndices.TryGetValue(i, out int bIndex))
                {
                    if (choices[bIndex] != BinaryChoice.Unknown)
                        continue;
                }

                varMap[i] = index++;
            }
        }

        private LinearSolution ExtractTableauSolution(
            Matrix tableau,
            BinaryChoice[] choices,
            IntMap<int> varMap,
            BitSet selected
        )
        {
            using var span = new TraceSpan("LinearProblem.ExtractTableauSolution");

            IntMap<int> inverse = new(tableau.Width);
            foreach (var (src, tgt) in varMap)
                inverse[tgt] = src;

            double[] values = new double[VariableCount];

            foreach (var x in selected)
            {
                // We may have selected one of the slack variables. However we
                // don't set those in the solution.
                if (x >= VariableCount)
                    break;

                var y = FindSetColumnValue(tableau, x);
                if (y == -1)
                    continue;

                values[inverse[x]] = tableau[tableau.Width - 1, y];
            }

            var soln = new LinearSolution(values);

            foreach (var (index, sub) in substitutions)
                values[index] = soln.Evaluate(sub.equation) + sub.constant;

            for (int i = 0; i < choices.Length; ++i)
            {
                var index = disjunctions[i].variable.Index;
                var choice = choices[i];

                if (choice == BinaryChoice.Left)
                    values[index] = 0.0;
                else if (choice == BinaryChoice.Right)
                    values[index] = 1.0;
            }

            return soln;
        }

        private static int FindSetColumnValue(Matrix tableau, int column)
        {
            int row = -1;

            for (int i = 0; i < tableau.Height; ++i)
            {
                var elem = tableau[column, i];
                if (elem == 0.0)
                    continue;
                if (elem != 1.0)
                    return -1;

                if (row < 0)
                    row = i;
                else
                    return -1;
            }

            return row;
        }

        private void CheckSolution(LinearSolution soln, double tol = 1e-6)
        {
            foreach (var constraint in constraints)
            {
                double value = soln.Evaluate(constraint.variables);
                double constant = constraint.constant;

                if (value <= constant + tol)
                    continue;

                throw new Exception(
                    $"LP solver solution did not satisfy constraint:\n    constraint: {constraint.ToRelationString("<=")} (got {value})\n    solution: {soln}"
                );
            }
        }
        #endregion

        public override string ToString()
        {
            StringBuilder builder = new();
            foreach (var equality in equalities)
            {
                builder.Append(equality.ToRelationString("=="));
                builder.Append("\n");
            }

            foreach (var constraint in constraints)
            {
                builder.Append(constraint.ToRelationString("<="));
                builder.Append("\n");
            }

            foreach (var or in disjunctions)
            {
                builder.Append("(");
                builder.Append(or.lhs.ToRelationString("<="));
                builder.Append(" || ");
                builder.Append(or.rhs.ToRelationString("<="));
                builder.Append(")");
            }

            if (substitutions != null)
            {
                foreach (var sub in substitutions.Values)
                {
                    builder.Append("sub ");
                    builder.Append(sub);
                    builder.AppendLine();
                }
            }

            return builder.ToString();
        }

        private string RenderChoices(BinaryChoice[] choices)
        {
            StringBuilder builder = new();

            for (int i = 0; i < choices.Length; ++i)
            {
                if (i != 0)
                    builder.Append(", ");

                var choice = choices[i] switch
                {
                    BinaryChoice.Unknown => "U",
                    BinaryChoice.Left => "0",
                    BinaryChoice.Right => "1",
                    _ => "X",
                };

                builder.Append($"{disjunctions[i].variable}={choice}");
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

        private struct OrConstraint
        {
            // The decision variable to use when computing the linear relaxation
            // of the problem.
            public Variable variable;

            public SolverConstraint lhs;
            public SolverConstraint rhs;
        }

        private class SolverConstraint() : IComparable<SolverConstraint>
        {
            public LinearEquation variables;
            public double constant;

            public SolverConstraint(LinearConstraint constraint)
                : this()
            {
                variables = constraint.variables;
                constant = constraint.constant;
            }

            public void Substitute(int index, LinearEquation eq, double value)
            {
                if (!variables.TryGetValue(index, out var variable))
                    return;
                if (eq.Contains(variable))
                    throw new ArgumentException(
                        $"Attempted to substitue variable {variable} with equation {eq} containing {variable}"
                    );

                variables.Remove(index);
                var coef = variable.Coef;
                foreach (var var in eq)
                    variables.Add(var * coef);
                constant -= coef * value;
            }

            public int CompareTo(SolverConstraint other)
            {
                int cmp = variables.CompareTo(other.variables);
                if (cmp != 0)
                    return cmp;
                return constant.CompareTo(other.constant);
            }

            public string ToRelationString(string relation)
            {
                return $"{variables} {relation} {constant}";
            }
        }

        // var == equation + constant
        private struct LinearEquality
        {
            public int variable;
            public LinearEquation equation;
            public double constant;

            public override readonly string ToString()
            {
                if (equation.Count == 0)
                    return $"x{variable} == {constant}";
                if (constant == 0.0)
                    return $"x{variable} == {equation}";
                if (constant < 0.0)
                    return $"x{variable} == {equation} - {-constant}";
                return $"x{variable} == {equation} + {constant}";
            }
        }

        enum BinaryChoice
        {
            Unknown = 0,
            Left,
            Right,
        }

        private struct QueueEntry
        {
            public double score;
            public int depth;
            public BinaryChoice[] choices;
        }

        private class InverseComparer : IComparer<KeyValuePair<int, double>>
        {
            public int Compare(KeyValuePair<int, double> x, KeyValuePair<int, double> y)
            {
                int cmp = y.Key.CompareTo(x.Key);
                if (cmp != 0)
                    return cmp;

                return y.Value.CompareTo(x.Value);
            }
        }

        [Conditional("SOLVERTRACE")]
        private static void Trace(Func<string> func)
        {
#if SOLVERTRACE
            LogUtil.Log(func());
#endif
        }
    }

    internal class UnsolvableProblemException(
        string message = "Linear problem has no valid solutions"
    ) : Exception(message) { }

    internal class OverconstrainedLinearProblemException(string message) : Exception(message) { }
}
