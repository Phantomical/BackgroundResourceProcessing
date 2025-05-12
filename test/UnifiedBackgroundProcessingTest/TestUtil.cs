using System.Runtime.CompilerServices;
using UnifiedBackgroundProcessing;
using UnifiedBackgroundProcessing.Core;

namespace UnifiedBackgroundProcessingTest
{
    internal class TestUtil
    {
        public class TestLogErrorException(string message) : Exception(message) { }

        private class TestLogSink : LogUtil.ILogSink
        {
            public void Error(string message)
            {
                throw new TestLogErrorException(message);
            }

            public void Log(string message)
            {
                Console.WriteLine($"[INFO]  {message}");
            }

            public void Warn(string message)
            {
                Console.WriteLine($"[WARN]  {message}");
            }
        }

        static TestUtil()
        {
            LogUtil.Sink = new TestLogSink();
            Registrar.RegisterAllBehaviours(typeof(Registrar).Assembly);
        }

        private static string GetCallerFilePath([CallerFilePath] string? callerFilePath = null)
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
            var configNode = ConfigNode.Load(configPath);
            var processor = new ResourceProcessor();
            processor.Load(configNode.GetNode("BRP_SHIP"));
            return processor;
        }
    }
}
