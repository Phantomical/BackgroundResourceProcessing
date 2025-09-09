using System;
using System.Text;
using Unity.Burst;
using Unity.Burst.CompilerServices;

namespace BackgroundResourceProcessing.BurstSolver;

internal struct LinearConstraint : IComparable<LinearConstraint>
{
    public LinearEquation variables;
    public Relation relation;
    public double constant;

    public LinearConstraint(LinearEquation variables, Relation relation, double constant)
    {
        ValidateVariable(in variables);

        this.variables = variables;
        this.relation = relation;
        this.constant = constant;
    }

    public LinearConstraint(SimpleConstraint constraint)
    {
        var var = constraint.variable;

        ValidateVariable(var);

        constant = constraint.constant;
        relation = constraint.relation;
        variables = new(var.Index + 1) { var };
    }

    [IgnoreWarning(1370)]
    [BurstDiscard]
    private static void ValidateVariable(in LinearEquation variables)
    {
        foreach (var var in variables)
        {
            if (double.IsNaN(var.Coef))
                throw new InvalidCoefficientException(
                    $"Coefficient for variable x{var.Index} was NaN"
                );
            if (double.IsPositiveInfinity(var.Coef) || double.IsNegativeInfinity(var.Coef))
                throw new InvalidCoefficientException(
                    $"Coefficient for variable x{var.Index} was infinite"
                );
        }
    }

    [IgnoreWarning(1370)]
    [BurstDiscard]
    private static void ValidateVariable(Variable var)
    {
        if (double.IsNaN(var.Coef))
            throw new InvalidCoefficientException($"Coefficient for variable x{var.Index} was NaN");
        if (double.IsPositiveInfinity(var.Coef) || double.IsNegativeInfinity(var.Coef))
            throw new InvalidCoefficientException(
                $"Coefficient for variable x{var.Index} was infinite"
            );
    }

    public readonly bool KnownInconsistent
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

    public readonly LinearConstraint Clone()
    {
        return new(variables.Clone(), relation, constant);
    }

    public override readonly string ToString()
    {
        StringBuilder builder = new();

        if (variables.Count == 0)
            builder.Append(0.0);
        else
            builder.Append(variables);

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
