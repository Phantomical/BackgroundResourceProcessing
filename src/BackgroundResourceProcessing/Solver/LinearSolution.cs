using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace BackgroundResourceProcessing.Solver;

/// <summary>
/// A solution to a linear problem, containing the values assigned to the
/// variables making up the problem.
/// </summary>
[DebuggerTypeProxy(typeof(DebugView))]
[DebuggerDisplay("Count = {Count}")]
internal readonly struct LinearSolution(double[] values)
{
    readonly double[] values = values;

    public readonly int Count => values == null ? 0 : values.Length;

    public readonly double this[int index] => values[index];
    public readonly double this[uint index] => values[index];
    public readonly double this[Variable var] => this[var.Index] * var.Coef;

    public readonly double Evaluate(Variable var)
    {
        return this[var];
    }

    public readonly double Evaluate(LinearEquation eq)
    {
        double value = 0.0;
        foreach (var var in eq)
            value += this[var];
        return value;
    }

    public override string ToString()
    {
        return ToString("G");
    }

    public string ToString(string fmt)
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

    class DebugView(LinearSolution soln)
    {
        private readonly LinearSolution soln = soln;

        [DebuggerBrowsable(DebuggerBrowsableState.RootHidden)]
        public KeyValuePair<Variable, double>[] Items
        {
            get
            {
                if (soln.values == null)
                    return [];

                KeyValuePair<Variable, double>[] array = new KeyValuePair<Variable, double>[
                    soln.Count
                ];

                for (int i = 0; i < soln.values.Length; ++i)
                    array[i] = new(new(i), soln.values[i]);

                return array;
            }
        }
    }
}
