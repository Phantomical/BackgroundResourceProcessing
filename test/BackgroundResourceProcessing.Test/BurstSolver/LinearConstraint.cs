using BackgroundResourceProcessing.BurstSolver;
using BackgroundResourceProcessing.Collections.Burst;

namespace BackgroundResourceProcessing.Test.BurstSolver;

[TestClass]
public class LinearConstraintTests
{
    [TestCleanup]
    public void Cleanup()
    {
        TestAllocator.Cleanup();
    }

    [TestMethod]
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

    [TestMethod]
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

    [TestMethod]
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

    [TestMethod]
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
