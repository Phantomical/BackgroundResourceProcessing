using System.Collections.Generic;
using BackgroundResourceProcessing;
using BackgroundResourceProcessing.Behaviour;
using BackgroundResourceProcessing.Utils;

/// <summary>
/// A consumer that consumes resources at a fixed rate.
/// </summary>
public class ConstantConsumer(List<ResourceRatio> inputs) : ConverterBehaviour()
{
    public List<ResourceRatio> Inputs = inputs;

    public ConstantConsumer()
        : this([]) { }

    public override ConverterResources GetResources(VesselState state)
    {
        return new() { Inputs = Inputs };
    }

    protected override void OnLoad(ConfigNode node)
    {
        base.OnLoad(node);
        Inputs = [.. ConfigUtil.LoadInputResources(node)];
    }

    protected override void OnSave(ConfigNode node)
    {
        base.OnSave(node);
        ConfigUtil.SaveInputResources(node, Inputs);
    }
}
