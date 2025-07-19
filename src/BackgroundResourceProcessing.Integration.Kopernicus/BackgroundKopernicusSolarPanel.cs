using System;
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

        // Kopernicus' solarLuminosity is actually the equivalent for KSP's
        // SolarLuminosityAtHome. We just need to compute the luminosity ratios
        // and distance ratios to get the solar flux multiplication factor.
        var distance = GetSolarDistance(state);
        var kstar = KopernicusStar.CelestialBodies[state.ShadowState.Star];
        var homeBodySMA = KopernicusStar.HomeBodySMA;
        var distanceRatio = homeBodySMA / distance;
        var luminosityRatio = kstar.shifter.solarLuminosity / PhysicsGlobals.SolarLuminosityAtHome;

        return luminosityRatio * distanceRatio * distanceRatio;
    }
}

public class BackgroundKopernicusSolarPanel : BackgroundSolarPanel
{
    protected override SolarPanelBehaviour ConstructBehaviour(ModuleDeployableSolarPanel panel)
    {
        return new KopernicusSolarPanelBehaviour();
    }
}
