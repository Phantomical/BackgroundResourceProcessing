using System;
using System.IO;
using System.Reflection;
using BackgroundResourceProcessing.Core;
using CommandLine;

namespace BackgroundResourceProcessing.CLI
{
    public class BaseOptions
    {
        [Option("verbose", HelpText = "Enable verbose logging")]
        public bool Verbose { get; set; }

        [Option("trace", HelpText = "Emit tracing data to a file")]
        public string Trace { get; set; }
    }

    class Program
    {
        static int Main(string[] args)
        {
            try
            {
                return Parser
                    .Default.ParseArguments<SimulateOptions, DotOptions, ReexportOptions>(args)
                    .MapResult(
                        (SimulateOptions options) => SimulateOptions.Run(options),
                        (DotOptions options) => DotOptions.Run(options),
                        (ReexportOptions options) => options.Run(),
                        errors => 1
                    );
            }
            catch (ReflectionTypeLoadException e)
            {
                Console.Error.WriteLine($"Unhandled Exception: {e}");

                foreach (var exception in e.LoaderExceptions)
                {
                    Console.Error.WriteLine($"Loader Exception: {exception}");
                }

                return 2;
            }
        }

        public static void Setup(BaseOptions options)
        {
            if (options.Verbose)
                LogUtil.Sink = new VerboseLogSink();
            else
                LogUtil.Sink = new SilentLogSink();

            BehaviourRegistry.RegisterAllBehaviours(typeof(BehaviourRegistry).Assembly);
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

        public static void DumpVessel(ResourceProcessor processor, string path)
        {
            ConfigNode root = new();
            ConfigNode node = root.AddNode("BRP_SHIP");
            processor.Save(node);
            root.Save(path);
        }
    }
}
