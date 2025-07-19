using BackgroundResourceProcessing.Behaviour;
using BackgroundResourceProcessing.Converter;
using Kopernicus.Components;

namespace BackgroundResourceProcessing.Integration.Kopernicus;

public class KopernicusSolarPanelBehaviour : SolarPanelBehaviour
{
    protected override double GetSolarFluxFactor(VesselState state)
    {
        if (state.ShadowState.Star == null)
            return 0;

        var kstar = KopernicusStar.CelestialBodies[state.ShadowState.Star];
        return KopernicusStarProvider.ComputeStellarFluxFactor(kstar, Vessel);
    }
}

public class BackgroundKopernicusSolarPanel : BackgroundSolarPanel
{
    protected override SolarPanelBehaviour ConstructBehaviour(ModuleDeployableSolarPanel panel)
    {
        return new KopernicusSolarPanelBehaviour();
    }
}
