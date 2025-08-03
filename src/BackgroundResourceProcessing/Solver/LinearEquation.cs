using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows.Markup;
using BackgroundResourceProcessing.Collections;

#pragma warning disable CS0660 // Type defines operator == or operator != but does not override Object.Equals(object o)
#pragma warning disable CS0661 // Type defines operator == or operator != but does not override Object.GetHashCode()

namespace BackgroundResourceProcessing.Solver;

internal class LinearEquation(double[] values) : IEnumerable<Variable>, IComparable<LinearEquation>
{
    double[] values = values;

    public int Capacity => values.Length;

    public int Count
    {
        get
        {
            int count = 0;
            for (int i = 0; i < values.Length; ++i)
                if (values[i] != 0.0)
                    count += 1;
            return count;
        }
    }

    public KeyList Keys => new(this);
    public Enumerator Values => GetEnumerator();

    public Variable this[int index]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get { return new(index, values[index]); }
    }

    public LinearEquation()
        : this([]) { }

    public LinearEquation(int capacity)
        : this(new double[capacity]) { }

    public LinearEquation(Variable var)
        : this(var.Index + 1)
    {
        values[var.Index] = var.Coef;
    }

    public void Add(Variable var)
    {
        if (var.Index >= values.Length)
            Expand(var.Index);

        values[var.Index] += var.Coef;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Sub(Variable var)
    {
        Add(-var);
    }

    public void Remove(int index)
    {
        if (index >= values.Length)
            return;

        values[index] = 0.0;
    }

    public void Clear()
    {
        for (int i = 0; i < values.Length; ++i)
            values[i] = 0.0;
    }

    public bool TryGetValue(int index, out Variable var)
    {
        if (index < 0 || index >= values.Length)
        {
            var = default;
            return false;
        }

        var = new(index, values[index]);
        return var.Coef != 0.0;
    }

    public bool Contains(int index)
    {
        if (index < 0 || index >= values.Length)
            return false;

        return values[index] != 0.0;
    }

    public bool Contains(Variable var)
    {
        return Contains(var.Index);
    }

    /// <summary>
    /// Create a copy of this <see cref="LinearEquation"/> with an
    /// independent backing list.
    /// </summary>
    /// <returns></returns>
    public LinearEquation Clone()
    {
        LinearEquation copy = new(Capacity);
        for (int i = 0; i < values.Length; ++i)
            copy.values[i] = values[i];
        return copy;
    }

    public static LinearEquation operator +(LinearEquation eq, Variable var)
    {
        eq.Add(var);
        return eq;
    }

    public static LinearEquation operator -(LinearEquation eq, Variable var)
    {
        eq.Sub(var);
        return eq;
    }

    public static LinearEquation operator +(LinearEquation a, LinearEquation b)
    {
        if (a.Capacity < b.Capacity)
            (a, b) = (b, a);

        for (int i = 0; i < b.values.Length; ++i)
            a.values[i] += b.values[i];

        return a;
    }

    public static LinearEquation operator -(LinearEquation a, LinearEquation b)
    {
        if (a.Capacity >= b.Capacity)
        {
            for (int i = 0; i < b.values.Length; ++i)
                a.values[i] -= b.values[i];

            return a;
        }
        else
        {
            for (int i = 0; i < a.values.Length; ++i)
                b.values[i] = -b.values[i] + a.values[i];
            for (int i = a.values.Length; i < b.values.Length; ++i)
                b.values[i] = -b.values[i];

            return b;
        }
    }

    public static LinearEquation operator *(LinearEquation eq, double value)
    {
        for (int i = 0; i < eq.values.Length; ++i)
            eq.values[i] *= value;
        return eq;
    }

    public static LinearEquation operator /(LinearEquation eq, double value)
    {
        for (int i = 0; i < eq.values.Length; ++i)
            eq.values[i] /= value;
        return eq;
    }

    public static LinearConstraint operator ==(LinearEquation eq, double value)
    {
        return new LinearConstraint(eq, Relation.Equal, value);
    }

    public static LinearConstraint operator !=(LinearEquation eq, double value)
    {
        throw new NotImplementedException("Cannot optimize != constraints");
    }

    public static LinearConstraint operator <=(LinearEquation eq, double value)
    {
        return new LinearConstraint(eq, Relation.LEqual, value);
    }

    public static LinearConstraint operator >=(LinearEquation eq, double value)
    {
        return new LinearConstraint(eq, Relation.GEqual, value);
    }

    public LinearEquation Negated()
    {
        var eq = new LinearEquation(Capacity);
        for (int i = 0; i < values.Length; ++i)
            eq.values[i] = -values[i];
        return eq;
    }

    public override string ToString()
    {
        StringBuilder builder = new();

        bool first = true;
        foreach (var (index, coef) in this)
            LinearProblem.RenderCoef(builder, coef, new Variable(index).ToString(), ref first);

        return builder.ToString();
    }

    private void Expand(int needed)
    {
        if (needed < values.Length)
            return;

        var newcap = Math.Max(values.Length * 2, needed + 1);
        var newvals = new double[newcap];

        for (int i = 0; i < values.Length; ++i)
            newvals[i] = values[i];

        values = newvals;
    }

    public Enumerator GetEnumerator()
    {
        return new Enumerator(this);
    }

    IEnumerator<Variable> IEnumerable<Variable>.GetEnumerator()
    {
        return new BoxedEnumerator(values);
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return ((IEnumerable<Variable>)this).GetEnumerator();
    }

    public int CompareTo(LinearEquation other)
    {
        var it1 = GetEnumerator();
        var it2 = other.GetEnumerator();

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

    public ref struct Enumerator(LinearEquation eq) : IEnumerator<Variable>
    {
        readonly double[] values = eq.values;
        int index = -1;

        public readonly Variable Current
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get { return new(index, values[index]); }
        }

        readonly object IEnumerator.Current => Current;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool MoveNext()
        {
            while (true)
            {
                index += 1;

                if (index >= values.Length)
                    return false;
                if (values[index] != 0.0)
                    return true;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly void Dispose() { }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Reset()
        {
            index = -1;
        }
    }

    class BoxedEnumerator(double[] values) : IEnumerator<Variable>
    {
        readonly double[] values = values;
        int index = -1;

        public Variable Current
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get { return new(index, values[index]); }
        }

        object IEnumerator.Current => Current;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool MoveNext()
        {
            while (true)
            {
                index += 1;

                if (index >= values.Length)
                    return false;
                if (values[index] != 0.0)
                    return true;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Dispose() { }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Reset()
        {
            index = -1;
        }
    }

    public readonly struct KeyList(LinearEquation eq) : IEnumerable<int>
    {
        readonly double[] values = eq.values;

        public Enumerator GetEnumerator()
        {
            return new(this);
        }

        IEnumerator<int> IEnumerable<int>.GetEnumerator()
        {
            return new BoxedEnumerator(this);
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return new BoxedEnumerator(this);
        }

        public ref struct Enumerator(KeyList keys) : IEnumerator<int>
        {
            readonly double[] values = keys.values;
            int index = -1;

            public readonly int Current => index;
            readonly object IEnumerator.Current => Current;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool MoveNext()
            {
                while (true)
                {
                    index += 1;

                    if (index >= values.Length)
                        return false;
                    if (values[index] != 0.0)
                        return true;
                }
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public readonly void Dispose() { }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Reset()
            {
                index = -1;
            }
        }

        struct BoxedEnumerator(KeyList keys) : IEnumerator<int>
        {
            readonly double[] values = keys.values;
            int index = -1;

            public readonly int Current => index;
            readonly object IEnumerator.Current => Current;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool MoveNext()
            {
                while (true)
                {
                    index += 1;

                    if (index >= values.Length)
                        return false;
                    if (values[index] != 0.0)
                        return true;
                }
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public readonly void Dispose() { }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Reset()
            {
                index = -1;
            }
        }
    }
}
