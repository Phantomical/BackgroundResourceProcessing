using System;
using System.Diagnostics;
using System.Text;
using UnityEngine;

namespace BackgroundResourceProcessing
{
    internal static class LogUtil
    {
#if DEBUG
        public static ILogSink Sink { get; set; } = new UnityLogSink();
#else
        public static ILogSink Sink => new UnityLogSink();
#endif

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

#if !DEBUG
        [Conditional("FALSE")]
#endif
        internal static void Debug(DebugExpression dbgexpr)
        {
#if DEBUG
            // Log(dbgexpr());
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
