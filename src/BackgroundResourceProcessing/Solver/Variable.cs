using System;
using System.Runtime.CompilerServices;

#pragma warning disable CS0660 // Type defines operator == or operator != but does not override Object.Equals(object o)
#pragma warning disable CS0661 // Type defines operator == or operator != but does not override Object.GetHashCode()

namespace BackgroundResourceProcessing.Solver;

/// <summary>
/// A single variable
/// </summary>
/// <param name="coef"></param>
/// <param name="index"></param>
[method: MethodImpl(MethodImplOptions.AggressiveInlining)]
internal readonly struct Variable(int index, double coef) : IComparable<Variable>
{
    public readonly double Coef = coef;
    public readonly int Index = index;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Variable(int index)
        : this(index, 1.0) { }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Variable operator *(double coef, Variable var)
    {
        return new(var.Index, coef * var.Coef);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Variable operator *(Variable var, double coef)
    {
        return new(var.Index, var.Coef * coef);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Variable operator /(Variable var, double coef)
    {
        return new(var.Index, var.Coef / coef);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Variable operator -(Variable var)
    {
        return new(var.Index, -var.Coef);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static LinearConstraint operator <=(Variable v, double c)
    {
        return new LinearEquation(v.Index + 1) { v } <= c;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static LinearConstraint operator >=(Variable v, double c)
    {
        return new LinearEquation(v.Index + 1) { v } >= c;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static LinearConstraint operator ==(Variable v, double c)
    {
        return new LinearEquation(v.Index + 1) { v } == c;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static LinearConstraint operator !=(Variable v, double c)
    {
        throw new NotImplementedException();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Deconstruct(out int index, out double coef)
    {
        index = Index;
        coef = Coef;
    }

    public override string ToString()
    {
        return ToString("G");
    }

    public string ToString(string fmt)
    {
        if (Coef == 1.0)
            return $"x{Index}";
        return $"{Coef.ToString(fmt)}*x{Index}";
    }

    public int CompareTo(Variable other)
    {
        var cmp = Index.CompareTo(other.Index);
        if (cmp != 0)
            return cmp;
        return Coef.CompareTo(other.Coef);
    }
}
