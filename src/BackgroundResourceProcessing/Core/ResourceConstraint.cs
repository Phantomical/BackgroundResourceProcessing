using System;

namespace BackgroundResourceProcessing.Core;

/// <summary>
/// The current state of this constraint.
/// </summary>
public enum ConstraintState : byte
{
    /// <summary>
    /// The constraint state has not been computed yet.
    /// </summary>
    UNSET = 0,

    /// <summary>
    /// The constraint is not satisified and this converter cannot run.
    /// </summary>
    DISABLED = 1,

    /// <summary>
    /// The current resources are at the constraint boundary and the converter
    /// may be able to run if the net resource rate meets the constraint
    /// requirements.
    /// </summary>
    BOUNDARY = 2,

    /// <summary>
    /// The constraint is satisified and the converter can operate freely.
    /// </summary>
    ENABLED = 3,
}

public static class ConstraintStateExtensions
{
    /// <summary>
    /// Merge together two <c><see cref="ConstraintState"/></c>s to get a single
    /// constraint state that represents the overall state.
    /// </summary>
    /// <param name="a"></param>
    /// <param name="b"></param>
    /// <returns></returns>
    public static ConstraintState Merge(this ConstraintState a, ConstraintState b)
    {
        // The numeric enum values are set so that merging the two states just
        // becomes a matter of taking the minimum of their values.
        return (ConstraintState)Math.Min((byte)a, (byte)b);
    }
}

public struct ResourceConverterConstraint
{
    public ResourceConstraint ResourceConstraint;

    /// <summary>
    /// The name of the resource that this constraint applies to.
    /// </summary>
    public readonly string ResourceName => ResourceConstraint.ResourceName;

    /// <summary>
    /// At what resource amount does this constraint apply.
    /// </summary>
    public readonly double Amount => ResourceConstraint.Amount;

    /// <summary>
    /// What type of constraint is being applied here.
    /// </summary>
    public readonly Constraint Constraint => ResourceConstraint.Constraint;

    /// <summary>
    /// The flow mode to use when computing inventory resource constraints.
    /// </summary>
    public readonly ResourceFlowMode FlowMode => ResourceConstraint.FlowMode;

    /// <summary>
    /// The current state of this constraint.
    /// </summary>
    public ConstraintState State { get; internal set; } = ConstraintState.UNSET;

    public ResourceConverterConstraint(ResourceConstraint constraint)
    {
        ResourceConstraint = constraint;

        if (FlowMode == ResourceFlowMode.NULL)
        {
            int resourceId = ResourceName.GetHashCode();
            var definition = PartResourceLibrary.Instance?.GetDefinition(resourceId);
            ResourceConstraint.FlowMode =
                definition?.resourceFlowMode ?? ResourceFlowMode.ALL_VESSEL_BALANCE;
        }
    }

    public void Load(ConfigNode node)
    {
        ResourceConstraint.Load(node);

        ConstraintState state = ConstraintState.UNSET;
        if (node.TryGetEnum("State", ref state, ConstraintState.UNSET))
            State = state;
    }

    public readonly void Save(ConfigNode node)
    {
        ResourceConstraint.Save(node);
        node.AddValue("State", State);
    }
}
