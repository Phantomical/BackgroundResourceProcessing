using System;
using System.IO;
using BackgroundResourceProcessing.Core;
using CommandLine;

namespace BackgroundResourceProcessing.CLI
{
    public class BaseOptions
    {
        [Option("verbose", HelpText = "Enable verbose logging")]
        public bool Verbose { get; set; }
    }

    class Program
    {
        static int Main(string[] args)
        {
            return Parser
                .Default.ParseArguments<SimulateOptions, DotOptions>(args)
                .MapResult(
                    (SimulateOptions options) => SimulateOptions.Run(options),
                    (DotOptions options) => DotOptions.Run(options),
                    errors => 1
                );
        }

        public static void Setup(BaseOptions options)
        {
            if (options.Verbose)
                LogUtil.Sink = new VerboseLogSink();
            else
                LogUtil.Sink = new SilentLogSink();

            Registrar.RegisterAllBehaviours(typeof(Registrar).Assembly);
        }

        public static ResourceProcessor LoadVessel(string path)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException($"file not found: {path}");

            var node = ConfigNode.Load(path);
            var processor = new ResourceProcessor();
            processor.Load(node.GetNode("BRP_SHIP"));
            return processor;
        }

        public static void DumpProcessor(ResourceProcessor processor, string path)
        {
            ConfigNode root = new();
            ConfigNode node = root.AddNode("BRP_SHIP");
            processor.Save(node);
            root.Save(path);
        }
    }
}
