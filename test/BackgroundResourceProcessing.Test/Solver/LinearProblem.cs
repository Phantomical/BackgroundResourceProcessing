extern alias BRPMemory;

using BackgroundResourceProcessing.BurstSolver;
using BackgroundResourceProcessing.Collections.Burst;
using BRPMemory.System;
using KSP.Testing;

namespace BackgroundResourceProcessing.Test.Solver;

public sealed class LinearProblemTest : BRPTestBase
{
    // This tests the textbook example from [0]. Which includes the
    // solution so we can easily verify that things are working as
    // expected (along with individual steps).
    //
    // [0]: https://math.libretexts.org/Bookshelves/Applied_Mathematics/Applied_Finite_Mathematics_(Sekhon_and_Bloom)/04%3A_Linear_Programming_The_Simplex_Method/4.02%3A_Maximization_By_The_Simplex_Method
    [TestInfo("LinearProblemTest_TestTextbookExample")]
    public void TestTextbookExample()
    {
        LinearProblem problem = new();
        var x1 = problem.CreateVariable();
        var x2 = problem.CreateVariable();

        problem.AddConstraint(new LinearEquation() { x1, x2 } <= 12.0).Unwrap();
        problem.AddConstraint(new LinearEquation() { 2.0 * x1, x2 } <= 16.0).Unwrap();

        var soln = problem.Maximize([40.0 * x1, 30.0 * x2]).Unwrap();

        Assert.AreEqual(4.0, soln[x1]);
        Assert.AreEqual(8.0, soln[x2]);
    }

    // This tests the example from
    // https://optimization.cbe.cornell.edu/index.php?title=Simplex_algorithm
    [TestInfo("LinearProblemTest_TestCornellExample")]
    public void TestCornellExample()
    {
        LinearProblem problem = new();
        var x1 = problem.CreateVariable();
        var x2 = problem.CreateVariable();
        var x3 = problem.CreateVariable();

        problem.AddConstraint(new LinearEquation() { 2.0 * x1, x2, x3 } <= 2.0).Unwrap();
        problem.AddConstraint(new LinearEquation() { x1, 2.0 * x2, 3.0 * x3 } <= 4.0).Unwrap();
        problem.AddConstraint(new LinearEquation() { 2.0 * x1, 2.0 * x2, x3 } <= 8.0).Unwrap();

        var soln = problem.Maximize([4.0 * x1, x2, 4.0 * x3]).Unwrap();

        Assert.AreEqual(0.4, soln[x1], 1e-6);
        Assert.AreEqual(0.0, soln[x2], 1e-6);
        Assert.AreEqual(1.2, soln[x3], 1e-6);
    }

    // This tests the example from
    // https://en.wikipedia.org/wiki/Simplex_algorithm#Implementation
    //
    // It has been tweaked so as to maximize the objective function instead
    // minimize.
    [TestInfo("LinearProblemTest_TestWikipediaExample")]
    public void TestWikipediaExample()
    {
        LinearProblem problem = new();
        var x = problem.CreateVariable();
        var y = problem.CreateVariable();
        var z = problem.CreateVariable();

        problem.AddConstraint(new LinearEquation() { 3 * x, 2 * y, 1 * z } <= 10).Unwrap();
        problem.AddConstraint(new LinearEquation() { 2 * x, 5 * y, 3 * z } <= 15).Unwrap();

        var soln = problem.Maximize([2 * x, 3 * y, 4 * z]).Unwrap();

        Assert.AreEqual(0.0, soln[x]);
        Assert.AreEqual(0.0, soln[y]);
        Assert.AreEqual(5.0, soln[z], 1e-6);
    }

    // This is derived from an actual test case that ended up returning NaNs
    // when optimizing.
    [TestInfo("LinearProblemTest_RegressionNan1")]
    public void RegressionNan1()
    {
        LinearProblem problem = new();
        var x1 = problem.CreateVariable();
        var x2 = problem.CreateVariable();
        var x3 = problem.CreateVariable();
        var x4 = problem.CreateVariable();

        problem.AddConstraint(x1 <= 1).Unwrap();
        problem.AddConstraint(x2 <= 1).Unwrap();
        problem.AddConstraint(x3 <= 1).Unwrap();
        problem.AddConstraint(x4 <= 1).Unwrap();

        problem
            .AddConstraint(new LinearEquation() { -37.46 * x1, 54 * x3, -9.375 * x4 } <= 0.0)
            .Unwrap();

        var soln = problem.Maximize([x1, 7.96 * x2, 3 * x3, x4]).Unwrap();

        Assert.AreEqual(1, soln[x1], 1e-6);
        Assert.AreEqual(1, soln[x2], 1e-6);
        Assert.AreEqual(9367.0 / 10800.0, soln[x3], 1e-6);
        Assert.AreEqual(1, soln[x4], 1e-6);
    }

