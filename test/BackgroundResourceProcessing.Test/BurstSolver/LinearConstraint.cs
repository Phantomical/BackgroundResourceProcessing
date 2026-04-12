using BackgroundResourceProcessing.BurstSolver;
using BackgroundResourceProcessing.Collections.Burst;
using KSP.Testing;

namespace BackgroundResourceProcessing.Test.BurstSolver;

public class LinearConstraintTests : BRPTestBase
{
    [TestInfo("LinearConstraintTests_TestConstraintState_AllZeros")]
    public void TestConstraintState_AllZeros()
    {
        var variables = new LinearEquation(
            new RawList<double>([0.0, 0.0, 0.0], AllocatorHandle.Temp)
        );

        var le = variables <= 0.0;
        var ge = variables >= 0.0;
        var eq = variables == 0.0;

        Assert.AreEqual(ConstraintState.VACUOUS, le.GetState());
        Assert.AreEqual(ConstraintState.VACUOUS, ge.GetState());
        Assert.AreEqual(ConstraintState.VACUOUS, eq.GetState());
    }

    [TestInfo("LinearConstraintTests_TestConstraintState_AllPositive")]
    public void TestConstraintState_AllPositive()
    {
        var variables = new LinearEquation(
            new RawList<double>([1.0, 1.0, 1.0], AllocatorHandle.Temp)
        );

        var le = variables <= 0.0;
        var ge = variables >= 0.0;
        var eq = variables == 0.0;

        Assert.AreEqual(ConstraintState.VALID, le.GetState());
        Assert.AreEqual(ConstraintState.VACUOUS, ge.GetState());
        Assert.AreEqual(ConstraintState.VALID, eq.GetState());
    }

    [TestInfo("LinearConstraintTests_TestConstraintState_AllNegative")]
    public void TestConstraintState_AllNegative()
    {
        var variables = new LinearEquation(
            new RawList<double>([-1.0, -1.0, -1.0], AllocatorHandle.Temp)
        );

        var le = variables <= 0.0;
        var ge = variables >= 0.0;
        var eq = variables == 0.0;

        Assert.AreEqual(ConstraintState.VACUOUS, le.GetState());
        Assert.AreEqual(ConstraintState.VALID, ge.GetState());
        Assert.AreEqual(ConstraintState.VALID, eq.GetState());
    }

    [TestInfo("LinearConstraintTests_TestConstraintState_Mixed")]
    public void TestConstraintState_Mixed()
    {
        var variables = new LinearEquation(
            new RawList<double>([-1.0, 1.0, -1.0], AllocatorHandle.Temp)
        );

        var le = variables <= 0.0;
        var ge = variables >= 0.0;
        var eq = variables == 0.0;

        Assert.AreEqual(ConstraintState.VALID, le.GetState());
        Assert.AreEqual(ConstraintState.VALID, ge.GetState());
        Assert.AreEqual(ConstraintState.VALID, eq.GetState());
    }
}
