using System;
using System.Collections.Generic;

namespace BackgroundResourceProcessing.Collections
{
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
    }
}
