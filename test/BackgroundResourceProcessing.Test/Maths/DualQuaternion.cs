using BackgroundResourceProcessing.Maths;
using KSP.Testing;

namespace BackgroundResourceProcessing.Test.Maths;

/// <summary>
/// Locks down the rotation value and derivatives of the auto-differentiating
/// <see cref="DualQuaternion"/> and <see cref="Dual2Quaternion"/> types.
///
/// The convention is right-handed (q*v*q*) and matches
/// <c>UnityEngine.Quaternion.AngleAxis</c>. The scalar <see cref="QuaternionD"/>
/// implementation shares this convention and is used as the independent
/// reference for finite-difference derivative checks.
/// </summary>
public class DualQuaternionTests : BRPTestBase
{
    // A set of (angle, axis, vector) cases exercised by the tests below.
    // Axes are normalized; the off-axis case is (1,1,1)/sqrt(3) at 120 deg.
    private static readonly (double angle, Vector3d axis, Vector3d vec)[] Cases =
    {
        (Math.PI / 2, new Vector3d(1, 0, 0), new Vector3d(0, 1, 0)),
        (Math.PI / 2, new Vector3d(0, 1, 0), new Vector3d(1, 0, 0)),
        (Math.PI / 2, new Vector3d(0, 0, 1), new Vector3d(0, 1, 0)),
        (2.0 * Math.PI / 3.0, new Vector3d(1, 1, 1).normalized, new Vector3d(1, 0, 0)),
        (0.7, new Vector3d(0.2, -0.5, 0.84).normalized, new Vector3d(-0.3, 0.9, 0.1)),
    };

    // Helper: value-level rotation using the independent scalar implementation.
    private static Vector3d ScalarRotate(double angle, Vector3d axis, Vector3d vec)
    {
        return QuaternionD.FromAngleAxis(angle, axis).Rotate(vec);
    }

    [TestInfo("DualQuaternionTests_DualValueMatchesScalar")]
    public void DualValueMatchesScalar()
    {
        // The dual rotation VALUE (with a constant angle, dx=0) must agree with
        // the scalar QuaternionD rotation. Tolerance 1e-9 - these are the same
        // arithmetic in double precision, so they should agree to near machine
        // epsilon; 1e-9 leaves margin for the differing operation order.
        const double tol = 1e-9;

        foreach (var (angle, axis, vec) in Cases)
        {
            var q = DualQuaternion.FromAngleAxis(new Dual(angle), new DualVector3(axis));
            var r = q.Rotate(new DualVector3(vec));
            var expected = ScalarRotate(angle, axis, vec);

            Assert.AreEqual(expected.x, r.x.x, tol, "DualQuaternion value x");
            Assert.AreEqual(expected.y, r.y.x, tol, "DualQuaternion value y");
            Assert.AreEqual(expected.z, r.z.x, tol, "DualQuaternion value z");
        }
    }

    [TestInfo("DualQuaternionTests_Dual2ValueMatchesScalar")]
    public void Dual2ValueMatchesScalar()
    {
        // Same as above for the second-order Dual2Quaternion.
        const double tol = 1e-9;

        foreach (var (angle, axis, vec) in Cases)
        {
            var q = Dual2Quaternion.FromAngleAxis(new Dual2(angle), new Dual2Vector3(axis));
            var r = q.Rotate(new Dual2Vector3(vec));
            var expected = ScalarRotate(angle, axis, vec);

            Assert.AreEqual(expected.x, r.x.x, tol, "Dual2Quaternion value x");
            Assert.AreEqual(expected.y, r.y.x, tol, "Dual2Quaternion value y");
            Assert.AreEqual(expected.z, r.z.x, tol, "Dual2Quaternion value z");
        }
    }

