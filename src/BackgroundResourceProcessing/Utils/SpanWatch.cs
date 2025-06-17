using System;
using System.Diagnostics;

namespace BackgroundResourceProcessing.Utils
{
    /// <summary>
    /// An internal helper class to measure a timespan and then log it.
    /// </summary>
    internal struct SpanWatch : IDisposable
    {
        Stopwatch stopwatch;
        Action<TimeSpan> func;

        public SpanWatch(Action<TimeSpan> func)
        {
            this.func = func;
            stopwatch = new Stopwatch();
            stopwatch.Start();
        }

        public void Dispose()
        {
            stopwatch.Stop();
            func(stopwatch.Elapsed);
        }

        internal static string FormatDuration(TimeSpan span)
        {
            const long TicksPerMicrosecond = TimeSpan.TicksPerMillisecond / 1000;

            if (span.TotalSeconds >= 1)
                return $"{span:g}";

            if (span.TotalMilliseconds >= 100)
                return $"{span:fff} ms";

            var micros = span.Ticks / TicksPerMicrosecond % 1000;
            var millis = span.Milliseconds;

            return $"{millis:D}.{micros:D3} ms";
        }
    }
}
