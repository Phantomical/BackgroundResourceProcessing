using System.CodeDom;
using System.Runtime.CompilerServices;
using BackgroundResourceProcessing;
using BackgroundResourceProcessing.Core;

namespace BackgroundResourceProcessing.Test
{
    internal static class TestUtil
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
    }
}