    [TestInfo("LinearProblemTest_OrConstraint1")]
    public void OrConstraint1()
    {
        LinearProblem problem = new();
        var x0 = problem.CreateVariable();
        var x1 = problem.CreateVariable();
        var x2 = problem.CreateVariable();

        problem.AddConstraint(x0 <= 1).Unwrap();
        problem.AddConstraint(x1 <= 1).Unwrap();
        problem.AddConstraint(x2 <= 1).Unwrap();

        problem
            .AddOrConstraint(
                new LinearEquation() { x2, x0 } <= 0.2,
                new LinearEquation() { x2, x1 } >= 0.8
            )
            .Unwrap();

        var soln = problem.Maximize([x0, x1, -x2]);

        LogUtil.Log($"{soln}");
    }

    [TestInfo("LinearProblemTest_InferConstantsBasic")]
    public void InferConstantsBasic()
    {
        LinearProblem problem = new();
        var x0 = problem.CreateVariable();
        var x1 = problem.CreateVariable();
        var x2 = problem.CreateVariable();

        problem.AddConstraint(x0 <= 1).Unwrap();
        problem.AddConstraint(x1 <= 1).Unwrap();
        problem.AddConstraint(x2 <= 1).Unwrap();

        var soln = problem.Maximize([x0, x1, -x2]).Unwrap();

        Assert.AreEqual(1.0, soln[x0]);
        Assert.AreEqual(1.0, soln[x1]);
        Assert.AreEqual(0.0, soln[x2]);
    }

    [TestInfo("LinearProblemTest_InferConstantsAvx2")]
    public unsafe void InferConstantsAvx2()
    {
        LinearProblem problem = new();
        Variable* vptr = stackalloc Variable[25];
        MemorySpan<Variable> variables = new(vptr, 25);

        for (int i = 0; i < 25; ++i)
        {
            var variable = problem.CreateVariable();
            variables[i] = variable;
            problem.AddConstraint(variable <= 1.0).Unwrap();
        }

        LinearEquation constraint = [variables[5], variables[7], -variables[22]];
        problem.AddConstraint(constraint <= 0.2).Unwrap();

        var extra = problem.CreateVariable();
        constraint = [variables[5], extra];
        problem.AddConstraint(constraint <= 1.0).Unwrap();

        var soln = problem.Maximize([.. variables, -2.0 * variables[2], 5.0 * extra]).Unwrap();

        Assert.AreEqual(1.0, soln[0]);
        Assert.AreEqual(1.0, soln[1]);
        Assert.AreEqual(0.0, soln[2]);
        Assert.AreEqual(1.0, soln[extra]);
    }

    [TestInfo("LinearProblemTest_TestLargeCoefficient")]
    public void TestLargeCoefficient()
    {
        LinearProblem problem = new();
        var x0 = problem.CreateVariable();
        var x1 = problem.CreateVariable();

        problem.AddConstraint(x0 <= 1).Unwrap();
        problem.AddConstraint(x1 <= 1).Unwrap();
        problem.AddConstraint(new LinearEquation() { 49.2 * x0 } <= 0.0).Unwrap();

        var soln = problem.Maximize([3e6 * x0, 8.0 * x1]).Unwrap();

        Assert.AreEqual(0.0, soln[x0], 1e-6);
        Assert.AreEqual(1.0, soln[x1], 1e-6);
    }

    /// <summary>
    /// A <c>&gt;=</c> constraint with a negative coefficient and a negative
    /// constant is an ordinary upper bound in disguise: <c>-x0 &gt;= -5</c> is
    /// exactly <c>x0 &lt;= 5</c>.
    /// </summary>
    ///
    /// <remarks>
    /// <c>InferState</c> classifies <c>&gt;=</c> rows by flipping the
    /// all-positive/all-negative flags to model the coefficient negation, but
    /// leaves the constant alone, so it compares against <c>b</c> where
    /// <c>-b</c> is required. That makes this constraint look like
    /// <c>x0 &lt;= -5</c>, which no non-negative variable can satisfy, and the
    /// whole problem is rejected as unsolvable before the solver ever runs.
    /// </remarks>
    [TestInfo("LinearProblemTest_GEqualNegativeCoefficient")]
    public void GEqualNegativeCoefficient()
    {
        LinearProblem problem = new();
        var x0 = problem.CreateVariable();

        problem.AddConstraint(new LinearEquation() { -1.0 * x0 } >= -5.0).Unwrap();

        var soln = problem.Maximize([1.0 * x0]).Unwrap();

        Assert.AreEqual(5.0, soln[x0], 1e-6);
    }

