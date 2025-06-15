using System;
using System.Collections.Generic;
using System.Text;

namespace BackgroundResourceProcessing.Solver
{
    internal class LinearConstraint : IComparable<LinearConstraint>
    {
        public LinearEquation variables;
        public Relation relation;
        public double constant;

        public Variable? slack = null;

        public LinearConstraint(LinearEquation variables, Relation relation, double constant)
        {
            SortedList<uint, double> copy = new(variables.Count);
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

                copy.Add(var.Index, var.Coef);
            }

            this.variables = new(copy);
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

        public void Substitute(uint index, LinearEquation eq, double value)
        {
            if (!variables.TryGetValue(index, out var variable))
                return;
            if (eq.Contains(variable))
                throw new ArgumentException(
                    $"Attempted to substitue variable {variable} with equation {eq} containing {variable}"
                );

            variables.Remove(index);
            var coef = variable.Coef;
            foreach (var var in eq)
                variables.Add(var * coef);
            constant += coef * value;
        }

        public LinearConstraint Clone()
        {
            return new(variables.Clone(), relation, constant);
        }

        public override string ToString()
        {
            return ToString("");
        }

        public string ToString(string fmt)
        {
            StringBuilder builder = new();

            if (variables.Count == 0.0)
                builder.Append(0.0);
            else
                builder.Append(variables);

            if (slack == null || fmt == "R")
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

        public int CompareTo(LinearConstraint other)
        {
            int cmp = variables.CompareTo(other.variables);
            if (cmp != 0)
                return cmp;
            cmp = relation.CompareTo(other.relation);
            if (cmp != 0)
                return cmp;
            return constant.CompareTo(other.constant);
        }
    }

    internal class InvalidCoefficientException(string message) : Exception(message) { }
}
