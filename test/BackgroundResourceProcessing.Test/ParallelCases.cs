using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using System.Threading.Tasks;

namespace BackgroundResourceProcessing.Test;

/// <summary>
/// Runs independent test cases in parallel.
/// </summary>
///
/// <remarks>
/// Generate the inputs for every case up front on the calling thread (so that
/// seeded random inputs stay the same), then compute the results here and
/// look at them afterwards in order. The case body must not touch Unity or
/// any shared mutable state, and must not log: logging from a background
/// thread can crash the game.
/// </remarks>
internal static class ParallelCases
{
    /// <summary>
    /// Compute <paramref name="body"/> for every index in
    /// <c>[0, count)</c> and return the results in index order.
    /// </summary>
    ///
    /// <remarks>
    /// The first case runs on the calling thread before the rest start, so
    /// that anything initialized lazily (such as Burst function pointers) is
    /// set up on the main thread.
    /// </remarks>
    public static T[] Run<T>(int count, Func<int, T> body)
    {
        var results = new T[count];
        if (count == 0)
            return results;

        results[0] = body(0);
        try
        {
            Parallel.For(1, count, i => results[i] = body(i));
        }
        catch (AggregateException e)
        {
            ExceptionDispatchInfo.Capture(e.Flatten().InnerExceptions[0]).Throw();
        }

        return results;
    }

    /// <summary>
    /// Run every case, and return the messages of those that failed, in
    /// order. A case fails by returning a non-null message.
    /// </summary>
    public static List<string> Failures(List<Func<string>> cases)
    {
        var failures = new List<string>();
        foreach (var message in Run(cases.Count, i => cases[i]()))
        {
            if (message is not null)
                failures.Add(message);
        }

        return failures;
    }
}
