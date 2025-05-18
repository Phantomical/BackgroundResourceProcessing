using BackgroundResourceProcessing.Solver.Graph;

namespace BackgroundResourceProcessing.Test.Solver
{
    [TestClass]
    public sealed class ResourceGraphTest
    {
        [TestMethod]
        public void MergeIdenticalInventories()
        {
            var module = TestUtil.LoadVessel("isru-2/asteroid-interceptor.cfg");
            var graph = new ResourceGraph(module);

            LogUtil.Log($"Initial graph: {TestUtil.DumpJson(graph)}");

            graph.MergeEquivalentInventories();

            LogUtil.Log($"Inventories merged: {TestUtil.DumpJson(graph)}");
        }
    }
}
