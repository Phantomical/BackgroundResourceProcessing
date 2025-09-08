using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Burst.CompilerServices;

namespace BackgroundResourceProcessing.BurstSolver;

/// <summary>
/// A helper type returned for a set of variables.
/// </summary>
internal struct VariableSet(int start, int count) : IEnumerable<Variable>
{
    public readonly int Start => start;
    public readonly int Count => count;

    public readonly Variable this[int index]
    {
        [IgnoreWarning(1370)]
        get
        {
            if (index < 0)
                throw new IndexOutOfRangeException($"index out of range: {index} < 0");
            if (index >= count)
                throw new IndexOutOfRangeException($"index out of range: {index} >= {count}");

            return new(Start + index);
        }
    }

    public readonly Enumerator GetEnumerator() => new(Start, Count);

    readonly IEnumerator<Variable> IEnumerable<Variable>.GetEnumerator() => GetEnumerator();

    readonly IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public struct Enumerator(int start, int count) : IEnumerator<Variable>
    {
        readonly int start = start;
        readonly int end = start + count;
        int? index = null;

        public readonly Variable Current => new((int)index);

        readonly object IEnumerator.Current => Current;

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
