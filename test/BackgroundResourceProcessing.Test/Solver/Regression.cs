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

        [TestMethod]
        public void TestCrashBadVariableSelection()
        {
            var processor = TestUtil.LoadVessel("regression/crash-bad-var-selection.cfg");
            var solver = new BackgroundResourceProcessing.Solver.Solver();

            solver.ComputeInventoryRates(processor);
        }

        [TestMethod]
        public void TestCrashSelectedSlack()
        {
            var processor = TestUtil.LoadVessel("regression/crash-selected-slack.cfg");
            var solver = new BackgroundResourceProcessing.Solver.Solver();

            solver.ComputeInventoryRates(processor);
        }

        [TestMethod]
        public void TestFullDumpExcess()
        {
            var processor = TestUtil.LoadVessel("regression/crash-full-dump-excess.cfg");
            var solver = new BackgroundResourceProcessing.Solver.Solver();

            solver.ComputeInventoryRates(processor);
        }
    }
}
