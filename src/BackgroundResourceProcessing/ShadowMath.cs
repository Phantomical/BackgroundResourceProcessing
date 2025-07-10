using System.Collections.Generic;
using BackgroundResourceProcessing.Maths;

namespace BackgroundResourceProcessing;

public struct ShadowState(double estimate, bool inShadow)
{
    public double NextTerminatorEstimate = estimate;
    public bool InShadow = inShadow;

    public static ShadowState AlwaysInSun => new(double.PositiveInfinity, false);
    public static ShadowState AlwaysInShadow => new(double.PositiveInfinity, true);

    public interface IStarProvider
    {
        /// <summary>
        /// Get a list of relevant stars, ordered by the amount of energy they
        /// would give to the current vessel.
        /// </summary>
        ///
        /// <remarks>
        /// Additional stars are expensive. The number should be kept to a
        /// minimum. If one star is 1000x less powerful than others on the list
        /// then it should not be returned.
        /// </remarks>
        List<CelestialBody> GetRelevantStars(Vessel vessel);
    }

    public static IStarProvider StarProvider = new KSPStarProvider();

    public static ShadowState GetShadowState(Vessel vessel)
    {
        // try
        // {
        var state = GetShadowStateImpl(vessel);
        if (double.IsNaN(state.NextTerminatorEstimate))
        {
            LogUtil.Error("Shadow state calculations returned NaN terminator estimate");
            return new(double.PositiveInfinity, false);
        }

        return state;
        // }
        // catch (Exception e)
        // {
        //     LogUtil.Error($"GetShadowState threw an exception: {e}");
        //     return new(double.PositiveInfinity, false);
        // }
    }

    private static ShadowState GetShadowStateImpl(Vessel vessel)
    {
        switch (vessel.situation)
        {
            case Vessel.Situations.ORBITING:
            case Vessel.Situations.SUB_ORBITAL:
            case Vessel.Situations.ESCAPING:
            case Vessel.Situations.DOCKED:
                return GetOrbitShadowState(vessel);

            default:
                return AlwaysInSun;
        }
    }

    private static ShadowState GetOrbitShadowState(Vessel vessel)
    {
        var settings = HighLogic.CurrentGame?.Parameters.CustomParams<Settings>();
        if (settings?.EnableOrbitShadows ?? false)
            return AlwaysInSun;

        var stars = StarProvider.GetRelevantStars(vessel);
        if (stars == null || stars.Count == 0)
            return AlwaysInShadow;

        var state = AlwaysInSun;
        foreach (var star in stars)
        {
            var bodies = GetReferenceBodies(vessel, star);
            if (ReferenceEquals(bodies.parent, star))
                return AlwaysInSun;

            OrbitShadow shadow = new();
            shadow.SetReferenceBodies(vessel, star, bodies.planet);
            var terminator = shadow.ComputeTerminatorUT(out var inshadow);

            if (!inshadow)
                return new(terminator, inshadow);

            if (terminator < state.NextTerminatorEstimate)
                state = new(terminator, inshadow);
        }

        return state;
    }

    private static ReferenceBodies GetReferenceBodies(Vessel vessel, CelestialBody star)
    {
        CelestialBody parent = vessel.orbit.referenceBody;
        CelestialBody planet = null;

        HashSet<CelestialBody> ancestors = [];

        CelestialBody cstar = star;
        do
        {
            if (!ancestors.Add(cstar))
                break;
            cstar = cstar.referenceBody;
        } while (cstar != null);

        CelestialBody cplanet = parent;
        while (!ancestors.Contains(cplanet))
        {
            planet = cplanet;
            cplanet = cplanet.referenceBody;
        }

        return new() { parent = parent, planet = planet };
    }

    private class KSPStarProvider : IStarProvider
    {
        public List<CelestialBody> GetRelevantStars(Vessel vessel)
        {
            return [Planetarium.fetch.Sun];
        }
    }

    private struct ReferenceBodies
    {
        public CelestialBody planet;
        public CelestialBody parent;
    }
}

internal ref struct OrbitalShadowCalcs { }
