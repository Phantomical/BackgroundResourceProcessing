using System;
using System.Collections.Generic;
using System.Linq;
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

    public List<CelestialBody> GetRelevantStars(Vessel vessel)
    {
        List<KopernicusStar> stars = [.. KopernicusStar.Stars];

        // Remove all stars that are too weak for us to bother with.
        stars.RemoveAll(star => ComputeStellarFlux(star, vessel) < FluxCutoff);

        // Sort the stars by descending order of solar flux.
        stars.Sort(new StarComparer(vessel));

        return [.. stars.Select(star => star.sun)];
    }

    public static double ComputeStellarFlux(KopernicusStar star, Vessel vessel)
    {
        var body = star.sun;
        var spos = body.position;
        var vpos = vessel.vesselTransform.position;

        var distance = (spos - vpos).magnitude - body.Radius;

        return star.shifter.solarLuminosity / (4 * Math.PI * distance * distance);
    }

    private struct StarComparer(Vessel vessel) : IComparer<KopernicusStar>
    {
        public readonly int Compare(KopernicusStar x, KopernicusStar y)
        {
            double xFlux = ComputeStellarFlux(x, vessel);
            double yFlux = ComputeStellarFlux(y, vessel);

            return -xFlux.CompareTo(yFlux);
        }
    }
}
