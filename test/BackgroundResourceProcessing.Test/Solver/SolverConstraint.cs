using BackgroundResourceProcessing.Collections.Burst;

namespace BackgroundResourceProcessing.Test.Solver;

[TestClass]
public class SolverConstraintTests
{
    [TestCleanup]
    public void Cleanup()
    {
        TestAllocator.Cleanup();
    }

    #region AT_MOST constraints
    [TestMethod]
    public void TestAtMostEnabled()
    {
        var processor = TestUtil.LoadVessel("constraint/at-most-enabled.cfg");
        processor.ComputeRates();

        Assert.AreEqual(1.0, processor.converters[0].Rate);
    }

    [TestMethod]
    public void TestAtMostDisabled()
    {
        var processor = TestUtil.LoadVessel("constraint/at-most-disabled.cfg");
        processor.ComputeRates();

        Assert.AreEqual(0.0, processor.converters[0].Rate);
    }

    [TestMethod]
    public void TestAtMostBoundaryDisabled()
    {
        var processor = TestUtil.LoadVessel("constraint/at-most-boundary-disabled.cfg");
        processor.ComputeRates();

        Assert.AreEqual(0.0, processor.converters[0].Rate);
    }

    [TestMethod]
    public void TestAtMostBoundaryEnabled()
    {
        var processor = TestUtil.LoadVessel("constraint/at-most-boundary-enabled.cfg");
        processor.ComputeRates();

        Assert.AreEqual(1.0, processor.converters[0].Rate);
    }

    [TestMethod]
    public void TestAtMostBoundaryPartial()
    {
        var processor = TestUtil.LoadVessel("constraint/at-most-boundary-partial.cfg");
        processor.ComputeRates();

        Assert.AreEqual(0.5, processor.converters[0].Rate);
    }
    #endregion
}
