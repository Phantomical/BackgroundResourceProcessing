using System;
using System.IO;
using System.Linq;
using BackgroundResourceProcessing.Collections;
using BackgroundResourceProcessing.Core;
using BackgroundResourceProcessing.Solver;
using CommandLine;
using DotNetGraph.Compilation;
using DotNetGraph.Core;
using DotNetGraph.Extensions;

namespace BackgroundResourceProcessing.CLI
{
    public enum MinimizeMode
    {
        Full,
        None,
        InventoryOnly,
        ConverterOnly,
    }

    [Verb("dot", HelpText = "Generate a dot graph for a ship file")]
    public class DotOptions : BaseOptions
    {
        [Value(0, MetaName = "PATH", HelpText = "Path to the ship file")]
        public string ShipPath { get; set; }

        [Option('o', "output", HelpText = "Output path to emit the dot file to")]
        public string Output { get; set; }

        [Option(
            "minimize",
            HelpText = "Configures how much minimization to perform",
            Default = MinimizeMode.Full
        )]
        public MinimizeMode Minimize { get; set; }

        public static int Run(DotOptions options)
        {
            Program.Setup(options);

            var processor = Program.LoadVessel(options.ShipPath);
            var graph = new ResourceGraph(processor);

            switch (options.Minimize)
            {
                case MinimizeMode.Full:
                    graph.MergeEquivalentInventories();
                    graph.MergeEquivalentConverters();
                    break;
                case MinimizeMode.InventoryOnly:
                    graph.MergeEquivalentInventories();
                    break;
                case MinimizeMode.ConverterOnly:
                    graph.MergeEquivalentConverters();
                    break;
                case MinimizeMode.None:
                    break;
            }

            if (options.Output == null)
            {
                using var writer = new StreamWriter(Console.OpenStandardOutput());
                EmitDot(processor, graph, writer);
            }
            else
            {
                var file = File.Create(options.Output);
                using var writer = new StreamWriter(file);
                EmitDot(processor, graph, writer);
            }

            return 0;
        }

        private static void EmitDot(
            ResourceProcessor processor,
            ResourceGraph rg,
            StreamWriter output
        )
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

            foreach (var (id, converter) in rg.converters)
            {
                var real = processor.converters[converter.baseId];
                int count = rg.converterIds.Where(id => id == converter.baseId).Count();

                var label = $"c{id}";
                if (real.Behaviour?.SourceModule != null)
                    label = $"{label}: {real.Behaviour.SourceModule}";
                if (count > 1)
                    label = $"{label} (+{count - 1})";

                var node = new DotNode()
                    .WithIdentifier($"c{id}")
                    .WithLabel(label)
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

            var context = new CompilationContext(
                output,
                new CompilationOptions() { Indented = true }
            );

            graph.CompileAsync(context).Wait();
        }

        private static string InventoryStateIdent(Solver.InventoryState state)
        {
            switch (state)
            {
                case Solver.InventoryState.Unconstrained:
                    return "U";
                case Solver.InventoryState.Empty:
                    return "E";
                case Solver.InventoryState.Full:
                    return "F";
                case Solver.InventoryState.Zero:
                    return "0";
                default:
                    return "invalid";
            }
        }
    }
}
