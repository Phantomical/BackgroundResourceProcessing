using System;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using BackgroundResourceProcessing.BurstSolver;
using BackgroundResourceProcessing.Collections.Burst;
using BackgroundResourceProcessing.Core;
using CommandLine;
using DotNetGraph.Compilation;
using DotNetGraph.Core;
using DotNetGraph.Extensions;
using static BackgroundResourceProcessing.Collections.KeyValuePairExt;
using InventoryState = BackgroundResourceProcessing.BurstSolver.InventoryState;

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

            var allocator = AllocatorHandle.Invalid;
            var processor = Program.LoadVessel(options.ShipPath);
            var graph = new ResourceGraph(processor, allocator);

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

        private static string InventoryStateIdent(InventoryState state)
        {
            return state switch
            {
                InventoryState.Unconstrained => "U",
                InventoryState.Empty => "E",
                InventoryState.Full => "F",
                InventoryState.Zero => "0",
                _ => "invalid",
            };
        }
    }
}

static class AdjacencyMatrixExt
{
    public static ConverterToInventoryEdges ConverterToInventoryEdges(
        this AdjacencyMatrix matrix
    ) => new(matrix);
}

struct ConverterToInventoryEdges(AdjacencyMatrix matrix)
{
    AdjacencyMatrix.RowEnumerator rows = new(matrix);
    BitSpan.Enumerator inner = default;

    public readonly Edge Current => new(rows.Index, inner.Current);

    public readonly ConverterToInventoryEdges GetEnumerator() => this;

    public bool MoveNext()
    {
        while (true)
        {
            if (inner.MoveNext())
                return true;
            if (!rows.MoveNext())
                return false;

            inner = new(rows.Current);
        }
    }

    internal struct Edge(int converter, int inventory)
    {
        public int Converter = converter;
        public int Inventory = inventory;

        public readonly void Deconstruct(out int converter, out int inventory)
        {
            converter = Converter;
            inventory = Inventory;
        }
    }
}
