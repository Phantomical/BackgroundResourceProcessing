using BackgroundResourceProcessing.Collections.Burst;
using KSP.Testing;

namespace BackgroundResourceProcessing.Test.Solver;

public class SolverConstraintTests : BRPTestBase
{
    #region AT_MOST constraints
    [TestInfo("SolverConstraintTests_TestAtMostEnabled")]
    public void TestAtMostEnabled()
    {
        var processor = TestUtil.LoadVessel("constraint/at-most-enabled.cfg");
        using var solve = processor.ComputeRates();
        solve.Complete();

        Assert.AreEqual(1.0, processor.converters[0].Rate);
    }

    [TestInfo("SolverConstraintTests_TestAtMostDisabled")]
    public void TestAtMostDisabled()
    {
        var processor = TestUtil.LoadVessel("constraint/at-most-disabled.cfg");
        using var solve = processor.ComputeRates();
        solve.Complete();

        Assert.AreEqual(0.0, processor.converters[0].Rate);
    }

    [TestInfo("SolverConstraintTests_TestAtMostBoundaryDisabled")]
    public void TestAtMostBoundaryDisabled()
    {
        var processor = TestUtil.LoadVessel("constraint/at-most-boundary-disabled.cfg");
        using var solve = processor.ComputeRates();
        solve.Complete();

        Assert.AreEqual(0.0, processor.converters[0].Rate);
    }

    [TestInfo("SolverConstraintTests_TestAtMostBoundaryEnabled")]
    public void TestAtMostBoundaryEnabled()
    {
        var processor = TestUtil.LoadVessel("constraint/at-most-boundary-enabled.cfg");
        using var solve = processor.ComputeRates();
        solve.Complete();

        Assert.AreEqual(1.0, processor.converters[0].Rate);
    }

    [TestInfo("SolverConstraintTests_TestAtMostBoundaryPartial")]
    public void TestAtMostBoundaryPartial()
    {
        var processor = TestUtil.LoadVessel("constraint/at-most-boundary-partial.cfg");
        using var solve = processor.ComputeRates();
        solve.Complete();

        Assert.AreEqual(0.5, processor.converters[0].Rate);
    }

    [TestInfo("SolverConstraintTests_TestAtLeastEnabled")]
    public void TestAtLeastEnabled()
    {
        var processor = TestUtil.LoadVessel("constraint/at-least-enabled.cfg");
        using var solve = processor.ComputeRates();
        solve.Complete();

        Assert.AreEqual(1.0, processor.converters[0].Rate);
    }

    [TestInfo("SolverConstraintTests_TestAtLeastDisabled")]
    public void TestAtLeastDisabled()
    {
        var processor = TestUtil.LoadVessel("constraint/at-least-disabled.cfg");
        using var solve = processor.ComputeRates();
        solve.Complete();

        Assert.AreEqual(0.0, processor.converters[0].Rate);
    }

    [TestInfo("SolverConstraintTests_TestAtLeastBoundaryDisabled")]
    public void TestAtLeastBoundaryDisabled()
    {
        var processor = TestUtil.LoadVessel("constraint/at-least-boundary-disabled.cfg");
        using var solve = processor.ComputeRates();
        solve.Complete();

        Assert.AreEqual(0.0, processor.converters[0].Rate);
    }

    [TestInfo("SolverConstraintTests_TestAtLeastBoundaryEnabled")]
    public void TestAtLeastBoundaryEnabled()
    {
        var processor = TestUtil.LoadVessel("constraint/at-least-boundary-enabled.cfg");
        using var solve = processor.ComputeRates();
        solve.Complete();

        Assert.AreEqual(1.0, processor.converters[0].Rate);
    }

    [TestInfo("SolverConstraintTests_TestAtLeastBoundaryPartial")]
    public void TestAtLeastBoundaryPartial()
    {
        var processor = TestUtil.LoadVessel("constraint/at-least-boundary-partial.cfg");
        using var solve = processor.ComputeRates();
        solve.Complete();

        Assert.AreEqual(0.5, processor.converters[0].Rate);
    }
    #endregion
}
