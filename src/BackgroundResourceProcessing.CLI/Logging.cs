using System;

namespace BackgroundResourceProcessing.CLI
{
    class SilentLogSink : LogUtil.ILogSink
    {
        VerboseLogSink sink = new();

        public void Log(string message) { }

        public void Error(string message)
        {
            sink.Error(message);
        }

        public void Warn(string message)
        {
            sink.Warn(message);
        }
    }

    class VerboseLogSink : LogUtil.ILogSink
    {
        public void Error(string message)
        {
            Console.WriteLine($"[ERROR] {message}");
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
}
