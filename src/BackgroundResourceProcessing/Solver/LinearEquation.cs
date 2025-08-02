using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using BackgroundResourceProcessing.Collections;

#pragma warning disable CS0660 // Type defines operator == or operator != but does not override Object.Equals(object o)
#pragma warning disable CS0661 // Type defines operator == or operator != but does not override Object.GetHashCode()

namespace BackgroundResourceProcessing.Solver;

internal class LinearEquation(SortedList<int, double> variables)
    : IEnumerable<Variable>,
        IComparable<LinearEquation>
{
    readonly SortedList<int, double> variables = variables;

    public int Count => variables.Count;

    public IEnumerable<Variable> Values =>
        variables.Select(entry => new Variable(entry.Key, entry.Value));

    public Variable this[int index]
    {
        get { return new(index, variables[index]); }
    }

    public LinearEquation()
        : this([]) { }

    public LinearEquation(int capacity)
        : this(new SortedList<int, double>(capacity)) { }

    public LinearEquation(Variable var)
        : this(new SortedList<int, double>(1))
    {
        Add(var);
    }

    public void Add(Variable var)
    {
        var coef = var.Coef;
        if (variables.TryGetValue(var.Index, out var existing))
            coef = var.Coef + existing;

        if (coef == 0.0)
            variables.Remove(var.Index);
        else
            variables[var.Index] = coef;
    }

    public void Sub(Variable var)
    {
        Add(-var);
    }

    public bool Remove(int index)
    {
        return variables.Remove(index);
    }

    public void Clear()
    {
        variables.Clear();
    }

    public bool TryGetValue(int index, out Variable var)
    {
        var result = variables.TryGetValue(index, out var value);
        var = new(index, value);
        return result;
    }

    public bool Contains(int index)
    {
        return variables.ContainsKey(index);
    }

    public bool Contains(Variable var)
    {
        return Contains(var.Index);
    }

    public void Substitute(int index, LinearEquation eq)
    {
        if (!TryGetValue(index, out var variable))
            return;
        if (eq.Contains(variable))
            throw new ArgumentException(
                $"Attempted to substitue variable {variable} with equation {eq} containing {variable}"
            );

        variables.Remove(index);
        foreach (var var in eq)
            Add(var * variable.Coef);
    }

    /// <summary>
    /// Create a copy of this <see cref="LinearEquation"/> with an
    /// independent backing list.
    /// </summary>
    /// <returns></returns>
    public LinearEquation Clone()
    {
        return new(new SortedList<int, double>(variables));
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
        foreach (var (index, coef) in b.variables)
            a.Add(new(index, coef));
        return a;
    }

    public static LinearEquation operator -(LinearEquation a, LinearEquation b)
    {
        foreach (var (index, coef) in b.variables)
            a.Sub(new(index, coef));
        return a;
    }

    public static LinearEquation operator *(LinearEquation eq, double value)
    {
        foreach (var index in eq.variables.Keys)
            eq.variables[index] = eq.variables[index] * value;
        return eq;
    }

    public static LinearEquation operator /(LinearEquation eq, double value)
    {
        foreach (var index in eq.variables.Keys)
            eq.variables[index] = eq.variables[index] / value;
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
        var eq = new LinearEquation(variables.Count);
        foreach (var (index, coef) in variables)
            eq.Sub(new(index, coef));
        return eq;
    }

    public override string ToString()
    {
        StringBuilder builder = new();

        bool first = true;
        foreach (var (index, coef) in variables)
            LinearProblem.RenderCoef(builder, coef, new Variable(index).ToString(), ref first);

        return builder.ToString();
    }

    public IEnumerator<Variable> GetEnumerator()
    {
        return variables.Select(entry => new Variable(entry.Key, entry.Value)).GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    public int CompareTo(LinearEquation other)
    {
        return EnumerableExtensions.SequenceCompareTo(this, other);
    }
}
