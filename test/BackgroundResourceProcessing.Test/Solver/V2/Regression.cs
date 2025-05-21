using BackgroundResourceProcessing.Collections;
using BackgroundResourceProcessing.Solver;
using BackgroundResourceProcessing.Solver.Graph;
using BackgroundResourceProcessing.Solver.V2;

namespace BackgroundResourceProcessing.Test.Solver.V2
{
    [TestClass]
    public sealed class Regression
    {
        [TestMethod]
        public void Crash1()
        {
            var processor = TestUtil.LoadVessel("regression/solver-v2-crash-1.cfg");
            var graph = new ResourceGraph(processor);

            graph.MergeEquivalentInventories();
            graph.MergeEquivalentConverters();

            graph.HasSplitResourceEdges();
        }

        [TestMethod]
        public void Crash1Solve()
        {
            var processor = TestUtil.LoadVessel("regression/solver-v2-crash-1.cfg");
            var solver = new V2Solver();

            solver.ComputeInventoryRates(processor);
        }
    }
}
