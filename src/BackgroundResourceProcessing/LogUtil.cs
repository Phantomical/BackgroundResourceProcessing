using System;
using System.Text;
using UnityEngine;

namespace BackgroundResourceProcessing
{
    public static class LogUtil
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
                Debug.LogError(message);
            }

            public void Log(string message)
            {
                Debug.Log(message);
            }

            public void Warn(string message)
            {
                Debug.LogWarning(message);
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
