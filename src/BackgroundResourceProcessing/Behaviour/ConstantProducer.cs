using System.Collections.Generic;

namespace BackgroundResourceProcessing.Behaviour;

/// <summary>
/// A producer that produces resources at a fixed rate.
/// </summary>
public class ConstantProducer(List<ResourceRatio> outputs) : ConverterBehaviour
{
    private List<ResourceRatio> outputs = outputs;

    public ConstantProducer()
        : this([]) { }

    public override ConverterResources GetResources(VesselState state)
    {
        return new() { Outputs = outputs };
    }

    protected override void OnLoad(ConfigNode node)
    {
        base.OnLoad(node);
        outputs = [.. ConfigUtil.LoadOutputResources(node)];
    }

    protected override void OnSave(ConfigNode node)
    {
        base.OnSave(node);
        ConfigUtil.SaveOutputResources(node, outputs);
    }
}
