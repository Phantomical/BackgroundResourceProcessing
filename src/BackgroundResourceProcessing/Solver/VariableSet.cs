using System;
using System.Collections;
using System.Collections.Generic;

namespace BackgroundResourceProcessing.Solver
{
    /// <summary>
    /// A helper type returned for a set of variables.
    /// </summary>
    internal struct VariableSet(int start, int count) : IEnumerable<Variable>
    {
        public readonly int Start => start;
        public readonly int Count => count;

        public readonly Variable this[int index]
        {
            get
            {
                if (index < 0)
                    throw new IndexOutOfRangeException($"index out of range: {index} < 0");
                if (index >= count)
                    throw new IndexOutOfRangeException($"index out of range: {index} >= {count}");

                return new(Start + index);
            }
        }

        public readonly IEnumerator<Variable> GetEnumerator()
        {
            return new Enumerator(Start, Count);
        }

        readonly IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        private class Enumerator(int start, int count) : IEnumerator<Variable>
        {
            readonly int start = start;
            readonly int end = start + count;
            int? index = null;

            public Variable Current => new((int)index);

            object IEnumerator.Current => Current;

            public bool MoveNext()
            {
                if (index == null)
                    index = start;
                else
                    index += 1;

                return index < end;
            }

            public void Reset()
            {
                index = null;
            }

            public void Dispose() { }
        }
    }
}
