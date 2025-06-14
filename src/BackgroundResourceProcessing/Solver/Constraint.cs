using System;
using System.Collections.Generic;
using System.Text;

#pragma warning disable CS0660 // Type defines operator == or operator != but does not override Object.Equals(object o)
#pragma warning disable CS0661 // Type defines operator == or operator != but does not override Object.GetHashCode()

namespace BackgroundResourceProcessing.Solver
{
    /// <summary>
    /// A single variable
    /// </summary>
    /// <param name="coef"></param>
    /// <param name="index"></param>
    internal readonly struct Variable(double coef, uint index)
    {
        public readonly double Coef = coef;
        public readonly uint Index = index;

        public Variable(uint index)
            : this(1.0, index) { }

        public static Variable operator *(double coef, Variable var)
        {
            return new(coef * var.Coef, var.Index);
        }

        public static Variable operator *(Variable var, double coef)
        {
            return new(var.Coef * coef, var.Index);
        }

        public static Variable operator /(Variable var, double coef)
        {
            return new(var.Coef / coef, var.Index);
        }

        public static Variable operator -(Variable var)
        {
            return new(-var.Coef, var.Index);
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
            var name = "x";
            var index = Index;
            if (index >= LinearProblem.BinaryStart)
            {
                index -= LinearProblem.BinaryStart;
                name = "z";
            }

            if (Coef == 1.0)
                return $"{name}{index}";
            return $"{Coef.ToString(fmt)}*{name}{index}";
        }
    }

    internal readonly ref struct LinearEquation(SortedList<uint, Variable> variables)
    {
        public readonly SortedList<uint, Variable> variables = variables;

        public LinearEquation()
            : this([]) { }

        public LinearEquation(Variable var)
            : this(new SortedList<uint, Variable>(1))
        {
            Add(var);
        }

        public void Add(Variable var)
        {
            if (variables.TryGetValue(var.Index, out var existing))
                var = new(var.Coef + existing.Coef, var.Index);
            variables[var.Index] = var;
        }

        public void Sub(Variable var)
        {
            Add(-var);
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

        public static LinearConstraint operator ==(LinearEquation eq, double value)
        {
            return new LinearConstraint(eq.variables, Relation.Equal, value);
        }

        public static LinearConstraint operator !=(LinearEquation eq, double value)
        {
            throw new NotImplementedException("Cannot optimize != constraints");
        }

        public static LinearConstraint operator <=(LinearEquation eq, double value)
        {
            return new LinearConstraint(eq.variables, Relation.LEqual, value);
        }

        public static LinearConstraint operator >=(LinearEquation eq, double value)
        {
            return new LinearConstraint(eq.variables, Relation.GEqual, value);
        }

        public override string ToString()
        {
            StringBuilder builder = new();

            bool first = true;
            foreach (var var in variables.Values)
            {
                LinearProblem.RenderCoef(
                    builder,
                    var.Coef,
                    new Variable(var.Index).ToString(),
                    ref first
                );
            }

            return builder.ToString();
        }
    }

    internal enum Relation
    {
        LEqual,
        GEqual,
        Equal,
    }

    internal class LinearConstraint
    {
        public SortedList<uint, Variable> variables;
        public Relation relation;
        public double constant;

        public Variable? slack = null;

        public LinearConstraint(
            SortedList<uint, Variable> variables,
            Relation relation,
            double constant
        )
        {
            SortedList<uint, Variable> copy = new(variables.Count);
            foreach (var var in variables.Values)
            {
                if (var.Coef == 0.0)
                    continue;
                if (double.IsNaN(var.Coef))
                    throw new InvalidCoefficientException(
                        $"Coefficient for variable x{var.Index} was NaN"
                    );
                if (double.IsPositiveInfinity(var.Coef) || double.IsNegativeInfinity(var.Coef))
                    throw new InvalidCoefficientException(
                        $"Coefficient for variable x{var.Index} was infinite"
                    );

                copy.Add(var.Index, var);
            }

            this.variables = copy;
            this.relation = relation;
            this.constant = constant;
        }

        public bool KnownInconsistent
        {
            get
            {
                if (variables.Count == 0)
                {
                    switch (relation)
                    {
                        case Relation.Equal:
                            return !(0 == constant);
                        case Relation.LEqual:
                            return !(0 <= constant);
                        case Relation.GEqual:
                            return !(0 >= constant);
                    }
                }

                return false;
            }
        }

        public void Substitute(uint index, double value)
        {
            if (!variables.TryGetValue(index, out var variable))
                return;

            variables.Remove(index);
            constant -= variable.Coef * value;
        }

        public LinearConstraint Clone()
        {
            return new(new(variables), relation, constant);
        }

        public override string ToString()
        {
            StringBuilder builder = new();

            if (variables.Count == 0.0)
                builder.Append(0.0);
            else
                builder.Append(new LinearEquation(variables).ToString());

            if (slack == null)
            {
                switch (relation)
                {
                    case Relation.LEqual:
                        builder.Append(" <= ");
                        break;
                    case Relation.GEqual:
                        builder.Append(" >= ");
                        break;
                    case Relation.Equal:
                        builder.Append(" == ");
                        break;
                }
            }
            else
            {
                var first = false;
                var s = (Variable)slack;
                LinearProblem.RenderCoef(builder, s.Coef, $"S{s.Index}", ref first);

                builder.Append(" == ");
            }

            builder.Append(constant);

            return builder.ToString();
        }
    }

    internal class InvalidCoefficientException(string message) : Exception(message) { }
}
