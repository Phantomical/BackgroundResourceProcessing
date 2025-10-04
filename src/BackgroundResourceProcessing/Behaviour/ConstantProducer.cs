using System.Collections.Generic;
using BackgroundResourceProcessing.Utils;

namespace BackgroundResourceProcessing.Behaviour;

/// <summary>
/// A producer that produces resources at a fixed rate.
/// </summary>
public class ConstantProducer(List<ResourceRatio> outputs) : ConverterBehaviour
{
    public List<ResourceRatio> Outputs = outputs;

    public ConstantProducer()
        : this([]) { }

    public override ConverterResources GetResources(VesselState state)
    {
        return new() { Outputs = Outputs };
    }

    protected override void OnLoad(ConfigNode node)
    {
        base.OnLoad(node);
        Outputs = [.. ConfigUtil.LoadOutputResources(node)];
    }

    protected override void OnSave(ConfigNode node)
    {
        base.OnSave(node);
        ConfigUtil.SaveOutputResources(node, Outputs);
    }
}
