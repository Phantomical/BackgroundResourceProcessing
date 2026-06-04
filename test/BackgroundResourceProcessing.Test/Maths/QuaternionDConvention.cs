using BackgroundResourceProcessing.Maths;
using KSP.Testing;

namespace BackgroundResourceProcessing.Test.Maths;

/// <summary>
/// Regression tests that pin <see cref="QuaternionD"/> against
/// <see cref="UnityEngine.Quaternion"/> and validate its helper methods.
///
/// These exist to lock down two recent bug fixes:
///   * The rotation convention now matches Unity (right-handed, q*v*q^-1), so
///     <c>FromAngleAxis(angleRad, axis).Rotate(v)</c> equals
///     <c>UnityEngine.Quaternion.AngleAxis(angleDeg, axis) * v</c>.
///   * <see cref="QuaternionD.Normalized"/> divides by Norm() (sqrt of the
///     squared norm) rather than by SqrNorm().
/// </summary>
public class QuaternionDConventionTests : BRPTestBase
{
    // Tolerance for comparisons against Unity. UnityEngine.Quaternion/Vector3
    // are single precision, so a round-trip through them only preserves ~6-7
    // significant decimal digits. 1e-4 leaves comfortable headroom over the
    // accumulated float error while still being tight enough to catch a sign
    // flip or axis swap.
    private const double UnityTol = 1e-4;

    // Tolerance for pure-double computations (no Unity round-trip). These are
    // limited only by accumulated floating point error over a handful of
    // multiply/add operations, so 1e-9 is appropriate.
    private const double DoubleTol = 1e-9;

    /// <summary>
    /// Component-wise comparison of two double-precision vectors with a
    /// descriptive failure message.
    /// </summary>
    private static void AssertVectorEqual(
        Vector3d expected,
        Vector3d actual,
        double tol,
        string message
    )
    {
        Assert.AreEqual(expected.x, actual.x, tol, $"{message} (x component)");
        Assert.AreEqual(expected.y, actual.y, tol, $"{message} (y component)");
        Assert.AreEqual(expected.z, actual.z, tol, $"{message} (z component)");
    }

    private static Vector3d FromUnity(UnityEngine.Vector3 v)
    {
        return new Vector3d(v.x, v.y, v.z);
    }

    private static UnityEngine.Vector3 ToUnity(Vector3d v)
    {
        return new UnityEngine.Vector3((float)v.x, (float)v.y, (float)v.z);
    }

    [TestInfo("QuaternionDConventionTests_MatchesUnityAngleAxis")]
    public void MatchesUnityAngleAxis()
    {
        var invsqrt3 = 1.0 / Math.Sqrt(3);
        var invsqrt2 = 1.0 / Math.Sqrt(2);

        Vector3d[] axes =
        [
            new(1, 0, 0),
            new(0, 1, 0),
            new(0, 0, 1),
            new(invsqrt3, invsqrt3, invsqrt3),
            new(invsqrt2, invsqrt2, 0),
            new Vector3d(2, -3, 6).normalized,
            new Vector3d(-1, 4, -2).normalized,
        ];

        double[] degrees = [30.0, 90.0, 135.0, 200.0, -60.0];

        Vector3d[] vectors =
        [
            new(1, 0, 0),
            new(0, 1, 0),
            new(0, 0, 1),
            new(3, -2, 5),
            new(-4, 1, 2),
        ];

        foreach (var axis in axes)
        {
            var unityAxis = ToUnity(axis);

            foreach (var deg in degrees)
            {
                var rad = deg * Math.PI / 180.0;
                var q = QuaternionD.FromAngleAxis(rad, axis);
                var uq = UnityEngine.Quaternion.AngleAxis((float)deg, unityAxis);

                foreach (var v in vectors)
                {
                    var actual = q.Rotate(v);
                    var expected = FromUnity(uq * ToUnity(v));

                    AssertVectorEqual(
                        expected,
                        actual,
                        UnityTol,
                        $"FromAngleAxis({deg}deg, {axis}).Rotate({v}) should match Unity"
                    );
                }
            }
        }
    }

    [TestInfo("QuaternionDConventionTests_NormalizedNonUnit")]
    public void NormalizedNonUnit()
    {
        // Norm = sqrt(2^2 + 4^2) = sqrt(20). If Normalized() incorrectly divided
        // by SqrNorm() (== 20) the result would have norm sqrt(20)/20 != 1.
        var q = new QuaternionD(2, new Vector3d(0, 4, 0));
        var normalized = q.Normalized();

        Assert.AreEqual(
            1.0,
            normalized.Norm(),
            1e-12,
            "Normalized() of a non-unit quaternion must have unit norm"
        );

        // Direction must be preserved: the ratio of components is unchanged.
        // r/v.y was 2/4 = 0.5 before normalization.
        Assert.AreEqual(
            2.0 / 4.0,
            normalized.r / normalized.v.y,
            1e-12,
            "Normalized() must preserve the quaternion direction"
        );

        // The other vector components were zero and must stay zero.
        Assert.AreEqual(0.0, normalized.v.x, 1e-12, "x component must stay zero");
        Assert.AreEqual(0.0, normalized.v.z, 1e-12, "z component must stay zero");
    }

