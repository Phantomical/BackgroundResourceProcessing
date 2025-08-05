using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace BackgroundResourceProcessing.Collections;

internal class UnionFind
{
    private readonly List<int> values;

    public int Count => values.Count;
#if DEBUG
    public List<int> Values => values;
#endif

    public UnionFind(int capacity)
    {
        values = new List<int>(capacity);

        for (int i = 0; i < capacity; ++i)
        {
            values.Add(i);
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
            current = values[current];

        values[x] = current;
        return current;
    }

    /// <summary>
    /// Union the two sets containing <paramref name="a"/> and <paramref name="b"/>.
    /// </summary>
    /// <param name="a"></param>
    /// <param name="b"></param>
    /// <returns></returns>
    public int Union(int a, int b)
    {
        a = Find(a);
        b = Find(b);

        if (a > b)
            (a, b) = (b, a);

        values[b] = a;
        return a;
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

    public void Canonicalize()
    {
        for (int i = 0; i < values.Count; ++i)
            values[i] = Find(values[i]);
    }

    /// <summary>
    /// Get an enumerator over all the elements in the same class as <paramref name="cls"/>.
    /// </summary>
    /// <param name="cls"></param>
    /// <returns></returns>
    public ClassEnumerator GetClassEnumerator(int cls)
    {
        return new(this, Find(cls));
    }

    public ref struct ClassEnumerator(UnionFind unf, int cls) : IEnumerator<int>
    {
        readonly UnionFind unf = unf;
        readonly int cls = cls;
        int index = -1;

        public readonly int Current
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get { return index; }
        }

        readonly object IEnumerator.Current => Current;

        public bool MoveNext()
        {
            while (true)
            {
                index += 1;

                if (index >= unf.Count)
                    return false;
                if (unf.Find(index) == cls)
                    return true;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly void Dispose() { }

        public void Reset()
        {
            index = -1;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly int Count()
        {
            int count = 0;
            var copy = this;
            while (copy.MoveNext())
                count += 1;
            return count;
        }

        public readonly ClassEnumerator GetEnumerator()
        {
            return this;
        }
    }
}
