using System;

namespace BackgroundResourceProcessing.Solver;

internal struct SimpleConstraint(Variable variable, Relation relation, double constant)
{
    public Variable variable = variable;
    public Relation relation = relation;
    public double constant = constant;

    public override readonly string ToString()
    {
        return relation switch
        {
            Relation.Equal => $"{variable} == {constant}",
            Relation.LEqual => $"{variable} <= {constant}",
            Relation.GEqual => $"{variable} >= {constant}",
            _ => throw new NotImplementedException($"unknown relation value {relation}"),
        };
    }
}
