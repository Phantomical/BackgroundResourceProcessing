using System.Collections.Generic;
using System.Linq;
using BackgroundResourceProcessing.Behaviour;
using Kopernicus.Components;

namespace BackgroundResourceProcessing.Integration.Kopernicus;

public class KopernicusStarProvider : ShadowState.IStarProvider
{
    /// <summary>
    /// A cutoff below which we don't bother trying to generate power for a
    /// star.
    /// </summary>
    [KSPField]
    public double FluxCutoff = 1e-6;

    public override List<CelestialBody> GetRelevantStars(Vessel vessel)
    {
        List<KopernicusStar> stars = [.. KopernicusStar.Stars];

        // Remove all stars that are too weak for us to bother with.
        stars.RemoveAll(star => ComputeStellarFluxFactor(star, vessel) < FluxCutoff);

        // Sort the stars by descending order of solar flux.
        stars.Sort(new StarComparer(vessel));

        return [.. stars.Select(star => star.sun)];
    }

    public override double GetSolarFluxFactor(CelestialBody body, Vessel vessel)
    {
        if (!KopernicusStar.CelestialBodies.TryGetValue(body, out var star))
            return 0.0;

        return ComputeStellarFluxFactor(star, vessel);
    }

    public static double ComputeStellarFluxFactor(KopernicusStar star, Vessel vessel)
    {
        var body = star.sun;
        var spos = body.position;
        var vpos = vessel.vesselTransform.position;

        // Kopernicus' solarLuminosity is actually the equivalent for KSP's
        // SolarLuminosityAtHome. We just need to compute the luminosity ratios
        // and distance ratios to get the solar flux multiplication factor.
        var distance = (spos - vpos).magnitude - body.Radius;
        var homeBodySMA = KopernicusStar.HomeBodySMA;
        var distanceRatio = homeBodySMA / distance;
        var luminosityRatio = star.shifter.solarLuminosity / PhysicsGlobals.SolarLuminosityAtHome;

        return luminosityRatio * distanceRatio * distanceRatio;
    }

    private struct StarComparer(Vessel vessel) : IComparer<KopernicusStar>
    {
        public readonly int Compare(KopernicusStar x, KopernicusStar y)
        {
            double xFlux = ComputeStellarFluxFactor(x, vessel);
            double yFlux = ComputeStellarFluxFactor(y, vessel);

            return -xFlux.CompareTo(yFlux);
        }
    }
}
