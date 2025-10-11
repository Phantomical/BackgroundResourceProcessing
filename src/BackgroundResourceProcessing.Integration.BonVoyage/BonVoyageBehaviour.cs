using System.Collections.Generic;
using BackgroundResourceProcessing.Behaviour;

namespace BackgroundResourceProcessing.Integration.BonVoyage;

public class BonVoyageBehaviour(List<ResourceRatio> inputs) : ConstantProducer(inputs)
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
}
