using BackgroundResourceProcessing.BurstSolver;
using BackgroundResourceProcessing.Collections.Burst;
using KSP.Testing;

namespace BackgroundResourceProcessing.Test.Solver
{
    public sealed class ResourceGraphTest : BRPTestBase
    {
        [TestInfo("ResourceGraphTest_MergeEquivalentInventories")]
        public void MergeEquivalentInventories()
        {
            var module = TestUtil.LoadVessel("isru-2/partially-full-lfo.cfg");
            var graph = new ResourceGraph(module, AllocatorHandle.Temp);

            graph.MergeEquivalentInventories();

            // All inventories containing ElectricCharge get merged together
            Assert.AreEqual(0, graph.inventoryIds[2]);
            Assert.AreEqual(0, graph.inventoryIds[10]);
            Assert.AreEqual(0, graph.inventoryIds[11]);
            Assert.AreEqual(0, graph.inventoryIds[12]);
            Assert.AreEqual(0, graph.inventoryIds[13]);
            Assert.AreEqual(0, graph.inventoryIds[14]);
            Assert.AreEqual(0, graph.inventoryIds[15]);

            // All inventories containing MonoPropellant get merged together
            Assert.AreEqual(1, graph.inventoryIds[3]);

            // Same with LiquidFuel
            Assert.AreEqual(4, graph.inventoryIds[6]);
            Assert.AreEqual(4, graph.inventoryIds[8]);

            // .. and Oxidizer
            Assert.AreEqual(5, graph.inventoryIds[7]);
            Assert.AreEqual(5, graph.inventoryIds[9]);

            // Also all the resources involved should be unconstrained except
            // for BRPSpaceObjectMass
            Assert.AreEqual(InventoryState.Unconstrained, graph.inventories[0].state); // EC
            Assert.AreEqual(InventoryState.Unconstrained, graph.inventories[1].state); // Monoprop
            Assert.AreEqual(InventoryState.Unconstrained, graph.inventories[4].state); // LF
            Assert.AreEqual(InventoryState.Unconstrained, graph.inventories[5].state); // Ox

            Assert.AreEqual(InventoryState.Full, graph.inventories[17].state); // Mass
        }

        [TestInfo("ResourceGraphTest_MergeInventoriesWithDifferentConstraints")]
        public void MergeInventoriesWithDifferentConstraints()
        {
            var module = TestUtil.LoadVessel("isru-2/asteroid-interceptor.cfg");
            var graph = new ResourceGraph(module, AllocatorHandle.Temp);

            graph.MergeEquivalentInventories();

            // We should have merged inventories with different constraints
            // (one empty, one full, and one unconstrained) to get an unconstrained
            // merged inventory.

            Assert.AreEqual(InventoryState.Unconstrained, graph.inventories[4].state);
        }

        [TestInfo("ResourceGraphTest_Crash1")]
        public void Crash1()
        {
            var processor = TestUtil.LoadVessel("regression/solver-v2-crash-1.cfg");
            var graph = new ResourceGraph(processor, AllocatorHandle.Temp);

            graph.MergeEquivalentInventories();
            graph.MergeEquivalentConverters();
        }

        [TestInfo("ResourceGraphTest_Crash1Solve")]
        public void Crash1Solve()
        {
            var processor = TestUtil.LoadVessel("regression/solver-v2-crash-1.cfg");
            using var solve = processor.ComputeRates();
            solve.Complete();
        }
    }
}
