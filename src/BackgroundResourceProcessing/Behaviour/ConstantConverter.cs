using System.Collections.Generic;
using BackgroundResourceProcessing.Utils;

namespace BackgroundResourceProcessing.Behaviour;

/// <summary>
/// A converter that converts a set of resources into another set of
/// resources at a constant rate.
/// </summary>
public class ConstantConverter(
    List<ResourceRatio> inputs,
    List<ResourceRatio> outputs,
    List<ResourceConstraint> required
) : ConverterBehaviour
{
    public List<ResourceRatio> Inputs = inputs;
    public List<ResourceRatio> Outputs = outputs;
    public List<ResourceConstraint> Required = required;

    public ConstantConverter()
        : this([], [], []) { }

    public ConstantConverter(List<ResourceRatio> inputs, List<ResourceRatio> outputs)
        : this(inputs, outputs, []) { }

    public override ConverterResources GetResources(VesselState state)
    {
        return new()
        {
            Inputs = Inputs,
            Outputs = Outputs,
            Requirements = Required,
        };
    }

    protected override void OnLoad(ConfigNode node)
    {
        base.OnLoad(node);
        Inputs = [.. ConfigUtil.LoadInputResources(node)];
        Outputs = [.. ConfigUtil.LoadOutputResources(node)];
        Required = [.. ConfigUtil.LoadRequiredResources(node)];
    }

    protected override void OnSave(ConfigNode node)
    {
        base.OnSave(node);
        ConfigUtil.SaveInputResources(node, Inputs);
        ConfigUtil.SaveOutputResources(node, Outputs);
        ConfigUtil.SaveRequiredResources(node, Required);
    }
}