    [TestInfo("QuaternionDConventionTests_NormalizedUnitIsIdentity")]
    public void NormalizedUnitIsIdentity()
    {
        // Normalizing an already-unit quaternion should be (very nearly) a no-op.
        var q = QuaternionD.FromAngleAxis(0.7, new Vector3d(1, 2, 3).normalized);
        var n = q.Normalized();

        Assert.AreEqual(q.r, n.r, DoubleTol, "r unchanged when normalizing a unit quaternion");
        AssertVectorEqual(
            q.v,
            n.v,
            DoubleTol,
            "vector part unchanged when normalizing a unit quaternion"
        );
        Assert.AreEqual(1.0, n.Norm(), DoubleTol, "result remains unit norm");
    }

    [TestInfo("QuaternionDConventionTests_InverseRoundTrip")]
    public void InverseRoundTrip()
    {
        var axis = new Vector3d(1, -2, 0.5).normalized;
        var q = QuaternionD.FromAngleAxis(1.3, axis);
        var inv = q.Inverse();

        Vector3d[] vectors =
        [
            new(1, 0, 0),
            new(0, 1, 0),
            new(0, 0, 1),
            new(3, -2, 5),
            new(-4, 1, 2),
        ];

        foreach (var v in vectors)
        {
            AssertVectorEqual(
                v,
                q.Rotate(inv.Rotate(v)),
                DoubleTol,
                $"q.Rotate(q.Inverse().Rotate({v})) should round-trip to v"
            );
            AssertVectorEqual(
                v,
                inv.Rotate(q.Rotate(v)),
                DoubleTol,
                $"q.Inverse().Rotate(q.Rotate({v})) should round-trip to v"
            );
        }
    }

    [TestInfo("QuaternionDConventionTests_ConjugateMatchesInverseForUnit")]
    public void ConjugateMatchesInverseForUnit()
    {
        // For a unit quaternion the conjugate equals the inverse, so they must
        // rotate any vector identically.
        var axis = new Vector3d(2, 1, -3).normalized;
        var q = QuaternionD.FromAngleAxis(2.1, axis);
        var conj = q.Conjugate();
        var inv = q.Inverse();

        Vector3d[] vectors = [new(1, 0, 0), new(0, 1, 0), new(0, 0, 1), new(3, -2, 5)];

        foreach (var v in vectors)
        {
            AssertVectorEqual(
                inv.Rotate(v),
                conj.Rotate(v),
                DoubleTol,
                $"Conjugate and Inverse must rotate {v} identically for a unit quaternion"
            );
        }
    }

    [TestInfo("QuaternionDConventionTests_ToBasisFromBasisRoundTrip")]
    public void ToBasisFromBasisRoundTrip()
    {
        var s = 1.0 / Math.Sqrt(2);

        // An orthonormal basis: the standard basis rotated 45 degrees about +Y.
        Vector3d bx = new(s, 0, -s);
        Vector3d by = new(0, 1, 0);
        Vector3d bz = new(s, 0, s);

        Vector3d ex = new(1, 0, 0);
        Vector3d ey = new(0, 1, 0);
        Vector3d ez = new(0, 0, 1);

        // ToBasis maps the standard basis onto (bx, by, bz).
        var to = QuaternionD.ToBasis(bx, by, bz);
        AssertVectorEqual(bx, to.Rotate(ex), DoubleTol, "ToBasis.Rotate(X) should be bx");
        AssertVectorEqual(by, to.Rotate(ey), DoubleTol, "ToBasis.Rotate(Y) should be by");
        AssertVectorEqual(bz, to.Rotate(ez), DoubleTol, "ToBasis.Rotate(Z) should be bz");

        // FromBasis is the inverse: it maps (bx, by, bz) back onto the standard basis.
        var from = QuaternionD.FromBasis(bx, by, bz);
        AssertVectorEqual(ex, from.Rotate(bx), DoubleTol, "FromBasis.Rotate(bx) should be X");
        AssertVectorEqual(ey, from.Rotate(by), DoubleTol, "FromBasis.Rotate(by) should be Y");
        AssertVectorEqual(ez, from.Rotate(bz), DoubleTol, "FromBasis.Rotate(bz) should be Z");
    }
}
