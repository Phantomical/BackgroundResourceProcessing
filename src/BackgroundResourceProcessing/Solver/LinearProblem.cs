using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using BackgroundResourceProcessing.Collections;
using BackgroundResourceProcessing.Solver.Simplex;
using Steamworks;

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

        // Constants for variables with known values.
        Dictionary<uint, double> constants = [];

        // Constraints making up the problem.
        List<LinearConstraint> constraints = [];

        public LinearProblem() { }

        public Variable CreateVariable()
        {
            var index = LinearCount;
            LinearCount += 1;
            return new(index);
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

        public LinearSolution Maximize(LinearEquation func)
        {
            // Variables that have been solved for would only add a constant to
            // the objective function so they can just be removed.
            foreach (var index in constants.Keys)
                func.variables.Remove(index);

            AssignSlackVariables();

            var varMap = BuildVarMap();
            var tableau = BuildSimplexTableau(func, varMap);
            Simplex2.SolveTableau(tableau);

            return ExtractTableauSolution(tableau, varMap);
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

            foreach (var (index, value) in constants.KSPEnumerate())
                constraint.Substitute(index, value);

            if (ExtractConstants(constraint))
                return;

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

            foreach (var (index, value) in constants.KSPEnumerate())
            {
                a.Substitute(index, value);
                b.Substitute(index, value);
            }

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
                    constraint.variables.Add(z.Index, -M * z);
                    constraints.Add(constraint);
                    break;
                case Relation.GEqual:
                    constraint.variables.Add(z.Index, M * z);
                    constraints.Add(constraint);
                    break;
                case Relation.Equal:
                    var copy = constraint.Clone();

                    constraint.variables.Add(z.Index, -M * z);
                    constraint.relation = Relation.LEqual;

                    copy.variables.Add(z.Index, M * z);
                    copy.relation = Relation.GEqual;

                    constraints.Add(constraint);
                    constraints.Add(copy);
                    break;
            }
        }

        /// <summary>
        /// Attempt to convert all the variables in this constraint to constants.
        /// </summary>
        /// <returns><c>true</c> if this constraint could be turned into constant values</returns>
        private bool ExtractConstants(LinearConstraint constraint)
        {
            if (constraint.KnownInconsistent)
                throw new UnsolvableProblemException();

            switch (constraint.relation)
            {
                case Relation.Equal:
                    if (constraint.variables.Count == 1)
                    {
                        var variable = constraint.variables.First().Value;
                        var value = constraint.constant / variable.Coef;

                        if (value < 0.0)
                            throw new UnsolvableProblemException();

                        // Special case: a x = c must mean that x = a/c
                        // (subject to x >= 0, of course).
                        Substitute(variable.Index, value);
                        return true;
                    }
                    break;

                case Relation.LEqual:
                    if (constraint.constant > 0.0)
                        return false;
                    if (constraint.variables.Values.All(v => v.Coef >= 0.0))
                    {
                        if (constraint.constant < 0.0)
                            throw new UnsolvableProblemException();

                        // Special case: a x1 + b x2 + ... <= 0 with a, b, ...
                        // all positive means that all the variables involved
                        // must be 0.
                        SubstituteAll(
                            constraint.variables.Values.Select(var => new KVPair<uint, double>(
                                var.Index,
                                0.0
                            ))
                        );
                        return true;
                    }
                    break;

                case Relation.GEqual:
                    if (constraint.constant < 0.0)
                        return false;

                    if (constraint.variables.Values.All(v => v.Coef <= 0.0))
                    {
                        if (constraint.constant > 0.0)
                            throw new UnsolvableProblemException();

                        // Special case: a x1 + b x2 + ... >= 0 with a, b, ...
                        // all negative means that all the variables involved
                        // must be 0.
                        SubstituteAll(
                            constraint.variables.Values.Select(var => new KVPair<uint, double>(
                                var.Index,
                                0.0
                            ))
                        );
                        return true;
                    }
                    break;
            }

            return false;
        }

        private void Substitute(uint index, double value)
        {
            SubstituteAll<KVPair<uint, double>[]>([new(index, value)]);
        }

        private void SubstituteAll<T>(T values)
            where T : IEnumerable<KVPair<uint, double>>
        {
            SlotStack<KVPair<uint, double>> stack = new();
            SlotStack<int> removed = new();

            foreach (var (index, value) in values)
            {
                constants.Add(index, value);
                stack.Push(new(index, value));
            }

            while (stack.TryPop(out var entry))
            {
                var (index, value) = entry;

                for (int i = 0; i < constraints.Count; ++i)
                {
                    var constraint = constraints[i];
                    constraint.Substitute(index, value);

                    if (constraint.KnownInconsistent)
                        throw new UnsolvableProblemException(
                            "Linear problem has no valid solutions"
                        );

                    if (constraint.relation != Relation.Equal)
                        continue;
                    if (constraint.variables.Count != 1)
                        continue;

                    var variable = constraint.variables.First().Value;
                    var soln = constraint.constant / variable.Coef;

                    if (soln < 0.0)
                        throw new UnsolvableProblemException(
                            $"Only valid solution for variable x{variable.Index} is {soln}, which is negative"
                        );

                    constants.Add(variable.Index, soln);
                    stack.Push(new(variable.Index, soln));

                    removed.Push(i);
                }

                while (removed.TryPop(out var i))
                    constraints.RemoveAt(i);
            }
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
                if (constants.ContainsKey(i))
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

            foreach (var var in func.variables.Values)
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

            foreach (var (index, value) in constants.KSPEnumerate())
                values[index] = value;

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

        public override string ToString()
        {
            StringBuilder builder = new();
            foreach (var (index, value) in constants.KSPEnumerate())
                builder.Append($"{new Variable(index)} == {value:G}\n");

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
    }

    internal readonly struct LinearSolution(double[] values)
    {
        readonly double[] values = values;

        public readonly int Count => values.Length;

        public readonly double this[int index] => values[index];
        public readonly double this[uint index] => values[index];
        public readonly double this[Variable var] => this[var.Index];
    }

    internal class UnsolvableProblemException(
        string message = "Linear problem has no valid solutions"
    ) : Exception(message) { }
}
