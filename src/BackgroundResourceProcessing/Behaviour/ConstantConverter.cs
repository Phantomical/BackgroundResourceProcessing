using System.Collections.Generic;

namespace BackgroundResourceProcessing.Behaviour;

/// <summary>
/// A converter that converts a set of resources into another set of
/// resources at a constant rate.
/// </summary>
public class ConstantConverter : ConverterBehaviour
{
    public List<ResourceRatio> inputs = [];
    public List<ResourceRatio> outputs = [];
    public List<ResourceConstraint> required = [];

    public ConstantConverter() { }

    public ConstantConverter(List<ResourceRatio> inputs, List<ResourceRatio> outputs)
    {
        this.inputs = inputs;
        this.outputs = outputs;
    }

    public ConstantConverter(
        List<ResourceRatio> inputs,
        List<ResourceRatio> outputs,
        List<ResourceConstraint> required
    )
    {
        this.inputs = inputs;
        this.outputs = outputs;
        this.required = required;
    }

    public override ConverterResources GetResources(VesselState state)
    {
        return new()
        {
            Inputs = inputs,
            Outputs = outputs,
            Requirements = required,
        };
    }

    protected override void OnLoad(ConfigNode node)
    {
        base.OnLoad(node);
        inputs = [.. ConfigUtil.LoadInputResources(node)];
        outputs = [.. ConfigUtil.LoadOutputResources(node)];
        required = [.. ConfigUtil.LoadRequiredResources(node)];
    }

    protected override void OnSave(ConfigNode node)
    {
        base.OnSave(node);
        ConfigUtil.SaveInputResources(node, inputs);
        ConfigUtil.SaveOutputResources(node, outputs);
        ConfigUtil.SaveRequiredResources(node, required);
    }
}
