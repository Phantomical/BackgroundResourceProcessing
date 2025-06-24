using System;
using System.IO;
using BackgroundResourceProcessing.Tracing;
using CommandLine;

namespace BackgroundResourceProcessing.CLI
{
    [Verb("simulate", HelpText = "Simulate an exported ship file")]
    public class SimulateOptions : BaseOptions
    {
        [Value(0, MetaName = "PATH", HelpText = "Path to the ship file")]
        public string ShipPath { get; set; }

        [Option(
            'o',
            "output",
            Required = true,
            HelpText = "Output directory to which to emit the simulation ship files"
        )]
        public string Output { get; set; }

        [Option(
            "max-iterations",
            HelpText = "The maximum number of iterations that will be performed"
        )]
        public uint MaxIters { get; set; } = 50;

        public static int Run(SimulateOptions options)
        {
            Program.Setup(options);

            using var trace = options.Trace != null ? Tracing.Trace.Start(options.Trace) : null;

            var processor = Program.LoadVessel(options.ShipPath);
            Directory.CreateDirectory(options.Output);
            foreach (var file in Directory.EnumerateFiles(options.Output))
            {
                if (file.EndsWith(".cfg") && file.StartsWith("ship-"))
                    File.Delete(file);
            }

            var currentTime = 0.0;
            processor.lastUpdate = currentTime;

            processor.ValidateReferencedInventories();

            for (uint i = 0; ; i++)
            {
                if (i >= options.MaxIters)
                    throw new Exception("Reached maximum number of allowed iterations");

                processor.ComputeRates();
                var prev = currentTime;
                currentTime = processor.UpdateNextChangepoint(currentTime);

                LogUtil.Log($"Dumping ship at {prev}");
                using (var dumpSpan = new TraceSpan("DumpProcessor"))
                {
                    Program.DumpVessel(processor, Path.Combine(options.Output, $"ship-{i}.cfg"));
                }

                if (double.IsNaN(currentTime) || double.IsInfinity(currentTime))
                    break;
                if (currentTime == prev)
                    throw new Exception("Simulation failed to progress");

                processor.UpdateState(currentTime);
                processor.lastUpdate = currentTime;
            }

            return 0;
        }
    }
}
