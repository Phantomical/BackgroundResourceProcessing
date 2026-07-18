using BackgroundResourceProcessing.BurstSolver;
using KSP.Testing;

namespace BackgroundResourceProcessing.Test.BurstSolver;

/// <summary>
/// Tests for <c>LinearPresolve.InferState</c>, the classifier that decides
/// whether a constraint is VALID (keep it), VACUOUS (drop it), or UNSOLVABLE
/// (give up on the whole problem).
/// </summary>
///
/// <remarks>
/// Every solver variable is non-negative, which is what makes the signs of a
/// row's coefficients enough to classify it on their own. For <c>c*x &lt;= b</c>
/// with all coefficients positive the left-hand side is at least 0, so the row
/// is unsatisfiable exactly when <c>b &lt; 0</c>; with all coefficients negative
/// the left-hand side is at most 0, so the row is vacuous exactly when
/// <c>b &gt;= 0</c>.
///
/// A <c>&gt;=</c> row is classified by pretending its coefficients have already
/// been negated, which is how <c>StandardizeConstraint</c> will eventually
/// rewrite it, and that swaps the all-positive/all-negative flags. Negating the
/// coefficients negates the constant as well, so every comparison in that path
/// has to be made against <c>-b</c>. These tests pin that down: with a zero
/// constant <c>b</c> and <c>-b</c> agree, which is why the bug stayed latent
/// (<c>Solver.cs</c> only ever emits <c>&gt;= 0</c>), so the interesting cases
/// are all the ones with a nonzero constant.
/// </remarks>
public sealed class InferStateTest : BRPTestBase
{
    private static ConstraintState State(double coef, Relation relation, double constant) =>
        new SimpleConstraint(new Variable(0, coef), relation, constant).GetState();

    /// <summary>
    /// <c>&gt;=</c> rows with a positive coefficient. These are genuine lower
    /// bounds and must not be discarded.
    /// </summary>
    [TestInfo("InferStateTest_TestGEqualPositiveCoefficient")]
    public void TestGEqualPositiveCoefficient()
    {
        // x >= 3 is a real lower bound: x = 0 violates it.
        Assert.AreEqual(ConstraintState.VALID, State(1.0, Relation.GEqual, 3.0));

        // x >= -3 is implied by x >= 0 and carries no information.
        Assert.AreEqual(ConstraintState.VACUOUS, State(1.0, Relation.GEqual, -3.0));
    }

    /// <summary>
    /// <c>&gt;=</c> rows with a negative coefficient. Misclassifying these is
    /// worse than dropping a constraint: <c>-x &gt;= -5</c> is just
    /// <c>x &lt;= 5</c>, and reporting it as UNSOLVABLE aborts the whole solve.
    /// </summary>
    [TestInfo("InferStateTest_TestGEqualNegativeCoefficient")]
    public void TestGEqualNegativeCoefficient()
    {
        // -x >= -5 is x <= 5, which x = 0 satisfies and x = 6 does not.
        Assert.AreEqual(ConstraintState.VALID, State(-1.0, Relation.GEqual, -5.0));

        // -x >= 5 is x <= -5, which no non-negative x satisfies.
        Assert.AreEqual(ConstraintState.UNSOLVABLE, State(-1.0, Relation.GEqual, 5.0));
    }

    /// <summary>
    /// The zero-constant cases, which are the only ones <c>Solver.cs</c>
    /// currently emits. They are classified correctly either way; this pins
    /// them down so the fix cannot regress them.
    /// </summary>
    [TestInfo("InferStateTest_TestGEqualZeroConstant")]
    public void TestGEqualZeroConstant()
    {
        // x >= 0 is implied by variable non-negativity.
        Assert.AreEqual(ConstraintState.VACUOUS, State(1.0, Relation.GEqual, 0.0));

        // -x >= 0 pins x to exactly 0, which is a real restriction.
        Assert.AreEqual(ConstraintState.VALID, State(-1.0, Relation.GEqual, 0.0));
    }

    /// <summary>
    /// The <c>&lt;=</c> path, which has always been correct, as a control.
    /// </summary>
    [TestInfo("InferStateTest_TestLEqual")]
    public void TestLEqual()
    {
        Assert.AreEqual(ConstraintState.VALID, State(1.0, Relation.LEqual, 3.0));
        Assert.AreEqual(ConstraintState.UNSOLVABLE, State(1.0, Relation.LEqual, -3.0));
        Assert.AreEqual(ConstraintState.VACUOUS, State(-1.0, Relation.LEqual, 5.0));
        Assert.AreEqual(ConstraintState.VALID, State(-1.0, Relation.LEqual, -5.0));
    }
}
