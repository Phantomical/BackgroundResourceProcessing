using System;
using System.Collections.Generic;
using System.Linq;
using BackgroundResourceProcessing.BurstSolver;
using BackgroundResourceProcessing.Mathematics;
using BackgroundResourceProcessing.Tracing;
using BackgroundResourceProcessing.Utils;
using Unity.Collections;
using Unity.Jobs;

namespace BackgroundResourceProcessing;

public struct ShadowState(double estimate, bool inShadow, CelestialBody star = null)
{
    public double NextTerminatorEstimate = estimate;
    public bool InShadow = inShadow;
    public CelestialBody Star = star;

    public static ShadowState AlwaysInSun(CelestialBody star) =>
        new(double.PositiveInfinity, false, star);

    public static ShadowState AlwaysInShadow() => new(double.PositiveInfinity, true);

    #region Solar Flux
    /// <summary>
    /// Get the relative solar flux available for the specified vessel as
    /// compared to the solar flux at home.
    /// </summary>
    public readonly double GetSolarFluxFactor(Vessel vessel)
    {
        return StarProvider.GetSolarFluxFactor(Star, vessel);
    }
    #endregion

    #region StarProvider
    public abstract class IStarProvider
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
        public virtual List<CelestialBody> GetRelevantStars(Vessel vessel)
        {
            return [Planetarium.fetch.Sun];
        }

