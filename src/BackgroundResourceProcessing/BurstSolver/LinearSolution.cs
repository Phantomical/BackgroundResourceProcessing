using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using BackgroundResourceProcessing.Collections.Burst;
using BackgroundResourceProcessing.Utils;
using Unity.Collections;

namespace BackgroundResourceProcessing.BurstSolver;

[DebuggerDisplay("Count = {Count}")]
[DebuggerTypeProxy(typeof(DebugView))]
internal struct LinearSolution(RawArray<double> values)
{
    RawArray<double> values = values;

    public readonly int Count => values.Count;

    public readonly double this[int index] => values[index];
    public readonly double this[Variable var] => this[var.Index] * var.Coef;

    public readonly double Evaluate(Variable var) => this[var];

    public readonly double Evaluate(LinearEquation eq)
    {
        double value = 0.0;
        foreach (var var in eq)
            value = MathUtil.Fma(values[var.Index], var.Coef, value);
        return value;
    }

    public override readonly string ToString()
    {
        return ToString("G");
    }

    public readonly string ToString(string fmt)
    {
        StringBuilder builder = new();
        bool first = true;

        for (int i = 0; i < values.Length; ++i)
        {
            if (first)
                first = false;
            else
                builder.Append(", ");

            builder.Append(new Variable(i));
            builder.Append(" = ");
            builder.Append(this[i].ToString(fmt));
        }

        return builder.ToString();
    }

    class DebugView
    {
        public DebugView(LinearSolution soln)
        {
            KeyValuePair<Variable, double>[] array = new KeyValuePair<Variable, double>[soln.Count];

            for (int i = 0; i < soln.values.Length; ++i)
                array[i] = new(new(i), soln.values[i]);

            Items = array;
        }

        [DebuggerBrowsable(DebuggerBrowsableState.RootHidden)]
        public KeyValuePair<Variable, double>[] Items { get; }
    }
}
