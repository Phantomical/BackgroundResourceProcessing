using System;
using System.Collections;
using System.Collections.Generic;

namespace BackgroundResourceProcessing.Collections;

internal static class EnumerableExtensions
{
    /// <summary>
    /// Determine the ordering between two enumerable sequences.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="a"></param>
    /// <param name="b"></param>
    /// <returns></returns>
    public static int SequenceCompareTo<T>(IEnumerable<T> a, IEnumerable<T> b)
        where T : IComparable<T>
    {
        var it1 = a.GetEnumerator();
        var it2 = b.GetEnumerator();

        while (true)
        {
            var next1 = it1.MoveNext();
            var next2 = it2.MoveNext();

            if (next1 && !next2)
                return 1;
            if (!next1 && next2)
                return -1;
            if (!next1 && !next2)
                return 0;

            var cmp = it1.Current.CompareTo(it2.Current);
            if (cmp != 0)
                return cmp;
        }
    }

    public static IEnumerable<R> TrySelect<T, R>(
        this IEnumerable<T> source,
        TrySelectFilter<T, R> func
    ) => new TrySelectEnumerable<T, R>(source, func);

    public delegate bool TrySelectFilter<T, R>(T value, out R result);

    class TrySelectEnumerable<T, R>(IEnumerable<T> inner, TrySelectFilter<T, R> filter)
        : IEnumerable<R>
    {
        readonly IEnumerable<T> inner = inner;
        readonly TrySelectFilter<T, R> filter = filter;

        public IEnumerator<R> GetEnumerator() =>
            new TrySelectEnumerator<T, R>(inner.GetEnumerator(), filter);

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    class TrySelectEnumerator<T, R>(IEnumerator<T> inner, TrySelectFilter<T, R> filter)
        : IEnumerator<R>
    {
        readonly IEnumerator<T> inner = inner;
        readonly TrySelectFilter<T, R> filter = filter;
        R value = default;

        public R Current => value;

        object IEnumerator.Current => Current;

        public bool MoveNext()
        {
            while (true)
            {
                if (!inner.MoveNext())
                    return false;

                if (!filter(inner.Current, out value))
                    continue;

                return true;
            }
        }

        public void Dispose() => inner.Dispose();

        public void Reset() => inner.Reset();
    }
}
