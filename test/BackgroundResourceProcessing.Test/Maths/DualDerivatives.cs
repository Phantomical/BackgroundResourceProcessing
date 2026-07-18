using BackgroundResourceProcessing.Maths;
using KSP.Testing;

namespace BackgroundResourceProcessing.Test.Maths;

/// <summary>
/// Derivative-correctness tests for the <see cref="Dual"/> and <see cref="Dual2"/>
/// arithmetic operators, checked against hand-derived analytic derivatives.
/// </summary>
///
/// <remarks>
/// Regression tests for findings D1, M1, D2, and D3 of the 2026-07-17 broad
/// codebase review (<c>.claude/docs/2026-07-17-broad-codebase-review.md</c>):
///
/// <list type="bullet">
/// <item>D1: <c>Dual2 / Dual2</c> added the <c>q*b''</c> term of the quotient
/// second derivative instead of subtracting it.</item>
/// <item>M1: <c>double - Dual2</c> passed the subtrahend's derivatives through
/// without negating them.</item>
/// <item>D2: <c>double / Dual</c> computed <c>-g'/g</c> instead of
/// <c>-c*g'/g^2</c>, dropping the numerator entirely.</item>
/// <item>D3: <c>Atan2</c> negated the true derivative in both <see cref="Dual"/>
/// and <see cref="Dual2"/>.</item>
/// </list>
///
/// These poison the second-order Newton steps and curvature classification used
/// by the shadow terminator solvers, so each operator is pinned to the analytic
/// derivative here.
/// </remarks>
public class DualDerivativeTests : BRPTestBase
{
    const double Eps = 1e-9;

    /// <summary>
    /// D1: for q = a/b the second derivative is q'' = (a'' - 2q'b' - q*b'')/b.
    /// Checked on tan(t) = sin(t)/cos(t), whose derivatives are known in closed
    /// form: tan' = sec^2, tan'' = 2*tan*sec^2.
    /// </summary>
    [TestInfo("DualDerivativeTests_Dual2DivisionSecondDerivativeTan")]
    public void Dual2DivisionSecondDerivativeTan()
    {
        const double t = 0.5;
        var v = Dual2.Variable(t);
        var q = Dual2.Sin(v) / Dual2.Cos(v);

        double tan = Math.Tan(t);
        double sec2 = 1.0 / (Math.Cos(t) * Math.Cos(t));

        Assert.AreEqual(tan, q.x, Eps, "tan value");
        Assert.AreEqual(sec2, q.dx, Eps, "tan first derivative");
        Assert.AreEqual(2.0 * tan * sec2, q.ddx, Eps, "tan second derivative");
    }

    /// <summary>
    /// D1: 1/t^2 via the <c>Dual2 / Dual2</c> operator. At t=1 the correct
    /// derivatives are f' = -2/t^3 = -2 and f'' = 6/t^4 = 6; the sign error on
    /// the q*b'' term instead yields f'' = 10. The sibling
    /// <c>double / Dual2</c> operator is correct and is checked alongside so the
    /// two paths cannot silently diverge again.
    /// </summary>
    [TestInfo("DualDerivativeTests_Dual2DivisionSecondDerivativeReciprocal")]
    public void Dual2DivisionSecondDerivativeReciprocal()
    {
        var v = Dual2.Variable(1.0);
        var b = v * v; // t^2 = (1, 2, 2)

        Assert.AreEqual(1.0, b.x, Eps);
        Assert.AreEqual(2.0, b.dx, Eps);
        Assert.AreEqual(2.0, b.ddx, Eps);

        var div = new Dual2(1.0) / b;
        Assert.AreEqual(1.0, div.x, Eps, "1/t^2 value (Dual2/Dual2)");
        Assert.AreEqual(-2.0, div.dx, Eps, "1/t^2 first derivative (Dual2/Dual2)");
        Assert.AreEqual(6.0, div.ddx, Eps, "1/t^2 second derivative (Dual2/Dual2)");

        var recip = 1.0 / b;
        Assert.AreEqual(1.0, recip.x, Eps, "1/t^2 value (double/Dual2)");
        Assert.AreEqual(-2.0, recip.dx, Eps, "1/t^2 first derivative (double/Dual2)");
        Assert.AreEqual(6.0, recip.ddx, Eps, "1/t^2 second derivative (double/Dual2)");
    }

