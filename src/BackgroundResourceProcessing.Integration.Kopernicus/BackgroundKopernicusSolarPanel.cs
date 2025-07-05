using BackgroundResourceProcessing.Behaviour;
using BackgroundResourceProcessing.Converter;
using Kopernicus.Components;

namespace BackgroundResourceProcessing.Integration.Kopernicus;

public class BackgroundKopernicusSolarPanel : BackgroundConverter<KopernicusSolarPanel>
{
    public override ModuleBehaviour GetBehaviour(KopernicusSolarPanel panel)
    {
        // TODO:
        //  - Support alternating between 0 and flowRate when going into and
        //    out of planet shadows.
        //  - Compute a non-zero background rate when in shadow.
        //  - Be smarter when landed on a planet.
        if (panel.currentOutput == 0)
            return null;

        var ratio = new ResourceRatio()
        {
            Ratio = panel.currentOutput,
            ResourceName = "ElectricCharge",
            FlowMode = ResourceFlowMode.ALL_VESSEL_BALANCE,
        };

        return new(new ConstantProducer([ratio]));
    }
}
