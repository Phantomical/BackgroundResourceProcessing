using System;
using System.Collections;
using System.Collections.Generic;

namespace BackgroundResourceProcessing.Solver
{
    /// <summary>
    /// A helper type returned for a set of variables.
    /// </summary>
    internal struct VariableSet(uint start, uint count) : IEnumerable<Variable>
    {
        public readonly uint Start => start;
        public readonly int Count => (int)count;

        public readonly Variable this[uint index]
        {
            get
            {
                if (index >= count)
                    throw new IndexOutOfRangeException($"index out of range: {index} >= {count}");

                return new(Start + index);
            }
        }

        public readonly Variable this[int index]
        {
            get
            {
                if (index < 0)
                    throw new IndexOutOfRangeException($"index out of range: {index} < 0");

                return this[(uint)index];
            }
        }

        public readonly IEnumerator<Variable> GetEnumerator()
        {
            return new Enumerator(Start, (uint)Count);
        }

        readonly IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        private class Enumerator(uint start, uint count) : IEnumerator<Variable>
        {
            readonly uint start = start;
            readonly uint end = start + count;
            uint? index = null;

            public Variable Current => new((uint)index);

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
