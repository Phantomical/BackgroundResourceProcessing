using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using BackgroundResourceProcessing.Core;
using BackgroundResourceProcessing.Utils;
using KSPAchievements;

namespace BackgroundResourceProcessing.Test
{
    internal static class TestUtil
    {
        private static string GetCallerFilePath([CallerFilePath] string callerFilePath = null)
        {
            return callerFilePath ?? throw new ArgumentNullException(nameof(callerFilePath));
        }

        public static string ProjectDirectory => Path.GetDirectoryName(GetCallerFilePath());

        /// <summary>
        /// Load a saved cfg file from the <c>vessels</c> directory.
        /// </summary>
        /// <param name="name">
        /// The relative path of the config file under the <c>vessels</c> directory.
        /// </param>
        /// <returns></returns>
        public static ResourceProcessor LoadVessel(string name)
        {
            var configPath = Path.Combine(ProjectDirectory, "vessels", name);

            // ConfigNode attempts to use unity's Debug.Log if an error occurs.
            // The unit tests run outside of unity so this ends up causing an
            // opaque error. We fix that here by manually checking in advance.
            if (!File.Exists(configPath))
                throw new FileNotFoundException($"file not found: {configPath}");

            var configNode = ConfigNode.Load(configPath);
            var processor = new ResourceProcessor();
            processor.Load(configNode.GetNode("BRP_SHIP"));
            return processor;
        }

        static readonly JsonSerializerOptions options = new()
        {
            WriteIndented = true,
            IncludeFields = true,
            NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        };

        static TestUtil()
        {
            options.Converters.Add(new JsonStringEnumConverter());
        }

        public static string DumpJson<T>(T obj)
        {
            return JsonSerializer.Serialize(obj, options);
        }

        public static void DumpProcessor(ResourceProcessor processor, string filename)
        {
            ConfigNode root = new();
            ConfigNode node = root.AddNode("BRP_SHIP");
            processor.Save(node);

            Directory.CreateDirectory(Path.Combine(ProjectDirectory, "bin/ship"));
            var path = Path.Combine(ProjectDirectory, "bin/ship", filename);
            root.Save(path);
        }

        public static string SequenceToString<T>(IEnumerable<T> seq)
        {
            StringBuilder builder = new();
            builder.Append("[");

            bool first = true;
            foreach (var item in seq)
            {
                if (!first)
                    builder.Append(", ");
                else
                    first = false;

                builder.Append(item);
            }

            builder.Append("]");
            return builder.ToString();
        }
    }

    [TestClass]
    public static class Setup
    {
        public class TestLogErrorException(string message) : Exception(message) { }

        internal class TestLogSink(string path) : LogUtil.ILogSink
        {
            internal TextWriter output = new StreamWriter(File.Open(path, FileMode.Create));

            public void Error(string message)
            {
                throw new TestLogErrorException(message);
            }

            public void Log(string message)
            {
                lock (output)
                {
                    output.WriteLine($"[INFO]  {message}");
                }
            }

            public void Warn(string message)
            {
                lock (output)
                {
                    output.WriteLine($"[WARN]  {message}");
                }
            }
        }

        static TestLogSink sink;

        static Setup()
        {
            sink = new TestLogSink(Path.Combine(TestUtil.ProjectDirectory, "bin/test-output.log"));
#if DEBUG
            LogUtil.Sink = sink;
#endif
            TypeRegistry.RegisterForTest();
        }

        [AssemblyInitialize()]
        public static void AssemblyInitialize(TestContext _) { }

        [AssemblyCleanup()]
        public static void AssemblyCleanup()
        {
            sink.output.Close();
        }
    }
}
