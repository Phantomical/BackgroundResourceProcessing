using System.Collections.Generic;
using BackgroundResourceProcessing;
using BackgroundResourceProcessing.Behaviour;

/// <summary>
/// A consumer that consumes resources at a fixed rate.
/// </summary>
public class ConstantConsumer(List<ResourceRatio> inputs) : ConverterBehaviour()
{
    private List<ResourceRatio> inputs = inputs;

    public ConstantConsumer()
        : this([]) { }

    public override ConverterResources GetResources(VesselState state)
    {
        return new() { Inputs = inputs };
    }

    protected override void OnLoad(ConfigNode node)
    {
        base.OnLoad(node);
        inputs = [.. ConfigUtil.LoadInputResources(node)];
    }

    protected override void OnSave(ConfigNode node)
    {
        base.OnSave(node);
        ConfigUtil.SaveInputResources(node, inputs);
    }
}