        /// <summary>
        /// Get a power multiplier for the solar flux due to <paramref name="star"/>
        /// on the provided vessel. A multiplier of 1.0 should be equivalent to the
        /// solar flux at kerbin in a stock game.
        /// </summary>
        public virtual double GetSolarFluxFactor(CelestialBody star, Vessel vessel)
        {
            var distance =
                (star.position - vessel.vesselTransform.position).magnitude - star.Radius;
            var homeDistance = FlightGlobals.GetHomeBody()?.orbit?.semiMajorAxis ?? distance;
            var ratio = homeDistance / distance;
            return ratio * ratio;
        }
    }

    private class KSPStarProvider : IStarProvider { }

    public static IStarProvider StarProvider = new KSPStarProvider();
    #endregion

    #region Shadow State Computations
    internal static ShadowState DefaultForStar(CelestialBody star)
    {
        if (star == null)
            return AlwaysInShadow();
        return AlwaysInSun(star);
    }

    /// <summary>
    /// Combine the per-star shadow terminators for a vessel into a single
    /// <see cref="ShadowState"/>.
    /// </summary>
    ///
    /// <remarks>
    /// <para>
    /// Stars are supplied in descending flux order, so the first star that lights
    /// the vessel is the strongest one currently lighting it; that star wins.
    /// </para>
    ///
    /// <para>
    /// The reported terminator is the <em>earlier</em> of (a) when the lighting
    /// star sets and (b) the soonest sunrise of an even-stronger star that is
    /// currently shadowed (one listed before it). Emerging from a stronger star's
    /// umbra increases the available solar flux, so it is a genuine changepoint
    /// even though <see cref="InShadow"/> stays <c>false</c> across it — hence the
    /// <c>Math.Min</c> against the running shadow minimum. If every star is
    /// shadowed, the nearest shadow-exit (sunrise) time is reported instead.
    /// </para>
    ///
    /// <para>
    /// This is the single source of truth shared by the asynchronous
    /// (<see cref="BurstSolver.ShadowHandle.Complete"/>) path and mirrors the
    /// synchronous <see cref="GetOrbitShadowState"/> / <see cref="GetLandedShadowState"/>
    /// loops so the two cannot diverge.
    /// </para>
    /// </remarks>
    internal static ShadowState AggregateShadowState(
        ReadOnlySpan<OrbitShadow.Terminator> terminators,
        IReadOnlyList<CelestialBody> stars
    )
    {
        var state = AlwaysInShadow();
        for (int i = 0; i < terminators.Length; i++)
        {
            var term = terminators[i];
            var ut = term.UT == double.MaxValue ? double.PositiveInfinity : term.UT;

            // The vessel is lit by this (strongest currently-lit) star. The next
            // changepoint is either when this star sets (ut) or when an even
            // stronger, currently-shadowed star rises (state.NextTerminatorEstimate).
            if (!term.InShadow)
                return new ShadowState(Math.Min(ut, state.NextTerminatorEstimate), false, stars[i]);

            if (ut < state.NextTerminatorEstimate)
                state = new ShadowState(ut, true);
        }

        return state;
    }

    /// <summary>
    /// Reject a computed shadow state whose terminator estimate is NaN or already
    /// in the past, falling back to a safe default for the given star.
    /// </summary>
    ///
    /// <remarks>
    /// Shared by the synchronous (<see cref="GetShadowState"/>) and asynchronous
    /// (<see cref="BurstSolver.ShadowHandle.Complete"/>) paths so that every result
    /// — lit or shadowed — is validated identically.
    /// </remarks>
    internal static ShadowState ValidateShadowState(ShadowState state, double currentUT)
    {
        if (double.IsNaN(state.NextTerminatorEstimate))
        {
            LogUtil.Error("Shadow state calculations returned NaN terminator estimate");
            return DefaultForStar(Planetarium.fetch?.Sun);
        }
        if (state.NextTerminatorEstimate < currentUT)
        {
            LogUtil.Error("Shadow state calculations returned a terminator in the past");
            return DefaultForStar(Planetarium.fetch?.Sun);
        }

        return state;
    }

    public static ShadowState GetShadowState(Vessel vessel)
    {
        using var span = new TraceSpan("ShadowState.GetShadowState");

        return ValidateShadowState(GetShadowStateImpl(vessel), Planetarium.GetUniversalTime());
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

    private static unsafe ShadowState GetOrbitShadowState(Vessel vessel)
    {
        var stars = StarProvider.GetRelevantStars(vessel) ?? [];
        var settings = HighLogic.CurrentGame?.Parameters.CustomParams<Settings>();
        if (!(settings?.EnableOrbitShadows ?? false))
            return DefaultForStar(stars.FirstOrDefault());

        var system = SolarSystem.Record();
        var orbit = new Mathematics.Orbit(vessel);
        var UT = Planetarium.GetUniversalTime();

        var state = AlwaysInShadow();
        foreach (var star in stars)
        {
            // A vessel orbiting this star directly is reported lit with an
            // infinite terminator by ComputeOrbitTerminator itself (it guards the
            // ParentBodyIndex == starIndex case before building the umbra geometry).
            // Do not short-circuit here: that would drop an earlier, brighter lit
            // star and ignore the sunrise of a stronger, currently-shadowed star.
            var terminator = OrbitShadow.ComputeOrbitTerminator(
                system,
                orbit,
                (int)SolarSystem.GetBodyIndex(star),
                UT
            );

            // Lit by this (strongest currently-lit) star. The changepoint is the
            // earlier of this star setting and an even-stronger shadowed star
            // rising; see AggregateShadowState.
            if (!terminator.InShadow)
                return new(Math.Min(terminator.UT, state.NextTerminatorEstimate), false, star);

            if (terminator.UT < state.NextTerminatorEstimate)
                state = new(terminator.UT, true);
        }

        return state;
    }

    private static ShadowState GetLandedShadowState(Vessel vessel)
    {
        var stars = StarProvider.GetRelevantStars(vessel) ?? [];
        var settings = HighLogic.CurrentGame?.Parameters.CustomParams<Settings>();
        if (!(settings?.EnableLandedShadows ?? false))
            return DefaultForStar(stars.FirstOrDefault());

        var currentUT = Planetarium.GetUniversalTime();
        var planet = vessel.orbit.referenceBody;

        var state = AlwaysInShadow();
        foreach (var star in stars)
        {
            var sub = ComputeLandedShadowState(
                planet,
                star,
                vessel.latitude,
                vessel.longitude,
                currentUT
            );

            // Lit by this (strongest currently-lit) star. The changepoint is the
            // earlier of this star setting and an even-stronger shadowed star
            // rising; see AggregateShadowState.
            if (!sub.InShadow)
                return new(
                    Math.Min(sub.NextTerminatorEstimate, state.NextTerminatorEstimate),
                    false,
                    star
                );

            if (sub.NextTerminatorEstimate < state.NextTerminatorEstimate)
                state = new(sub.NextTerminatorEstimate, true);
        }

        return state;
    }

    /// <summary>
    /// Compute the shadow state for a single star acting on a vessel landed at
    /// the given coordinates on <paramref name="planet"/>.
    /// </summary>
    ///
    /// <remarks>
    /// This is the per-star core of <see cref="GetLandedShadowState"/>, factored
    /// out so that it can be exercised directly (without constructing a full
    /// <see cref="Vessel"/>) by tests.
    /// </remarks>
    internal static ShadowState ComputeLandedShadowState(
        CelestialBody planet,
        CelestialBody star,
        double latitude,
        double longitude,
        double currentUT
    )
    {
        var system = SolarSystem.Record();
        var planetIndex = (int)SolarSystem.GetBodyIndex(planet);
        var starIndex = (int)SolarSystem.GetBodyIndex(star);
        var referenceIndex = FindReferenceBodyIndex(planet, star);

        var normal = GetLandedSurfaceNormal(planet, latitude, longitude);
        var cosLatitude = Math.Cos(Math.Abs(latitude) * MathUtil.DEG2RAD);

        var terminator = LandedShadow.ComputeLandedTerminator(
            system,
            planetIndex,
            starIndex,
            referenceIndex,
            normal,
            cosLatitude,
            currentUT
        );

        return terminator.InShadow
            ? new ShadowState(terminator.UT, true)
            : new ShadowState(terminator.UT, false, star);
    }

    /// <summary>
    /// Get the surface normal for a landed vessel, expressed in the same
    /// reference frame that <see cref="SolarSystem"/> uses for orbital
    /// positions.
    /// </summary>
    ///
    /// <remarks>
    /// <see cref="CelestialBody.GetSurfaceNVector"/> returns the normal in
    /// Unity world space, whereas <see cref="SolarSystem"/> reconstructs body
    /// positions in KSP's internal (Z-up) orbit frame. We convert the normal
    /// into that orbit frame so the dot products in <see cref="LandedShadow"/>
    /// are meaningful; mixing the two frames silently inverts the lit/shadow
    /// result for landed vessels (issue #26).
    /// </remarks>
    internal static Vector3d GetLandedSurfaceNormal(
        CelestialBody planet,
        double latitude,
        double longitude
    )
    {
        var normal = planet.GetSurfaceNVector(latitude, longitude);
        return Planetarium.Zup.LocalToWorld(normal.xzy);
    }

    /// <summary>
    /// Schedule shadow state computation as an async job.
    /// </summary>
    ///
    /// <remarks>
    /// <para>
    /// The returned <see cref="ShadowHandle"/> can be <c>yield return</c>ed
    /// in a coroutine or completed synchronously with
    /// <see cref="ShadowHandle.Complete"/>.
    /// </para>
    ///
    /// <para>
    /// All managed data extraction happens on the main thread before the
    /// job is scheduled. The heavy computation runs in the job.
    /// </para>
    /// </remarks>
    internal static ShadowHandle ScheduleShadowState(Vessel vessel)
    {
        using var span = new TraceSpan("ShadowState.ScheduleShadowState");

        return vessel.situation switch
        {
            Vessel.Situations.ORBITING
            or Vessel.Situations.SUB_ORBITAL
            or Vessel.Situations.ESCAPING
            or Vessel.Situations.DOCKED => ScheduleOrbitShadowState(vessel),
            Vessel.Situations.LANDED
            or Vessel.Situations.SPLASHED
            or Vessel.Situations.PRELAUNCH
            or Vessel.Situations.FLYING => ScheduleLandedShadowState(vessel),
            _ => new ShadowHandle(AlwaysInSun(Planetarium.fetch?.Sun)),
        };
    }

    private static ShadowHandle ScheduleOrbitShadowState(Vessel vessel)
    {
        var stars = StarProvider.GetRelevantStars(vessel) ?? [];
        var settings = HighLogic.CurrentGame?.Parameters.CustomParams<Settings>();
        if (!(settings?.EnableOrbitShadows ?? false))
            return new ShadowHandle(DefaultForStar(stars.FirstOrDefault()));

        var system = SolarSystem.Record();
        var orbit = new Mathematics.Orbit(vessel);
        var UT = Planetarium.GetUniversalTime();

        List<CelestialBody> validStars = [];
        List<int> starIndexList = [];

        foreach (var star in stars)
        {
            // See GetOrbitShadowState: do not short-circuit when the vessel orbits
            // this star directly. ComputeOrbitTerminator handles it, and skipping
            // the remaining stars here is exactly the bug that let the async path
            // drop an earlier, brighter lit star that the synchronous path keeps.
            starIndexList.Add((int)SolarSystem.GetBodyIndex(star));
            validStars.Add(star);
        }

        if (validStars.Count == 0)
            return new ShadowHandle(AlwaysInShadow());

        return ScheduleShadowJob(
            system,
            validStars,
            starIndexList,
            referenceIndices: null,
            vesselOrbit: orbit,
            planetIndex: 0,
            normal: default,
            cosLatitude: 0,
            UT,
            isLanded: false
        );
    }

    private static ShadowHandle ScheduleLandedShadowState(Vessel vessel)
    {
        var stars = StarProvider.GetRelevantStars(vessel) ?? [];
        var settings = HighLogic.CurrentGame?.Parameters.CustomParams<Settings>();
        if (!(settings?.EnableLandedShadows ?? false))
            return new ShadowHandle(DefaultForStar(stars.FirstOrDefault()));

        var system = SolarSystem.Record();
        var currentUT = Planetarium.GetUniversalTime();
        var planet = vessel.orbit.referenceBody;
        var planetIndex = (int)SolarSystem.GetBodyIndex(planet);

        var normal = GetLandedSurfaceNormal(planet, vessel.latitude, vessel.longitude);
        var cosLatitude = Math.Cos(Math.Abs(vessel.latitude) * MathUtil.DEG2RAD);

        List<CelestialBody> validStars = [];
        List<int> starIndexList = [];
        List<int> referenceIndexList = [];

        foreach (var star in stars)
        {
            starIndexList.Add((int)SolarSystem.GetBodyIndex(star));
            referenceIndexList.Add(FindReferenceBodyIndex(planet, star));
            validStars.Add(star);
        }

        if (validStars.Count == 0)
            return new ShadowHandle(AlwaysInShadow());

        return ScheduleShadowJob(
            system,
            validStars,
            starIndexList,
            referenceIndexList,
            vesselOrbit: default,
            planetIndex,
            normal,
            cosLatitude,
            currentUT,
            isLanded: true
        );
    }

    private static ShadowHandle ScheduleShadowJob(
        SystemBody[] system,
        List<CelestialBody> stars,
        List<int> starIndices,
        List<int> referenceIndices,
        Mathematics.Orbit vesselOrbit,
        int planetIndex,
        Vector3d normal,
        double cosLatitude,
        double UT,
        bool isLanded
    )
    {
        BurstCrashHandler.Init();

        int starCount = stars.Count;

        var nativeBodies = new NativeArray<SystemBody>(system.Length, Allocator.TempJob);
        nativeBodies.CopyFrom(system);

        var nativeStarIndices = new NativeArray<int>(starCount, Allocator.TempJob);
        for (int i = 0; i < starCount; i++)
            nativeStarIndices[i] = starIndices[i];

        var nativeReferenceIndices = new NativeArray<int>(starCount, Allocator.TempJob);
        if (referenceIndices != null)
        {
            for (int i = 0; i < starCount; i++)
                nativeReferenceIndices[i] = referenceIndices[i];
        }

        var results = new NativeArray<OrbitShadow.Terminator>(starCount, Allocator.TempJob);

        var job = new ShadowJob
        {
            Bodies = nativeBodies,
            StarIndices = nativeStarIndices,
            ReferenceIndices = nativeReferenceIndices,
            Results = results,
            VesselOrbit = vesselOrbit,
            PlanetIndex = planetIndex,
            Normal = normal,
            CosLatitude = cosLatitude,
            UT = UT,
            StarCount = starCount,
            IsLanded = isLanded,
        };
        var jobHandle = job.Schedule(default);

        return new ShadowHandle(
            jobHandle,
            nativeBodies,
            nativeStarIndices,
            nativeReferenceIndices,
            results,
            stars
        );
    }

    private static int FindReferenceBodyIndex(CelestialBody planet, CelestialBody star)
    {
        HashSet<CelestialBody> ancestors = [];
        CelestialBody ancestor = star;

        do
        {
            if (!ancestors.Add(ancestor))
                break;
            ancestor = ancestor.orbit?.referenceBody;
        } while (ancestor != null);

        CelestialBody reference = planet;

        while (!ancestors.Contains(reference.referenceBody))
            reference = reference.referenceBody;

        return (int)SolarSystem.GetBodyIndex(reference);
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
}

internal ref struct OrbitalShadowCalcs { }
