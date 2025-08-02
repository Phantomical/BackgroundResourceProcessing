using System;

namespace BackgroundResourceProcessing.Collections;

internal static class SpanExtensions
{
    public static int IndexOfNotEqual<T>(this Span<T> span, T value)
        where T : IEquatable<T>
    {
        for (int i = 0; i < span.Length; ++i)
        {
            if (!span[i].Equals(value))
                return i;
        }

        return -1;
    }
}
