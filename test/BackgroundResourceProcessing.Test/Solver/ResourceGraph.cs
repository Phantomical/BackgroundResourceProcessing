using BackgroundResourceProcessing.Collections;
using BackgroundResourceProcessing.Solver;
using BackgroundResourceProcessing.Solver.Graph;
using DotNetGraph.Compilation;
using DotNetGraph.Core;
using DotNetGraph.Extensions;

namespace BackgroundResourceProcessing.Test.Solver
{
    [TestClass]
    public sealed class ResourceGraphTest
    {
        [TestMethod]
        public void RunAllSteps()
        {
            var module = TestUtil.LoadVessel("isru-2/asteroid-interceptor.cfg");
            var graph = new ResourceGraph(module);

            LogUtil.Log($"Initial graph: {TestUtil.DumpJson(graph)}");
            EmitDot(graph, "initial.dot");

            graph.MergeEquivalentInventories();

            LogUtil.Log($"Inventories merged: {TestUtil.DumpJson(graph)}");
            EmitDot(graph, "inv-merged.dot");

            graph.MergeEquivalentConverters();

            LogUtil.Log($"Converters merged: {TestUtil.DumpJson(graph)}");
            EmitDot(graph, "conv-merged.dot");
        }

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

        private static void EmitDot(ResourceGraph rg, string filename)
        {
            var graph = new DotGraph().WithIdentifier("ResourceGraph");
            graph.Directed = true;

            foreach (var (id, inventory) in rg.inventories)
            {
                var node = new DotNode()
                    .WithIdentifier($"i{id}")
                    .WithLabel(
                        $"i{id}: {inventory.resourceName} [{InventoryStateIdent(inventory.state)}]"
                    )
                    .WithStyle(DotNodeStyle.Dashed);

                graph.Add(node);
            }

            foreach (var id in rg.converters.Keys)
            {
                var node = new DotNode()
                    .WithIdentifier($"c{id}")
                    .WithLabel($"c{id}")
                    .WithStyle(DotNodeStyle.Solid);

                graph.Add(node);
            }

            foreach (var (converter, inventory) in rg.inputs.ConverterToInventoryEdges())
            {
                var edge = new DotEdge()
                    .From($"i{inventory}")
                    .To($"c{converter}")
                    .WithArrowTail(DotEdgeArrowType.None)
                    .WithArrowHead(DotEdgeArrowType.Normal);

                graph.Add(edge);
            }

            foreach (var (converter, inventory) in rg.outputs.ConverterToInventoryEdges())
            {
                var edge = new DotEdge()
                    .To($"i{inventory}")
                    .From($"c{converter}")
                    .WithArrowTail(DotEdgeArrowType.None)
                    .WithArrowHead(DotEdgeArrowType.Normal);

                graph.Add(edge);
            }

            var path = Path.Combine(Path.Combine(TestUtil.ProjectDirectory, "bin"), filename);
            var file = File.Create(path);
            using var writer = new StreamWriter(file);
            var context = new CompilationContext(
                writer,
                new CompilationOptions() { Indented = true }
            );

            graph.CompileAsync(context).Wait();
        }

        private static string InventoryStateIdent(InventoryState state)
        {
            switch (state)
            {
                case InventoryState.Unconstrained:
                    return "U";
                case InventoryState.Empty:
                    return "E";
                case InventoryState.Full:
                    return "F";
                case InventoryState.Zero:
                    return "0";
                default:
                    return "invalid";
            }
        }
    }
}
