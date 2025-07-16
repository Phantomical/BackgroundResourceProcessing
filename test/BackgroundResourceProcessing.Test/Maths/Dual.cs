using BackgroundResourceProcessing.Maths;

namespace BackgroundResourceProcessing.Test.Maths;

[TestClass]
public class DualTests
{
    [TestMethod]
    public void RotateNormalByQuaternion()
    {
        var nrm = new Vector3d(-0.714522795224093, -0.0871465129957876, 0.69416328077609);
        var q = Dual2Quaternion.FromAngleAxis(new(0, 1.0), new(new Vector3d(0, 1, 0)));
        var dq = DualQuaternion.FromAngleAxis(new(0, 1.0), new(new Vector3d(0, 1, 0)));

        var nq = q.Normalized();
        var ndq = dq.Normalized();

        var qc = nq.Conjugate();
        var dqc = ndq.Conjugate();

        var r = q.Rotate(new(nrm));
    }

    [TestMethod]
    public void RotateByQuaternionDual()
    {
        var q1 = DualQuaternion.FromAngleAxis(new(0, 1), new(new Vector3d(0, 1, 0)));
        var q2 = DualQuaternion.FromAngleAxis(new(0.5 * Math.PI, 1), new(new Vector3d(0, 1, 0)));
        var v = new DualVector3(new(1, 0, 0));
        var r1 = q1.Rotate(v);
        var r2 = q2.Rotate(v);
    }
}