    /// <summary>
    /// M1: c - b(t) has derivatives -b' and -b''. With b = t^2 at t=3
    /// (b = (9, 6, 2)), 2 - b must be (-7, -6, -2), and must agree with the
    /// equivalent -(b - 2).
    /// </summary>
    [TestInfo("DualDerivativeTests_Dual2DoubleMinusDual2NegatesDerivatives")]
    public void Dual2DoubleMinusDual2NegatesDerivatives()
    {
        var v = Dual2.Variable(3.0);
        var b = v * v; // t^2 = (9, 6, 2)

        var r = 2.0 - b;
        Assert.AreEqual(-7.0, r.x, Eps, "2 - t^2 value");
        Assert.AreEqual(-6.0, r.dx, Eps, "2 - t^2 first derivative");
        Assert.AreEqual(-2.0, r.ddx, Eps, "2 - t^2 second derivative");

        var neg = -(b - 2.0);
        Assert.AreEqual(neg.x, r.x, Eps, "2 - b must equal -(b - 2): value");
        Assert.AreEqual(neg.dx, r.dx, Eps, "2 - b must equal -(b - 2): dx");
        Assert.AreEqual(neg.ddx, r.ddx, Eps, "2 - b must equal -(b - 2): ddx");
    }

    /// <summary>
    /// D2: d/dt (c/g) = -c*g'/g^2. For 3/t at t=2 that is -3/4; the buggy
    /// operator computed -g'/g = -1/2 independent of the numerator. Must also
    /// agree with the (correct) <c>Dual / Dual</c> path.
    /// </summary>
    [TestInfo("DualDerivativeTests_DualDoubleDividedByDual")]
    public void DualDoubleDividedByDual()
    {
        var g = Dual.Variable(2.0);

        var r = 3.0 / g;
        Assert.AreEqual(1.5, r.x, Eps, "3/t value");
        Assert.AreEqual(-0.75, r.dx, Eps, "3/t derivative");

        var full = new Dual(3.0) / g;
        Assert.AreEqual(full.x, r.x, Eps, "double/Dual must match Dual/Dual: value");
        Assert.AreEqual(full.dx, r.dx, Eps, "double/Dual must match Dual/Dual: dx");
    }

    /// <summary>
    /// D3: d/dt atan2(y, x) = (x*y' - y*x')/(x^2 + y^2). On the unit circle
    /// atan2(sin t, cos t) = t, so the derivative is exactly 1; the sign error
    /// yields -1.
    /// </summary>
    [TestInfo("DualDerivativeTests_DualAtan2Derivative")]
    public void DualAtan2Derivative()
    {
        const double t = 0.7;
        var v = Dual.Variable(t);
        var r = Dual.Atan2(Dual.Sin(v), Dual.Cos(v));

        Assert.AreEqual(t, r.x, Eps, "atan2(sin t, cos t) value");
        Assert.AreEqual(1.0, r.dx, Eps, "atan2(sin t, cos t) derivative");

        // atan2(t, 1) = atan(t): derivative 1/(1 + t^2) = 1/2 at t=1.
        var atan = Dual.Atan2(Dual.Variable(1.0), new Dual(1.0));
        Assert.AreEqual(Math.PI / 4.0, atan.x, Eps, "atan(t) value");
        Assert.AreEqual(0.5, atan.dx, Eps, "atan(t) derivative");
    }

    /// <summary>
    /// D3: the same identities for the second-order overload.
    /// atan2(sin t, cos t) = t has derivatives (1, 0), and atan(t) at t=1 has
    /// derivatives (1/2, -1/2).
    /// </summary>
    [TestInfo("DualDerivativeTests_Dual2Atan2Derivatives")]
    public void Dual2Atan2Derivatives()
    {
        const double t = 0.7;
        var v = Dual2.Variable(t);
        var r = Dual2.Atan2(Dual2.Sin(v), Dual2.Cos(v));

        Assert.AreEqual(t, r.x, Eps, "atan2(sin t, cos t) value");
        Assert.AreEqual(1.0, r.dx, Eps, "atan2(sin t, cos t) first derivative");
        Assert.AreEqual(0.0, r.ddx, Eps, "atan2(sin t, cos t) second derivative");

        // atan2(t, 1) = atan(t): derivatives 1/(1 + t^2) and -2t/(1 + t^2)^2,
        // i.e. (1/2, -1/2) at t=1.
        var atan = Dual2.Atan2(Dual2.Variable(1.0), new Dual2(1.0));
        Assert.AreEqual(Math.PI / 4.0, atan.x, Eps, "atan(t) value");
        Assert.AreEqual(0.5, atan.dx, Eps, "atan(t) first derivative");
        Assert.AreEqual(-0.5, atan.ddx, Eps, "atan(t) second derivative");
    }
}
