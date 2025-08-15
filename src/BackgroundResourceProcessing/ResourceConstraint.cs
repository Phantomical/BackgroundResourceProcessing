using System.Diagnostics;

namespace BackgroundResourceProcessing;

/// <summary>
/// The type of constraint applied on this converter.
/// </summary>
public enum Constraint
{
    /// <summary>
    /// The converter must have at least <c>Amount</c> resources to activate.
    /// </summary>
    AT_LEAST,

    /// <summary>
    /// The converter must have at most <c>Amount</c> resources to activate.
    /// </summary>
    AT_MOST,
}

/// <summary>
/// A constraint applied to a resource.
/// </summary>
[DebuggerDisplay("{Constraint} {Amount} {ResourceName}")]
public struct ResourceConstraint()
{
    /// <summary>
    /// The name of the resource that this constraint applies to.
    /// </summary>
    public string ResourceName;

    /// <summary>
    /// At what resource amount does this constraint apply.
    /// </summary>
    public double Amount = 0.0;

    /// <summary>
    /// What type of constraint is being applied here.
    /// </summary>
    public Constraint Constraint = Constraint.AT_LEAST;

    /// <summary>
    /// The flow mode to use when computing inventory resource constraints.
    /// </summary>
    public ResourceFlowMode FlowMode = ResourceFlowMode.ALL_VESSEL;

    public ResourceConstraint(ResourceRatio ratio)
        : this()
    {
        ResourceName = ratio.ResourceName;
        Amount = ratio.Ratio;
        FlowMode = ratio.FlowMode;
    }

    public void Load(ConfigNode node)
    {
        node.TryGetValue("ResourceName", ref ResourceName);
        node.TryGetEnum("Constraint", ref Constraint, Constraint.AT_LEAST);
        node.TryGetEnum("FlowMode", ref FlowMode, ResourceFlowMode.ALL_VESSEL);

        if (!node.TryGetValue("Amount", ref Amount))
        {
            // This is for backwards compatibility with existing KSP
            // REQUIRED_RESOURCE blocks.
            //
            // We'll use the Amount key if present but this will make MM
            // node copies just work as expected.
            node.TryGetValue("Ratio", ref Amount);
        }
    }

    public readonly void Save(ConfigNode node)
    {
        node.AddValue("ResourceName", ResourceName);
        node.AddValue("Amount", Amount);
        node.AddValue("Constraint", Constraint);
        node.AddValue("FlowMode", FlowMode);
    }

    internal ResourceConstraint WithDefaultedFlowMode()
    {
        if (FlowMode != ResourceFlowMode.NULL)
            return this;

        int resourceId = ResourceName.GetHashCode();
        var definition = PartResourceLibrary.Instance.GetDefinition(resourceId);

        if (definition == null)
        {
            LogUtil.Error(
                $"Resource {ResourceName} had no resource definition in PartResourceLibrary."
            );
            FlowMode = ResourceFlowMode.ALL_VESSEL_BALANCE;
        }
        else
        {
            FlowMode = definition.resourceFlowMode;
        }

        return this;
    }
}
