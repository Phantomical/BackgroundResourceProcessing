using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;

namespace BackgroundResourceProcessing.Tracing;

internal class Trace : IDisposable
{
    public static Trace Active { get; private set; }

    readonly object mutex = new();
    readonly StreamWriter stream;
    readonly Stopwatch watch = new();

    private Trace(StreamWriter stream)
    {
        this.stream = stream;
        stream.WriteLine("[");

        watch.Start();
    }

    public static Trace Start(string filename)
    {
        if (Active != null)
            throw new Exception("Cannot start a new trace when one is already active");

        Trace trace = new(File.CreateText(filename));
        Active = trace;
        return trace;
    }

    public TimeSpan GetCurrentTime()
    {
        return watch.Elapsed;
    }

    public void WriteEvent(string label, TimeSpan start, TimeSpan end)
    {
        var startUs = start.TotalMicroseconds();
        var endUs = end.TotalMicroseconds();
#pragma warning disable CS0618 // Type or member is obsolete
        var tid = AppDomain.GetCurrentThreadId();
#pragma warning restore CS0618 // Type or member is obsolete

        var evt = new CompleteEvent()
        {
            name = label,
            ts = startUs,
            dur = endUs - startUs,
            pid = 0,
            tid = tid,
        };

        if (evt.name.Contains("\""))
            evt.name = evt.name.Replace("\"", "\\\"");

        string json =
            $"{{\"ph\":\"X\",\"name\":\"{evt.name}\",\"ts\":{evt.ts},\"dur\":{evt.dur},\"pid\":0,\"tid\":{evt.tid}}},";

        lock (mutex)
        {
            stream.WriteLine(json);
        }
    }

    public void Dispose()
    {
        lock (mutex)
        {
            stream.Close();
        }
    }

    [Serializable]
    private struct CompleteEvent
    {
        public string name;
        public long ts;
        public long dur;
        public int pid;
        public int tid;
    }
}

#if TRACING
internal struct TraceSpan : IDisposable
{
    string label;
    TimeSpan start;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public TraceSpan(string label)
    {
        var trace = Trace.Active;
        if (trace == null)
            return;

        Setup(label, trace);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public TraceSpan(Func<string> labelfn)
    {
        var trace = Trace.Active;
        if (trace == null)
            return;

        Setup(labelfn, trace);
    }

    private void Setup(Func<string> labelfn, Trace trace)
    {
        Setup(labelfn(), trace);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void Setup(string label, Trace trace)
    {
        this.label = label;
        this.start = trace.GetCurrentTime();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly void Dispose()
    {
        if (label == null)
            return;

        DoDispose();
    }

    private readonly void DoDispose()
    {
        var trace = Trace.Active;
        if (trace == null)
            return;

        var end = trace.GetCurrentTime();
        trace.WriteEvent(label, start, end);
    }
}
#else
internal struct TraceSpan : IDisposable
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public TraceSpan(string _) { }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public TraceSpan(Func<string> _) { }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Dispose() { }
}
#endif

internal static class TimeSpanExt
{
    private const long TicksPerMicrosecond = TimeSpan.TicksPerMillisecond / 1000;

    public static long TotalMicroseconds(this TimeSpan span)
    {
        return span.Ticks / TicksPerMicrosecond;
    }
}