    /// <summary>
    /// Presolve can reduce a multi-variable row down to a single coefficient,
    /// and re-adds it as a simple bound by dividing through by that
    /// coefficient. Dividing an inequality by a negative number reverses it, so
    /// a row that says <c>x0 &gt;= 2</c> must not come back as <c>x0 &lt;= 2</c>.
    /// </summary>
    ///
    /// <remarks>
    /// The first constraint is an all-positive row with a zero constant, which
    /// is how presolve learns that <c>x1</c> is pinned to 0. Zeroing that
    /// column collapses the third row from <c>-x0 + x1 &lt;= -2</c> down to
    /// <c>-x0 &lt;= -2</c>, i.e. a lower bound of 2 on <c>x0</c>, which is the
    /// single-negative-coefficient row the re-add path mishandles. Turning that
    /// lower bound into an upper bound caps the objective at 2 instead of
    /// letting it reach the real optimum of 10.
    ///
    /// This went unnoticed because the branch was unreachable: the scan for a
    /// second nonzero coefficient did not advance past the first one, so it
    /// always re-found it and every row took the multi-variable path instead.
    /// Both halves have to be fixed for this test to mean anything.
    /// </remarks>
    [TestInfo("LinearProblemTest_PresolveSingleNegativeCoefficient")]
    public void PresolveSingleNegativeCoefficient()
    {
        LinearProblem problem = new();
        var x0 = problem.CreateVariable();
        var x1 = problem.CreateVariable();

        problem.AddConstraint(x1 <= 0.0).Unwrap();
        problem.AddConstraint(x0 <= 10.0).Unwrap();
        problem.AddConstraint(new LinearEquation() { -1.0 * x0, 1.0 * x1 } <= -2.0).Unwrap();

        var soln = problem.Maximize([1.0 * x0]).Unwrap();

        Assert.AreEqual(10.0, soln[x0], 1e-6);
    }

    /// <summary>
    /// Beale's classic cycling example. Dantzig's entering rule with the lowest
    /// row index breaking ratio-test ties revisits the same degenerate vertex
    /// every six pivots and never terminates on its own.
    /// </summary>
    ///
    /// <remarks>
    /// The pivot loop can only leave early by reaching optimality or detecting
    /// unboundedness; running out of iterations falls through to an
    /// unconditional success. Because every pivot preserves feasibility, a
    /// cycling problem therefore reports a perfectly valid-looking but
    /// suboptimal answer with no diagnostics at all — here the solver stays
    /// stuck at the origin (objective 0) instead of finding the optimum of 0.05.
    ///
    /// All three rows are added as <see cref="LinearEquation"/> constraints
    /// rather than simple bounds so that they keep their relative order in the
    /// tableau; simple bounds are emitted first, which would perturb the
    /// tie-breaking the cycle depends on.
    /// </remarks>
    [TestInfo("LinearProblemTest_BealeCyclingExample")]
    public void BealeCyclingExample()
    {
        LinearProblem problem = new();
        var x1 = problem.CreateVariable();
        var x2 = problem.CreateVariable();
        var x3 = problem.CreateVariable();
        var x4 = problem.CreateVariable();

        problem
            .AddConstraint(
                new LinearEquation() { 0.25 * x1, -60.0 * x2, -0.04 * x3, 9.0 * x4 } <= 0.0
            )
            .Unwrap();
        problem
            .AddConstraint(
                new LinearEquation() { 0.5 * x1, -90.0 * x2, -0.02 * x3, 3.0 * x4 } <= 0.0
            )
            .Unwrap();
        problem.AddConstraint(new LinearEquation() { 1.0 * x3 } <= 1.0).Unwrap();

        var soln = problem.Maximize([0.75 * x1, -150.0 * x2, 0.02 * x3, -6.0 * x4]).Unwrap();

        Assert.AreEqual(0.04, soln[x1], 1e-6);
        Assert.AreEqual(0.0, soln[x2], 1e-6);
        Assert.AreEqual(1.0, soln[x3], 1e-6);
        Assert.AreEqual(0.0, soln[x4], 1e-6);
    }
}
