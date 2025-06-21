using System.Text;

namespace BackgroundResourceProcessing
{
    internal static class LogUtil
    {
        public static ILogSink Sink = new UnityLogSink();

        public interface ILogSink
        {
            void Log(string message);
            void Warn(string message);
            void Error(string message);
        }

        internal struct UnityLogSink : ILogSink
        {
            public void Error(string message)
            {
                UnityEngine.Debug.LogError(message);
            }

            public void Log(string message)
            {
                UnityEngine.Debug.Log(message);
            }

            public void Warn(string message)
            {
                UnityEngine.Debug.LogWarning(message);
            }
        }

        static string BuildMessage(object[] args)
        {
            var builder = new StringBuilder("[BackgroundResourceProcessing] ");
            foreach (var arg in args)
            {
                builder.Append(arg.ToString());
            }
            return builder.ToString();
        }

        public delegate string DebugExpression();

        internal static void Debug(DebugExpression dbgexpr)
        {
#if DEBUG
            Log(dbgexpr());
#endif
        }

        public static void Log(params object[] args)
        {
            Sink.Log(BuildMessage(args));
        }

        public static void Warn(params object[] args)
        {
            Sink.Warn(BuildMessage(args));
        }

        public static void Error(params object[] args)
        {
            Sink.Error(BuildMessage(args));
        }
    }
}
