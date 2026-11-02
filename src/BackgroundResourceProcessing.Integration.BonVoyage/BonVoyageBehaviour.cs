using System.Collections.Generic;
using BackgroundResourceProcessing.Behaviour;
using BackgroundResourceProcessing.Utils;

namespace BackgroundResourceProcessing.Integration.BonVoyage;

public class BonVoyageBehaviour(List<ResourceRatio> inputs) : ConstantConsumer(inputs)
{
    [KSPField(isPersistant = true)]
    public bool Enabled = true;

    [KSPField(isPersistant = true)]
    public double AverageSpeed = 0.0;

    public BonVoyageBehaviour()
        : this([]) { }

    public override ConverterResources GetResources(VesselState state)
    {
        if (!Enabled)
            return new();

        return base.GetResources(state);
    }

    protected override void OnLoad(ConfigNode node)
    {
        base.OnLoad(node);

        // Older versions mistakenly saved the rover's power draw as an output.
        // Load it as the input it was meant to be.
        if (Inputs.Count == 0)
            Inputs = [.. ConfigUtil.LoadOutputResources(node)];
    }
}
