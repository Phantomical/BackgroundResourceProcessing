using System.Collections.Generic;
using System.Linq;
using BackgroundResourceProcessing.Behaviour;
using Kopernicus.Components;

namespace BackgroundResourceProcessing.Integration.Kopernicus;

public class KopernicusStarProvider : ShadowState.IStarProvider
{
    /// <summary>
    /// The solar flux, in W/m^2, at which Kopernicus' KopernicusSolarPanel
    /// fixer produces exactly a panel's rated output. The fixer computes
    /// <c>nominalRate / 24.4 / 56.37091313591871</c> EC per W/m^2 of per-star
    /// flux, so this is the flux basis its output is normalized against.
    /// </summary>
    private const double PanelRatedFlux = 24.4 * 56.37091313591871;

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

        // Kopernicus' solarLuminosity is the star's flux in W/m^2 at the home
        // body's SMA, measured from the star's surface. Note that we must NOT
        // normalize against PhysicsGlobals.SolarLuminosityAtHome here:
        // LightShifter.ApplyPhysics overwrites that global with whichever
        // star's physics was applied last (usually the tracked star's own
        // luminosity), which would silently cancel the luminosity factor.
        var distance = (spos - vpos).magnitude - body.Radius;
        var homeBodySMA = KopernicusStar.HomeBodySMA;
        var distanceRatio = homeBodySMA / distance;
        var flux = star.shifter.solarLuminosity * distanceRatio * distanceRatio;

        // In multi-star systems panels are driven by the KopernicusSolarPanel
        // fixer, which rates panels against the fixed PanelRatedFlux basis. In
        // single-star systems the stock module runs instead and its output is
        // relative to the star's own luminosity-at-home.
        var basis = KopernicusStar.Stars.Count > 1 ? PanelRatedFlux : star.shifter.solarLuminosity;
        if (basis <= 0.0)
            return 0.0;

        return flux / basis;
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
