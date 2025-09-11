using System;
using System.Text;
using BackgroundResourceProcessing.Collections.Burst;
using BackgroundResourceProcessing.Utils;
using Unity.Burst;
using Unity.Burst.CompilerServices;
using static BackgroundResourceProcessing.BurstSolver.LinearPresolve;
using static BackgroundResourceProcessing.Collections.KeyValuePairExt;
using ConstraintState = BackgroundResourceProcessing.Solver.LinearPresolve.ConstraintState;

namespace BackgroundResourceProcessing.BurstSolver;

[BurstCompile]
internal struct LinearProblem()
{
    const double M = 1e9;

    // The number of linear variables that have been created.
    public int VariableCount { get; private set; } = 0;

    // Constraints in standard form (Ax <= b)
    RawList<SolverConstraint> constraints = new(32);

    // Constraints that are known to only involve 1 variable.
    RawList<SimpleSolverConstraint> simple = new(32);

    // Equalities in equation form.
    RawList<SolverConstraint> equalities = new(32);

    RawList<OrConstraint> disjunctions = new(32);

    RawIntMap<LinearEquality> substitutions = default;

    #region Variable Creation
    public Variable CreateVariable()
    {
        VariableCount += 1;
        return new(VariableCount - 1);
    }

    [IgnoreWarning(1370)]
    public VariableSet CreateVariables(int count)
    {
        if (count < 0)
            throw new ArgumentOutOfRangeException(nameof(count));

        var start = VariableCount;
        VariableCount += count;
        return new VariableSet(start, count);
    }
    #endregion

    #region Add Constraints
    /// <summary>
    /// Add a single constraint to this linear problem.
    /// </summary>
    ///
    /// <remarks>
    /// Note that this method takes ownership of the constraint. If you don't
    /// want that then make sure to clone the constraint first.
    /// </remarks>
    public void AddConstraint(LinearConstraint constraint)
    {
        switch (constraint.relation)
        {
            case Relation.Equal:
                equalities.Add(new SolverConstraint(constraint));
                break;

            case Relation.GEqual:
            case Relation.LEqual:
                constraints.Add(StandardizeConstraint(constraint));
                break;
        }
    }

    /// <summary>
    /// Add a single constraint to this linear problem.
    /// </summary>
    ///
    /// <remarks>
    /// Note that this method takes ownership of the constraint. If you don't
    /// want that then make sure to clone the constraint first.
    /// </remarks>
    public void AddConstraint(SimpleConstraint constraint)
    {
        switch (constraint.relation)
        {
            case Relation.Equal:

                equalities.Add(
                    new SolverConstraint
                    {
                        variables = new(constraint.variable),
                        constant = constraint.constant,
                    }
                );
                break;

            case Relation.GEqual:
            case Relation.LEqual:
                simple.Add(StandardizeConstraint(constraint));
                break;
        }
    }

    [IgnoreWarning(1370)]
    private static SolverConstraint StandardizeConstraint(LinearConstraint constraint)
    {
        switch (constraint.relation)
        {
            case Relation.GEqual:
                constraint.variables.Negate();
                constraint.constant *= -1.0;
                goto case Relation.LEqual;

            case Relation.LEqual:
                return new(constraint);

            case Relation.Equal:
                throw new ArgumentException("cannot standardize an equality constraint");

            default:
                throw new InvalidOperationException("invalid constraint relation");
        }
    }

    [IgnoreWarning(1370)]
    private static SimpleSolverConstraint StandardizeConstraint(SimpleConstraint constraint)
    {
        switch (constraint.relation)
        {
            case Relation.GEqual:
                constraint.variable *= -1.0;
                constraint.constant *= -1;
                goto case Relation.LEqual;

            case Relation.LEqual:
                return new SimpleSolverConstraint(constraint);

            case Relation.Equal:
                throw new ArgumentException("cannot standardize an equality constraint");

            default:
                throw new InvalidOperationException("invalid constraint relation");
        }
    }

