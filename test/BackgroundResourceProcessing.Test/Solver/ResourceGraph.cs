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
        public void MergeIdenticalInventories()
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

        private static void EmitDot(ResourceGraph rg, string filename)
        {
            var graph = new DotGraph().WithIdentifier("ResourceGraph");
            graph.Directed = true;

            foreach (var (id, inventory) in rg.inventories.KSPEnumerate())
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

            foreach (var (converter, inventories) in rg.inputs.Forward.KSPEnumerate())
            {
                foreach (var inventory in inventories)
                {
                    var edge = new DotEdge()
                        .From($"i{inventory}")
                        .To($"c{converter}")
                        .WithArrowTail(DotEdgeArrowType.None)
                        .WithArrowHead(DotEdgeArrowType.Normal);

                    graph.Add(edge);
                }
            }

            foreach (var (converter, inventories) in rg.outputs.Forward.KSPEnumerate())
            {
                foreach (var inventory in inventories)
                {
                    var edge = new DotEdge()
                        .To($"i{inventory}")
                        .From($"c{converter}")
                        .WithArrowTail(DotEdgeArrowType.None)
                        .WithArrowHead(DotEdgeArrowType.Normal);

                    graph.Add(edge);
                }
            }

            var path = Path.Combine(Path.Combine(TestUtil.ProjectDirectory, "bin"), filename);
            var file = File.Create(path);
            using (var writer = new StreamWriter(file))
            {
                var context = new CompilationContext(
                    writer,
                    new CompilationOptions() { Indented = true }
                );

                var task = graph.CompileAsync(context);

                task.Wait();
            }
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
