using BackgroundResourceProcessing.Solver;

namespace BackgroundResourceProcessing.Test.Solver
{
    [TestClass]
    public sealed class RegressionTests
    {
        [TestMethod]
        public void TestCrashSplitOutput()
        {
            var processor = TestUtil.LoadVessel("regression/crash-split-output.cfg");
            var solver = new BackgroundResourceProcessing.Solver.Solver();

            solver.ComputeInventoryRates(processor);
        }
    }
}
