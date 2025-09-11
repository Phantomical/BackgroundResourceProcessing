using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using BackgroundResourceProcessing.Collections.Burst;
using BackgroundResourceProcessing.Utils;
using Unity.Burst.CompilerServices;

namespace BackgroundResourceProcessing.BurstSolver;

internal struct LinearEquation(RawList<double> values)
    : IEnumerable<Variable>,
        IEquatable<LinearEquation>,
        IComparable<LinearEquation>
{
    RawList<double> values = values;

    public readonly int Capacity => values.Count;
    public readonly int Count
    {
        get
        {
            int count = 0;
            foreach (double value in values)
            {
                if (value != 0.0)
                    count += 1;
            }
            return count;
        }
    }

    public readonly MemorySpan<double> Span => values.Span;

    public LinearEquation()
        : this([]) { }

    public LinearEquation(int capacity, AllocatorHandle allocator)
        : this(new RawList<double>(capacity, allocator)) { }

    public LinearEquation(Variable var, AllocatorHandle allocator)
        : this(var.Index + 1, allocator)
    {
        Add(var);
    }

    public LinearEquation(MemorySpan<double> values, AllocatorHandle allocator)
        : this(new RawList<double>(values, allocator)) { }

    public void Add(Variable var)
    {
        if (var.Index >= Capacity)
        {
            if (var.Coef == 0.0)
                return;
            Reserve(var.Index + 1);
        }

        values[var.Index] += var.Coef;
    }

    public void Add(LinearEquation eq)
    {
        int mincap = Math.Min(eq.Capacity, Capacity);
        int i = 0;

        for (; i < mincap; ++i)
            values[i] += eq.values[i];

        for (; i < eq.Capacity; ++i)
        {
            if (eq.values[i] == 0.0)
                continue;

            Reserve(eq.Capacity);
            for (; i < eq.Capacity; ++i)
                values[i] += eq.values[i];
            break;
        }
    }

    public void Sub(Variable var) => Add(-var);

    public void Sub(LinearEquation eq)
    {
        int mincap = Math.Min(eq.Capacity, Capacity);
        int i = 0;

        for (; i < mincap; ++i)
            values[i] -= eq.values[i];

        for (; i < eq.Capacity; ++i)
        {
            if (eq.values[i] == 0.0)
                continue;

            Reserve(eq.Capacity);
            for (; i < eq.Capacity; ++i)
                values[i] -= eq.values[i];
            break;
        }
    }

    public readonly void Negate()
    {
        for (int i = 0; i < Capacity; ++i)
            values[i] = -values[i];
    }

    public void Clear() => values.Clear();

    [IgnoreWarning(1370)]
    public readonly void CopyTo(MemorySpan<double> dst)
    {
        if (!Span.TryCopyTo(dst))
            throw new InvalidOperationException(
                "destination was too small to store all equation coefficients"
            );
    }

    public void Set(MemorySpan<double> values)
    {
        int min = Math.Min(Capacity, values.Length);
        int i = 0;
        for (; i < min; ++i)
            this.values[i] = values[i];
        if (Capacity < values.Length)
        {
            for (; i < values.Length; ++i)
            {
                if (values[i] == 0.0)
                    continue;

                Reserve(values.Length);

                for (; i < values.Length; ++i)
                    this.values[i] = values[i];
                break;
            }
        }
        else
        {
            for (; i < Capacity; ++i)
                this.values[i] = 0.0;
        }
    }

    public readonly LinearEquation Clone() => new(values.Clone());

    public void Reserve(int newcap)
    {
        if (newcap < Capacity)
            return;

        values.Resize(newcap);
    }

    #region Operators
    public static LinearConstraint operator <=(LinearEquation eq, double constant) =>
        new(eq, Relation.LEqual, constant);

    public static LinearConstraint operator >=(LinearEquation eq, double constant) =>
        new(eq, Relation.GEqual, constant);

    public static LinearConstraint operator ==(LinearEquation eq, double constant) =>
        new(eq, Relation.Equal, constant);

    public static LinearConstraint operator !=(LinearEquation eq, double constant) =>
        throw new NotImplementedException();
    #endregion


    #region IComparable
    public readonly int CompareTo(LinearEquation other)
    {
        int mincap = Math.Min(Capacity, other.Capacity);
        int i = 0;

        for (; i < mincap; ++i)
        {
            int comp = values[i].CompareTo(other.values[i]);
            if (comp != 0)
                return comp;
        }

        if (Capacity > other.Capacity)
        {
            for (; i < Capacity; ++i)
            {
                int comp = values[i].CompareTo(0.0);
                if (comp != 0)
                    return comp;
            }
        }
        else
        {
            for (; i < other.Capacity; ++i)
            {
                int comp = 0.0.CompareTo(other.values[i]);
                if (comp != 0)
                    return comp;
            }
        }

        return 0;
    }
    #endregion

    #region IEquatable
    public readonly bool Equals(LinearEquation other)
    {
        int mincap = Math.Min(Capacity, other.Capacity);
        int i = 0;

        for (; i < mincap; ++i)
        {
            if (values[i] != other.values[i])
                return false;
        }

        if (Capacity > other.Capacity)
        {
            for (; i < Capacity; ++i)
            {
                if (values[i] != 0.0)
                    return false;
            }
        }
        else
        {
            for (; i < other.Capacity; ++i)
            {
                if (values[i] != 0.0)
                    return false;
            }
        }

        return true;
    }
    #endregion

    public override readonly bool Equals(object obj)
    {
        if (obj is LinearEquation eq)
            return Equals(eq);

        return false;
    }

    public override readonly int GetHashCode()
    {
        HashCode hasher = new();
        hasher.AddAll(values.Span);
        return hasher.GetHashCode();
    }

    #region IEnumerable
    public readonly Enumerator GetEnumerator() => new(this);

    readonly IEnumerator<Variable> IEnumerable<Variable>.GetEnumerator() => GetEnumerator();

    readonly IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public struct Enumerator(LinearEquation variables)
        : IEnumerator<Variable>,
            IEnumerable<Variable>
    {
        readonly RawList<double> values = variables.values;
        int index = -1;

        public readonly Variable Current => new(index, values[index]);

        readonly object IEnumerator.Current => Current;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool MoveNext()
        {
            while (true)
            {
                index += 1;
                if (index >= values.Count)
                    return false;
                if (values[index] != 0.0)
                    return true;
            }
        }

        public readonly void Dispose() { }

        public void Reset()
        {
            index = -1;
        }

        public readonly Enumerator GetEnumerator() => this;

        readonly IEnumerator<Variable> IEnumerable<Variable>.GetEnumerator() => GetEnumerator();

        readonly IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
    #endregion

    #region ToString
    public override readonly string ToString()
    {
        StringBuilder builder = new();
        bool first = true;
        foreach (var (index, coef) in this)
            BackgroundResourceProcessing.Solver.LinearProblem.RenderCoef(
                builder,
                coef,
                new Variable(index).ToString(),
                ref first
            );

        if (first)
            builder.Append("0");

        return builder.ToString();
    }
    #endregion
}
