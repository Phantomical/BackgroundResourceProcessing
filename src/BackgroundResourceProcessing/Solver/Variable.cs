using System;

#pragma warning disable CS0660 // Type defines operator == or operator != but does not override Object.Equals(object o)
#pragma warning disable CS0661 // Type defines operator == or operator != but does not override Object.GetHashCode()

namespace BackgroundResourceProcessing.Solver
{
    /// <summary>
    /// A single variable
    /// </summary>
    /// <param name="coef"></param>
    /// <param name="index"></param>
    internal readonly struct Variable(uint index, double coef) : IComparable<Variable>
    {
        public readonly double Coef = coef;
        public readonly uint Index = index;

        public Variable(uint index)
            : this(index, 1.0) { }

        public static Variable operator *(double coef, Variable var)
        {
            return new(var.Index, coef * var.Coef);
        }

        public static Variable operator *(Variable var, double coef)
        {
            return new(var.Index, var.Coef * coef);
        }

        public static Variable operator /(Variable var, double coef)
        {
            return new(var.Index, var.Coef / coef);
        }

        public static Variable operator -(Variable var)
        {
            return new(var.Index, -var.Coef);
        }

        public static LinearEquation operator +(Variable a, Variable b)
        {
            LinearEquation eq = new();
            eq.Add(a);
            eq.Add(b);
            return eq;
        }

        public static LinearEquation operator -(Variable a, Variable b)
        {
            LinearEquation eq = new();
            eq.Add(a);
            eq.Sub(b);
            return eq;
        }

        public static LinearConstraint operator <=(Variable v, double c)
        {
            return new LinearEquation(v) <= c;
        }

        public static LinearConstraint operator >=(Variable v, double c)
        {
            return new LinearEquation(v) >= c;
        }

        public static LinearConstraint operator ==(Variable v, double c)
        {
            return new LinearEquation(v) == c;
        }

        public static LinearConstraint operator !=(Variable v, double c)
        {
            throw new NotImplementedException();
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
}
