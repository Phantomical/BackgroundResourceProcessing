using BackgroundResourceProcessing.Collections;
using BackgroundResourceProcessing.Solver;
using BackgroundResourceProcessing.Solver.Graph;
using BackgroundResourceProcessing.Solver.V3;
using DotNetGraph.Compilation;
using DotNetGraph.Core;
using DotNetGraph.Extensions;

namespace BackgroundResourceProcessing.Test.Solver
{
    [TestClass]
    public sealed class ResourceGraphTest
    {
        [TestMethod]
        public void MergeEquivalentInventories()
        {
            var module = TestUtil.LoadVessel("isru-2/partially-full-lfo.cfg");
            var graph = new ResourceGraph(module);

            graph.MergeEquivalentInventories();

            // All inventories containing ElectricCharge get merged together
            Assert.AreEqual(0, graph.inventoryIds.Find(2));
            Assert.AreEqual(0, graph.inventoryIds.Find(10));
            Assert.AreEqual(0, graph.inventoryIds.Find(11));
            Assert.AreEqual(0, graph.inventoryIds.Find(12));
            Assert.AreEqual(0, graph.inventoryIds.Find(13));
            Assert.AreEqual(0, graph.inventoryIds.Find(14));
            Assert.AreEqual(0, graph.inventoryIds.Find(15));

            // All inventories containing MonoPropellant get merged together
            Assert.AreEqual(1, graph.inventoryIds.Find(3));

            // Same with LiquidFuel
            Assert.AreEqual(4, graph.inventoryIds.Find(6));
            Assert.AreEqual(4, graph.inventoryIds.Find(8));

            // .. and Oxidizer
            Assert.AreEqual(5, graph.inventoryIds.Find(7));
            Assert.AreEqual(5, graph.inventoryIds.Find(9));

            // Also all the resources involved should be unconstrained except
            // for BRPSpaceObjectMass
            Assert.AreEqual(InventoryState.Unconstrained, graph.inventories[0].state); // EC
            Assert.AreEqual(InventoryState.Unconstrained, graph.inventories[1].state); // Monoprop
            Assert.AreEqual(InventoryState.Unconstrained, graph.inventories[4].state); // LF
            Assert.AreEqual(InventoryState.Unconstrained, graph.inventories[5].state); // Ox

            Assert.AreEqual(InventoryState.Full, graph.inventories[17].state); // Mass
        }

        [TestMethod]
        public void MergeInventoriesWithDifferentConstraints()
        {
            var module = TestUtil.LoadVessel("isru-2/asteroid-interceptor.cfg");
            var graph = new ResourceGraph(module);

            graph.MergeEquivalentInventories();

            // We should have merged inventories with different constraints
            // (one empty, one full, and one unconstrained) to get an unconstrained
            // merged inventory.

            Assert.AreEqual(InventoryState.Unconstrained, graph.inventories[4].state);
        }

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
            var solver = new V3Solver();

            solver.ComputeInventoryRates(processor);
        }
    }
}
