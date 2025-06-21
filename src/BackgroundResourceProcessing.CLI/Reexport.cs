using CommandLine;

namespace BackgroundResourceProcessing.CLI
{
    [Verb("reexport", HelpText = "Reexport a ship file")]
    public class ReexportOptions : BaseOptions
    {
        [Value(0, MetaName = "PATH", HelpText = "Path to the ship file")]
        public string ShipPath { get; set; }

        [Option(
            'o',
            "output",
            HelpText = "Output path to emit the new ship file to",
            Required = true
        )]
        public string Output { get; set; }

        public int Run()
        {
            Program.Setup(this);

            var vessel = Program.LoadVessel(ShipPath);
            Program.DumpVessel(vessel, Output);
            return 0;
        }
    }
}
