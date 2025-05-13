using System;
using System.Collections.Generic;

namespace BackgroundResourceProcessing.Collections
{
    public class UnionFind
    {
        private readonly List<int> values;

        public int Count => values.Count;

        public UnionFind(int capacity)
        {
            values = new List<int>(capacity);

            for (int i = 0; i < capacity; ++i)
            {
                values[i] = i;
            }
        }

        /// <summary>
        /// Get the canonical index associated with <paramref name="x"/>.
        /// </summary>
        /// <param name="x"></param>
        public int Find(int x)
        {
            var current = x;

            while (current != values[current])
            {
                current = values[current];
            }

            values[x] = current;
            return current;
        }

        /// <summary>
        /// Union the two sets containing
        /// </summary>
        /// <param name="a"></param>
        /// <param name="b"></param>
        /// <returns></returns>
        public int Union(int a, int b)
        {
            a = Find(a);
            b = Find(b);

            var min = Math.Min(a, b);
            var max = Math.Max(a, b);

            values[max] = min;
            return min;
        }

        /// <summary>
        /// Introduce a new set.
        /// </summary>
        /// <returns>The canonical index of the new set.</returns>
        public int Add()
        {
            var next = values.Count;
            values.Add(next);
            return next;
        }
    }
}