    /// <summary>
    /// Add constraints such that <paramref name="a"/> OR <paramref name="b"/>
    /// can be true.
    /// </summary>
    /// <param name="a"></param>
    /// <param name="b"></param>
    public void AddOrConstraint(LinearConstraint a, LinearConstraint b)
    {
        var ai = a.KnownInconsistent;
        var bi = b.KnownInconsistent;

        if (bi)
        {
            AddConstraint(a);
            return;
        }
        if (ai)
        {
            AddConstraint(b);
            return;
        }

        var z = CreateVariable();
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

    public void AddOrConstraint(SimpleConstraint a, LinearConstraint b)
    {
        AddOrConstraint(new LinearConstraint(a), b);
    }
    #endregion

    #region Maximize
    [MustUseReturnValue]
    public Result<LinearSolution> Maximize(LinearEquation func)
    {
        if (!SolveBranchAndBound(func).Match(out var soln, out var err))
            return err;

        // This isn't strictly necessary but it is cheap and has proven to
        // be incredibly useful for catching bugs so I've enabled it
        // unconditionally.
        if (!CheckSolution(soln).Match(out err))
            return err;

        return soln;
    }

    [IgnoreWarning(1370)]
    [MustUseReturnValue]
    private readonly Result CheckSolution(LinearSolution soln, double tol = 1e-6)
    {
        foreach (var constraint in constraints)
        {
            double value = soln.Evaluate(constraint.variables);
            double constant = constraint.constant;

            if (value <= constant + tol)
                continue;

            if (!BurstUtil.IsBurstCompiled)
                ThrowCheckSolutionFailure(soln, constraint, value);
            return BurstError.SolutionDidNotSatisfyConstraints();
        }

        foreach (var constraint in simple)
        {
            double value = soln.Evaluate(constraint.variable);
            double constant = constraint.constant;

            if (value <= constant + tol)
                continue;

            if (!BurstUtil.IsBurstCompiled)
                ThrowCheckSolutionFailure(soln, constraint, value);
            return BurstError.SolutionDidNotSatisfyConstraints();
        }

        return Result.Ok;
    }

    [BurstDiscard]
    private static void ThrowCheckSolutionFailure(
        LinearSolution soln,
        SolverConstraint constraint,
        double value
    ) =>
        throw new Exception(
            "LP solver solution did not satisfy constraint:\n"
                + $"    constraint: {constraint} (got {value})\n"
                + $"    solution: {soln}"
        );

    [BurstDiscard]
    private static void ThrowCheckSolutionFailure(
        LinearSolution soln,
        SimpleSolverConstraint constraint,
        double value
    ) =>
        throw new Exception(
            "LP solver solution did not satisfy constraint:\n"
                + $"    constraint: {constraint} (got {value})\n"
                + $"    solution: {soln}"
        );

    #region Presolve
    [IgnoreWarning(1370)]
    [MustUseReturnValue]
    private unsafe Result Presolve(ref LinearEquation func)
    {
        var matrix = BuildPresolveMatrix(in func);

        int words = BitSet.WordsRequired(VariableCount);
        ulong* zerodata = stackalloc ulong[words];
        ConstraintState* statedata = stackalloc ConstraintState[matrix.Rows];
        var zeros = new BitSet(zerodata, words);
        var states = new RawArray<ConstraintState>(statedata, matrix.Rows);

        var result = LinearPresolve.Presolve(
            matrix,
            zeros,
            equalities.Count,
            constraints.Count + simple.Count
        );
        if (!result.Match(out var changed, out var err))
            return err;

        if (!changed)
            return Result.Ok;

        InferStates(in matrix, states.Span, equalities.Count);

        var nEqs = equalities.Count;
        var nCons = constraints.Count + simple.Count;
        var nDisj = disjunctions.Count;

        substitutions = new(VariableCount);
        equalities.Clear();
        simple.Clear();
        constraints.Clear();

        foreach (var index in zeros)
            substitutions.Add(index, new LinearEquality() { variable = index, constant = 0.0 });

        int i = 0;
        for (; i < nEqs; ++i)
        {
            if (states[i] == ConstraintState.UNSOLVABLE)
                return BurstError.Unsolvable();
            if (states[i] == ConstraintState.VACUOUS)
                continue;

            if (!AddEquality(matrix[i]).Match(out err))
                return err;
        }

        for (; i < nEqs + nCons; ++i)
        {
            if (states[i] == ConstraintState.UNSOLVABLE)
                return BurstError.Unsolvable();
            if (states[i] == ConstraintState.VACUOUS)
                continue;

            if (!AddConstraint(matrix[i]).Match(out err))
                return err;
        }

        var oldD = disjunctions;
        disjunctions = new(disjunctions.Count);
        for (int j = 0; j < nDisj; ++j, i += 2)
        {
            var lhs = matrix[i];
            var rhs = matrix[i + 1];
            var dis = oldD[j];

            var lstate = states[i];
            var rstate = states[i + 1];

            if (lstate == ConstraintState.UNSOLVABLE && rstate == ConstraintState.UNSOLVABLE)
                return BurstError.Unsolvable();

            // If any of them are vacuous then the whole constraint is unconditionally
            // satisfiable and we don't need to worry about it.
            if (lstate == ConstraintState.VACUOUS || rstate == ConstraintState.VACUOUS)
                continue;

            if (lstate == ConstraintState.UNSOLVABLE)
            {
                if (!AddConstraint(rhs).Match(out err))
                    return err;
                continue;
            }
            if (rstate == ConstraintState.UNSOLVABLE)
            {
                if (!AddConstraint(lhs).Match(out err))
                    return err;
                continue;
            }

            dis.lhs.variables.Set(lhs.Slice(0, lhs.Length - 1));
            dis.rhs.variables.Set(rhs.Slice(0, rhs.Length - 1));
            dis.lhs.constant = lhs[lhs.Length - 1];
            dis.rhs.constant = rhs[rhs.Length - 1];

            disjunctions.Add(dis);
        }

        func.Set(matrix[matrix.Rows - 1].Slice(0, matrix.Cols - 1));

        return Result.Ok;
    }

    private readonly Matrix BuildPresolveMatrix(in LinearEquation func)
    {
        var matrix = new Matrix(
            equalities.Count + simple.Count + constraints.Count + disjunctions.Count * 2 + 1,
            VariableCount + 1
        );

        int i = 0;
        foreach (var equality in equalities)
            AddPresolveMatrixRow(ref matrix, in equality.variables, equality.constant, ref i);

        foreach (var simple in simple)
        {
            var row = matrix[i++];
            row[simple.variable.Index] = simple.variable.Coef;
            row[row.Length - 1] = simple.constant;
        }

        foreach (var constraint in constraints)
            AddPresolveMatrixRow(ref matrix, in constraint.variables, constraint.constant, ref i);

        foreach (var disjunction in disjunctions)
        {
            AddPresolveMatrixRow(
                ref matrix,
                in disjunction.lhs.variables,
                disjunction.lhs.constant,
                ref i
            );
            AddPresolveMatrixRow(
                ref matrix,
                in disjunction.rhs.variables,
                disjunction.rhs.constant,
                ref i
            );
        }

        func.CopyTo(matrix[i]);

        return matrix;
    }

    private static void AddPresolveMatrixRow(
        ref Matrix matrix,
        in LinearEquation variables,
        double constant,
        ref int i
    )
    {
        var row = matrix[i++];
        variables.CopyTo(row);
        row[row.Length - 1] = constant;
    }

    [IgnoreWarning(1370)]
    [MustUseReturnValue]
    private Result AddConstraint(MemorySpan<double> row)
    {
        var coefs = row.Slice(0, row.Length - 1);
        var constant = row[row.Length - 1];
        int i = 0;
        for (; i < coefs.Length; ++i)
        {
            if (coefs[i] != 0.0)
                goto FOUND_FIRST;
        }

        if (constant < 0.0)
            return BurstError.Unsolvable();
        return Result.Ok;

        FOUND_FIRST:
        int index = i;

        for (; i < coefs.Length; ++i)
        {
            if (coefs[i] != 0.0)
                goto FOUND_SECOND;
        }

        // Only 1 variable, so we can add a simple constraint.
        simple.Add(
            new SimpleSolverConstraint()
            {
                variable = new Variable(index),
                constant = constant / coefs[index],
            }
        );
        return Result.Ok;

        FOUND_SECOND:
        constraints.Add(
            new SolverConstraint() { variables = new LinearEquation(coefs), constant = constant }
        );

        return Result.Ok;
    }

    [IgnoreWarning(1370)]
    [MustUseReturnValue]
    private Result AddEquality(MemorySpan<double> row)
    {
        var coefs = row.Slice(0, row.Length - 1);
        var constant = row[row.Length - 1];
        int i = 0;
        for (; i < coefs.Length; ++i)
        {
            if (coefs[i] != 0.0)
                goto FOUND_FIRST;
        }

        if (constant != 0.0)
            return BurstError.Unsolvable();
        return Result.Ok;

        FOUND_FIRST:
        int index = i;
        i += 1;

        for (; i < coefs.Length; ++i)
        {
            if (coefs[i] != 0.0)
                goto FOUND_SECOND;
        }

        // Only 1 variable, so we can add a simple constraint.
        substitutions.Add(
            index,
            new LinearEquality()
            {
                variable = index,
                equation = [],
                constant = constant / coefs[index],
            }
        );
        return Result.Ok;

        FOUND_SECOND:

        var eqn = new LinearEquation(VariableCount);
        for (int x = i; x < coefs.Length; ++x)
        {
            if (row[x] != 0.0)
                eqn.Sub(new Variable(x, row[x] / row[index]));
        }
        substitutions.Add(
            index,
            new LinearEquality()
            {
                variable = index,
                equation = eqn,
                constant = constant / row[index],
            }
        );

        return Result.Ok;
    }
    #endregion

    #region Branch & Bound
    [IgnoreWarning(1371)]
    [MustUseReturnValue]
    private unsafe Result<LinearSolution> SolveBranchAndBound(LinearEquation func)
    {
        TraceInitialProblem(func);

        if (!Presolve(ref func).Match(out var err))
            return err;

        TracePresolvedProblem(func);

        if (simple.Count == 0 && constraints.Count == 0 && disjunctions.Count == 0)
            return ExtractEmptySolution();

        var varMapData = stackalloc RawIntMap<int>.Entry[VariableCount];
        var binaryIndicesData = stackalloc RawIntMap<int>.Entry[VariableCount];

        var varMap = new RawIntMap<int>(varMapData, VariableCount);
        var binaryIndices = new RawIntMap<int>(binaryIndicesData, VariableCount);

        for (int i = 0; i < disjunctions.Count; ++i)
            binaryIndices.Add(disjunctions[i].variable.Index, i);

        // Do a depth-first search but order by score in order to break depth ties.
        var entries = new PriorityQueue<QueueEntry>(disjunctions.Count * 2 + 1);
        entries.Enqueue(
            new()
            {
                score = double.NegativeInfinity,
                depth = 0,
                choices = new RawArray<BinaryChoice>(disjunctions.Count),
            }
        );

        // The best score seen so far for a fully completed variable selection.
        double best = double.NegativeInfinity;
        LinearSolution? soln = null;

        while (entries.TryDequeue(out var e))
        {
            var entry = e;

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
                    entries.Enqueue(entry);
                    continue;
                }
            }

            BuildVarMap(ref varMap, entry.choices, binaryIndices);
            var tableau = BuildSimplexTableau(func, entry.choices, varMap);
            var selected = new BitSet(tableau.Cols);

            if (!Simplex.SolveTableau(tableau, selected).Match(out err))
            {
                // Some variable choices may not be solvable. That's not an
                // issue, it just means that we don't need to consider
                // any more changes here.
                if (err == Error.Unsolvable)
                    continue;

                return err;
            }

            var score = tableau[0, tableau.Cols - 1];

            LinearSolution? csoln = null;
            if (BurstUtil.SolverTrace)
            {
                csoln = ExtractTableauSolution(tableau, entry.choices, varMap, selected);
                TraceStepSolution(csoln.Value, score);
            }

            // The relaxation we got here (or solution) is not better than
            // the current incumbent solution, so we have no extra work to
            // do here.
            if (score <= best)
                continue;

            var current = csoln ?? ExtractTableauSolution(tableau, entry.choices, varMap, selected);

            // If the solution has set variables that we were going to recurse
            // on anyway then we should just set those and avoid doing extra
            // solver iterations.
            //
            // Doing this immediately also means that we will prioritize the
            // deeper iterations due to how the priority queue works.
            for (; entry.depth < disjunctions.Count; ++entry.depth)
            {
                var var = disjunctions[entry.depth].variable;
                var value = current[var];

                if (value == 0.0)
                    entry.choices[entry.depth] = BinaryChoice.Left;
                else if (value == 1.0)
                    entry.choices[entry.depth] = BinaryChoice.Right;
                else
                    break;
            }

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

                var lc = entry.choices.Clone();
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

                entries.Enqueue(right);
                entries.Enqueue(left);
            }
        }

        if (soln is null)
            return BurstError.Unsolvable();

        TraceFinalSolution(soln.Value, best);
        return soln.Value;
    }

    [BurstDiscard]
    private readonly void TraceInitialProblem(LinearEquation func)
    {
        if (BurstUtil.SolverTrace)
            LogUtil.Log($"\nMaximize Z = {func}\nsubject to\n{this}");
    }

    [BurstDiscard]
    private readonly void TracePresolvedProblem(LinearEquation func)
    {
        if (BurstUtil.SolverTrace)
            LogUtil.Log($"After presolve:\nMaximize Z = {func}\nsubject to\n{this}");
    }

    [BurstDiscard]
    private static void TraceStepSolution(LinearSolution soln, double score)
    {
        if (BurstUtil.SolverTrace)
            LogUtil.Log($"Step solution {soln} with score {score}");
    }

    [BurstDiscard]
    private static void TraceFinalSolution(LinearSolution soln, double score)
    {
        if (BurstUtil.SolverTrace)
            LogUtil.Log($"Final solution {soln} with score {score}");
    }

    [IgnoreWarning(1370)]
    private readonly Matrix BuildSimplexTableau(
        LinearEquation func,
        MemorySpan<BinaryChoice> choices,
        RawIntMap<int> varMap
    )
    {
        int constraintCount = constraints.Count + simple.Count;
        foreach (var choice in choices)
            constraintCount += choice == BinaryChoice.Unknown ? 3 : 1;

        var tableau = new Matrix(constraintCount + 1, varMap.GetCount() + constraintCount + 1);

        foreach (var var in func)
            tableau[0, varMap[var.Index]] = -var.Coef;

        int y = 1;
        foreach (var constraint in simple)
            WriteConstraintToTableau(
                tableau,
                constraint.variable,
                constraint.constant,
                varMap,
                ref y
            );

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
                    // the "big M" method. This doesn't directly give us a solution
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
                    tableau[y - 1, varMap[z.Index]] = -M * z.Coef;

                    WriteConstraintToTableau(tableau, constraint.rhs, varMap, ref y);
                    tableau[y - 1, varMap[z.Index]] = M * z.Coef;
                    tableau[y - 1, tableau.Cols - 1] += M;

                    WriteConstraintToTableau(tableau, z, 1.0, varMap, ref y);

                    break;
                default:
                    throw new NotImplementedException($"Invalid BinaryChoice value {choice}");
            }
        }

        return tableau;
    }

    private static void WriteConstraintToTableau(
        Matrix tableau,
        SolverConstraint constraint,
        RawIntMap<int> varMap,
        ref int y
    )
    {
        foreach (var var in constraint.variables)
            tableau[y, varMap[var.Index]] = var.Coef;

        tableau[y, varMap.Count + y - 1] = 1.0;
        tableau[y, tableau.Cols - 1] = constraint.constant;
        y += 1;
    }

    private static void WriteConstraintToTableau(
        Matrix tableau,
        Variable var,
        double constant,
        RawIntMap<int> varMap,
        ref int y
    )
    {
        tableau[y, varMap[var.Index]] = var.Coef;
        tableau[y, varMap.Count + y - 1] = 1.0;
        tableau[y, tableau.Cols - 1] = constant;
        y += 1;
    }

    [IgnoreWarning(1370)]
    private readonly void BuildVarMap(
        ref RawIntMap<int> varMap,
        MemorySpan<BinaryChoice> choices,
        RawIntMap<int> binaryIndices
    )
    {
        if (varMap.Capacity != VariableCount)
            throw new ArgumentException("variable count was not the right length");

        varMap.Clear();

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

            varMap.Add(i, index++);
        }
    }

    private LinearSolution ExtractTableauSolution(
        Matrix tableau,
        MemorySpan<BinaryChoice> choices,
        RawIntMap<int> varMap,
        BitSpan selected
    )
    {
        var inverse = new RawIntMap<int>(tableau.Cols);
        foreach (var (src, tgt) in varMap)
            inverse.Add(tgt, src);

        var values = new RawArray<double>(VariableCount);

        foreach (var x in selected)
        {
            // We may have selected one of the slack variables. However we
            // don't set those in the solution.
            if (x >= VariableCount)
                break;

            if (!inverse.TryGetValue(x, out var index))
                continue;

            var y = FindSetColumnValue(tableau, x);
            if (y == -1)
                continue;

            values[index] = tableau[y, tableau.Cols - 1];
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

    private readonly LinearSolution ExtractEmptySolution()
    {
        var values = new RawArray<double>(VariableCount);
        var soln = new LinearSolution(values);

        foreach (var (index, sub) in substitutions)
            values[index] = soln.Evaluate(sub.equation) + sub.constant;

        return soln;
    }

    private static int FindSetColumnValue(Matrix tableau, int column)
    {
        int row = -1;

        for (int i = 0; i < tableau.Rows; ++i)
        {
            var elem = tableau[i, column];
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
    #endregion
    #endregion

    #region ToString
    public readonly override string ToString()
    {
        StringBuilder builder = new();
        foreach (var equality in equalities)
        {
            builder.Append(equality.ToRelationString("=="));
            builder.Append('\n');
        }

        foreach (var simple in simple)
        {
            builder.Append(simple);
            builder.Append('\n');
        }

        foreach (var constraint in constraints)
        {
            builder.Append(constraint.ToRelationString("<="));
            builder.Append('\n');
        }

        foreach (var or in disjunctions)
        {
            builder.Append('(');
            builder.Append(or.lhs.ToRelationString("<="));
            builder.Append(" || ");
            builder.Append(or.rhs.ToRelationString("<="));
            builder.Append(")\n");
        }

        foreach (var sub in substitutions.Values)
        {
            builder.Append("sub ");
            builder.Append(sub);
            builder.AppendLine();
        }

        return builder.ToString();
    }

    private readonly string RenderChoices(BinaryChoice[] choices)
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

    internal static void RenderCoef(StringBuilder builder, double coef, string var, ref bool first)
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

            builder.Append('*');
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
    #endregion

    #region Internal Types
    private struct SolverConstraint : IComparable<SolverConstraint>
    {
        public LinearEquation variables;
        public double constant;

        public SolverConstraint()
        {
            variables = [];
            constant = 0.0;
        }

        public SolverConstraint(LinearConstraint constraint)
        {
            variables = constraint.variables;
            constant = constraint.constant;
        }

        public int CompareTo(SolverConstraint other)
        {
            int cmp = variables.CompareTo(other.variables);
            if (cmp != 0)
                return cmp;
            return constant.CompareTo(other.constant);
        }

        public override readonly string ToString() => $"{variables} <= {constant}";

        public readonly string ToRelationString(string relation) =>
            $"{variables} {relation} {constant}";
    }

    private struct SimpleSolverConstraint()
    {
        public Variable variable;
        public double constant;

        public SimpleSolverConstraint(SimpleConstraint constraint)
            : this()
        {
            variable = constraint.variable;
            constant = constraint.constant;
        }

        public override readonly string ToString() => $"{variable} <= {constant}";
    }

    // var == equation + constant
    private struct LinearEquality()
    {
        public int variable = -1;
        public LinearEquation equation = [];
        public double constant = double.NaN;

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

    private struct OrConstraint
    {
        // The decision variable to use when computing the linear relaxation
        // of the problem.
        public Variable variable;

        public SolverConstraint lhs;
        public SolverConstraint rhs;
    }

    enum BinaryChoice : byte
    {
        Unknown = 0,
        Left,
        Right,
    }

    private struct QueueEntry : IComparable<QueueEntry>
    {
        public double score;
        public int depth;
        public RawArray<BinaryChoice> choices;

        public readonly int CompareTo(QueueEntry entry)
        {
            int cmp = depth.CompareTo(entry.depth);
            if (cmp != 0)
                return cmp;

            return score.CompareTo(entry.score);
        }
    }
    #endregion
}
