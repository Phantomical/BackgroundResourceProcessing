using System.IO;
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

        public static int Run(SimulateOptions options)
        {
            Program.Setup(options);

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

            for (int i = 0; ; i++)
            {
                processor.ComputeRates();
                currentTime = processor.UpdateNextChangepoint(currentTime);

                Program.DumpProcessor(processor, Path.Combine(options.Output, $"ship-{i}.cfg"));

                if (double.IsNaN(currentTime) || double.IsInfinity(currentTime))
                    break;

                processor.UpdateInventories(currentTime);
                processor.lastUpdate = currentTime;
            }

            return 0;
        }
    }
}
