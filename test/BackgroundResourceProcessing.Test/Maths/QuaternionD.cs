using BackgroundResourceProcessing.Maths;
using KSP.Testing;

namespace BackgroundResourceProcessing.Test.Maths;

public class QuaternionDTests : BRPTestBase
{
    [TestInfo("QuaternionDTests_ToBasisIdentity")]
    public void ToBasisIdentity()
    {
        var q = QuaternionD.ToBasis(new(1, 0, 0), new(0, 1, 0), new(0, 0, 1));
        var x = q.Rotate(new(1, 0, 0));
        var y = q.Rotate(new(0, 1, 0));
        var z = q.Rotate(new(0, 0, 1));

        AssertUtils.AreEqual(new(1, 0, 0), x);
        AssertUtils.AreEqual(new(0, 1, 0), y);
        AssertUtils.AreEqual(new(0, 0, 1), z);
    }

    [TestInfo("QuaternionDTests_ToBasisRotatedY")]
    public void ToBasisRotatedY()
    {
        // A 90 degree rotation around the Y axis
        var q = QuaternionD.ToBasis(new(0, 0, -1), new(0, 1, 0), new(1, 0, 0));
        var x = q.Rotate(new(1, 0, 0));
        var y = q.Rotate(new(0, 1, 0));
        var z = q.Rotate(new(0, 0, 1));

        AssertUtils.AreEqual(new(0, 0, 1), x);
        AssertUtils.AreEqual(new(0, 1, 0), y);
        AssertUtils.AreEqual(new(-1, 0, 0), z);
    }

    [TestInfo("QuaternionDTests_ToBasisRotated45")]
    public void ToBasisRotated45()
    {
        var invsqrt2 = 1.0 / Math.Sqrt(2);

        // A 45 degree rotation around the Y axis
        var q = QuaternionD.ToBasis(
            new(invsqrt2, 0, -invsqrt2),
            new(0, 1, 0),
            new(invsqrt2, 0, invsqrt2)
        );
        var x = q.Rotate(new(1, 0, 0));
        var y = q.Rotate(new(0, 1, 0));
        var z = q.Rotate(new(0, 0, 1));

        AssertUtils.AreEqual(new(invsqrt2, 0, invsqrt2), x);
        AssertUtils.AreEqual(new(0, 1, 0), y);
        AssertUtils.AreEqual(new(-invsqrt2, 0, invsqrt2), z);
    }

    [TestInfo("QuaternionDTests_FromBasisRotated45")]
    public void FromBasisRotated45()
    {
        var invsqrt2 = 1.0 / Math.Sqrt(2);

        // A 90 degree rotation around the Y axis
        var q = QuaternionD.FromBasis(
            new(invsqrt2, 0, -invsqrt2),
            new(0, 1, 0),
            new(invsqrt2, 0, invsqrt2)
        );

        var x = q.Rotate(new(invsqrt2, 0, -invsqrt2));
        var y = q.Rotate(new(0, 1, 0));
        var z = q.Rotate(new(invsqrt2, 0, invsqrt2));

        AssertUtils.AreEqual(new(0, 0, -1), x);
        AssertUtils.AreEqual(new(0, 1, 0), y);
        AssertUtils.AreEqual(new(1, 0, 0), z);
    }

    [TestInfo("QuaternionDTests_Rotate90DegreesAroundY")]
    public void Rotate90DegreesAroundY()
    {
        var q = QuaternionD.FromAngleAxis(Math.PI / 2, new(0, 1, 0));
        var x = q.Rotate(new(1, 0, 0));
        var y = q.Rotate(new(0, 1, 0));
        var z = q.Rotate(new(0, 0, 1));

        AssertUtils.AreEqual(new(0, 0, 1), x);
        AssertUtils.AreEqual(new(0, 1, 0), y);
        AssertUtils.AreEqual(new(-1, 0, 0), z);
    }
}
