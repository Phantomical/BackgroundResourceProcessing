using System;
using System.Collections.Generic;
using System.Linq;
using BackgroundResourceProcessing.Maths;

namespace BackgroundResourceProcessing;

public struct ShadowState(double estimate, bool inShadow, CelestialBody star = null)
{
    public double NextTerminatorEstimate = estimate;
    public bool InShadow = inShadow;
    public CelestialBody Star = star;

    public static ShadowState AlwaysInSun(CelestialBody star) =>
        new(double.PositiveInfinity, false, star);

    public static ShadowState AlwaysInShadow() => new(double.PositiveInfinity, true);

    #region StarProvider
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

    private class KSPStarProvider : IStarProvider
    {
        public List<CelestialBody> GetRelevantStars(Vessel vessel)
        {
            return [Planetarium.fetch.Sun];
        }
    }

    public static IStarProvider StarProvider = new KSPStarProvider();
    #endregion

    #region Shadow State Computations
    static ShadowState DefaultForStar(CelestialBody star)
    {
        if (star == null)
            return AlwaysInShadow();
        return AlwaysInSun(star);
    }

    public static ShadowState GetShadowState(Vessel vessel)
    {
        // try
        // {
        var state = GetShadowStateImpl(vessel);
        if (double.IsNaN(state.NextTerminatorEstimate))
        {
            LogUtil.Error("Shadow state calculations returned NaN terminator estimate");
            return DefaultForStar(Planetarium.fetch?.Sun);
        }
        if (state.NextTerminatorEstimate < Planetarium.GetUniversalTime())
        {
            LogUtil.Error("Shadow state calculations returned a terminator in the past");
            return DefaultForStar(Planetarium.fetch?.Sun);
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
        return vessel.situation switch
        {
            Vessel.Situations.ORBITING
            or Vessel.Situations.SUB_ORBITAL
            or Vessel.Situations.ESCAPING
            or Vessel.Situations.DOCKED => GetOrbitShadowState(vessel),
            Vessel.Situations.LANDED
            or Vessel.Situations.SPLASHED
            or Vessel.Situations.PRELAUNCH
            or Vessel.Situations.FLYING => GetLandedShadowState(vessel),
            _ => AlwaysInSun(Planetarium.fetch?.Sun),
        };
    }

    private static ShadowState GetOrbitShadowState(Vessel vessel)
    {
        var stars = StarProvider.GetRelevantStars(vessel) ?? [];
        var settings = HighLogic.CurrentGame?.Parameters.CustomParams<Settings>();
        if (!(settings?.EnableOrbitShadows ?? false))
            return DefaultForStar(stars.FirstOrDefault());

        var state = AlwaysInShadow();
        foreach (var star in stars)
        {
            var bodies = GetReferenceBodies(vessel, star);
            if (ReferenceEquals(bodies.parent, star))
                return AlwaysInSun(star);

            OrbitShadow shadow = new();
            shadow.SetReferenceBodies(vessel, star, bodies.planet);
            var terminator = shadow.ComputeTerminatorUT(out var inshadow);

            if (!inshadow)
                return new(Math.Min(terminator, state.NextTerminatorEstimate), inshadow, star);

            if (terminator < state.NextTerminatorEstimate)
                state = new(terminator, inshadow);
        }

        return state;
    }

    private static ShadowState GetLandedShadowState(Vessel vessel)
    {
        var stars = StarProvider.GetRelevantStars(vessel) ?? [];
        var settings = HighLogic.CurrentGame?.Parameters.CustomParams<Settings>();
        if (!(settings?.EnableLandedShadows ?? false))
            return DefaultForStar(stars.FirstOrDefault());

        var state = AlwaysInShadow();
        foreach (var star in stars)
        {
            LandedShadow shadow = new();
            shadow.SetReferenceBodies(vessel, star);
            var terminator = shadow.ComputeTerminatorUT(out var inshadow);

            if (!inshadow)
                return new(Math.Min(terminator, state.NextTerminatorEstimate), inshadow, star);

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
    #endregion

    public static ShadowState? Load(ConfigNode node)
    {
        ShadowState state = default;
        if (!node.TryGetValue("NextTerminatorEstimate", ref state.NextTerminatorEstimate))
            return null;
        if (!node.TryGetValue("InShadow", ref state.InShadow))
            return null;

        if (!state.InShadow)
        {
            string star = null;
            if (!node.TryGetValue("Star", ref star))
                return null;

            var body = PSystemManager.Instance?.localBodies?.Find(body =>
                body.isStar && body.bodyName == star
            );
            if (body == null)
                return null;
            state.Star = body;
        }

        return state;
    }

    public readonly void Save(ConfigNode node)
    {
        node.AddValue("NextTerminatorEstimate", NextTerminatorEstimate);
        node.AddValue("InShadow", InShadow);
        if (Star != null)
            node.AddValue("Star", Star.bodyName);
    }

    private struct ReferenceBodies
    {
        public CelestialBody planet;
        public CelestialBody parent;
    }
}

internal ref struct OrbitalShadowCalcs { }