    [TestInfo("DualQuaternionTests_FirstDerivativeMatchesFiniteDifference")]
    public void FirstDerivativeMatchesFiniteDifference()
    {
        // Make the rotation angle the differentiation variable and check that
        // the propagated .dx equals a central finite difference of the scalar
        // rotation w.r.t. theta.
        //
        // Central differences have error O(h^2). With h = 1e-5 the truncation
        // error is ~1e-10 and the rounding floor for a central difference is
        // ~eps/h ~ 1e-11; the rotation magnitudes here are O(1), so a tolerance
        // of 1e-6 is comfortably above the achievable accuracy while still
        // catching any real derivative-propagation bug.
        const double h = 1e-5;
        const double tol = 1e-6;

        foreach (var (angle, axis, vec) in Cases)
        {
            var theta = Dual.Variable(angle);
            var q = DualQuaternion.FromAngleAxis(theta, new DualVector3(axis));
            var r = q.Rotate(new DualVector3(vec));

            var plus = ScalarRotate(angle + h, axis, vec);
            var minus = ScalarRotate(angle - h, axis, vec);
            var fd = (plus - minus) / (2.0 * h);

            Assert.AreEqual(fd.x, r.x.dx, tol, "DualQuaternion dx for x");
            Assert.AreEqual(fd.y, r.y.dx, tol, "DualQuaternion dx for y");
            Assert.AreEqual(fd.z, r.z.dx, tol, "DualQuaternion dx for z");
        }
    }

    [TestInfo("DualQuaternionTests_SecondDerivativeMatchesFiniteDifference")]
    public void SecondDerivativeMatchesFiniteDifference()
    {
        // This is the regression that broke the landed-shadow terminator
        // search: the second derivative through the quaternion path must be
        // correct.
        //
        // Use Dual2.Variable so .dx is d/dtheta and .ddx is d^2/dtheta^2.
        // Check .dx against a first central difference and .ddx against the
        // second central difference f''(t) ~ (f(t+h) - 2 f(t) + f(t-h)) / h^2.
        //
        // The second difference rounding floor is ~eps/h^2. With h = 1e-3 that
        // floor is ~2e-10 and the truncation error is O(h^2) ~ 1e-6, so the
        // total achievable accuracy is dominated by truncation at ~1e-6. We use
        // 1e-3 tolerance to leave generous margin while still firmly rejecting a
        // wrong second derivative (which would be off by an O(1) amount). The
        // first-difference .dx check uses the tighter 1e-6 tolerance.
        const double h = 1e-3;
        const double tolFirst = 1e-6;
        const double tolSecond = 1e-3;

        foreach (var (angle, axis, vec) in Cases)
        {
            var theta = Dual2.Variable(angle);
            var q = Dual2Quaternion.FromAngleAxis(theta, new Dual2Vector3(axis));
            var r = q.Rotate(new Dual2Vector3(vec));

            var plus = ScalarRotate(angle + h, axis, vec);
            var mid = ScalarRotate(angle, axis, vec);
            var minus = ScalarRotate(angle - h, axis, vec);

            var fd1 = (plus - minus) / (2.0 * h);
            var fd2 = (plus - 2.0 * mid + minus) / (h * h);

            Assert.AreEqual(fd1.x, r.x.dx, tolFirst, "Dual2Quaternion dx for x");
            Assert.AreEqual(fd1.y, r.y.dx, tolFirst, "Dual2Quaternion dx for y");
            Assert.AreEqual(fd1.z, r.z.dx, tolFirst, "Dual2Quaternion dx for z");

            Assert.AreEqual(fd2.x, r.x.ddx, tolSecond, "Dual2Quaternion ddx for x");
            Assert.AreEqual(fd2.y, r.y.ddx, tolSecond, "Dual2Quaternion ddx for y");
            Assert.AreEqual(fd2.z, r.z.ddx, tolSecond, "Dual2Quaternion ddx for z");
        }
    }

    [TestInfo("DualQuaternionTests_MatchesUnityConvention")]
    public void MatchesUnityConvention()
    {
        // Pin the rotation convention to UnityEngine.Quaternion, which uses
        // degrees and single-precision Vector3/Quaternion. The float math means
        // we can only expect agreement to ~1e-4.
        const double tol = 1e-4;

        foreach (var (angleRad, axis, vec) in Cases)
        {
            var q = DualQuaternion.FromAngleAxis(new Dual(angleRad), new DualVector3(axis));
            var r = q.Rotate(new DualVector3(vec));

            var angleDeg = (float)(angleRad * 180.0 / Math.PI);
            var unityAxis = new UnityEngine.Vector3((float)axis.x, (float)axis.y, (float)axis.z);
            var unityVec = new UnityEngine.Vector3((float)vec.x, (float)vec.y, (float)vec.z);
            var unity = UnityEngine.Quaternion.AngleAxis(angleDeg, unityAxis) * unityVec;

            Assert.AreEqual(unity.x, r.x.x, tol, "Unity convention x");
            Assert.AreEqual(unity.y, r.y.x, tol, "Unity convention y");
            Assert.AreEqual(unity.z, r.z.x, tol, "Unity convention z");
        }
    }
}
