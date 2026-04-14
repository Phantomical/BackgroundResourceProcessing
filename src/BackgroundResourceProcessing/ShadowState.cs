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
            return PhysicsGlobals.SolarLuminosity
                / (4 * Math.PI * distance * distance * PhysicsGlobals.SolarLuminosityAtHome);
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

    public static ShadowState GetShadowState(Vessel vessel)
    {
        using var span = new TraceSpan("ShadowState.GetShadowState");

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
            var bodies = GetReferenceBodies(vessel, star);
            if (ReferenceEquals(bodies.parent, star))
                return AlwaysInSun(star);

            var terminator = OrbitShadow.ComputeOrbitTerminator(
                system,
                orbit,
                (int)SolarSystem.GetBodyIndex(star),
                UT
            );

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

        var system = SolarSystem.Record();
        var currentUT = Planetarium.GetUniversalTime();
        var planet = vessel.orbit.referenceBody;
        var planetIndex = (int)SolarSystem.GetBodyIndex(planet);

        var normal = planet.GetSurfaceNVector(vessel.latitude, vessel.longitude);
        var cosLatitude = Math.Cos(Math.Abs(vessel.latitude) * MathUtil.DEG2RAD);

        var state = AlwaysInShadow();
        foreach (var star in stars)
        {
            var starIndex = (int)SolarSystem.GetBodyIndex(star);
            var referenceIndex = FindReferenceBodyIndex(planet, star);

            var terminator = LandedShadow.ComputeLandedTerminator(
                system,
                planetIndex,
                starIndex,
                referenceIndex,
                normal,
                cosLatitude,
                currentUT
            );

            if (!terminator.InShadow)
                return new(Math.Min(terminator.UT, state.NextTerminatorEstimate), false, star);

            if (terminator.UT < state.NextTerminatorEstimate)
                state = new(terminator.UT, true);
        }

        return state;
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
            var bodies = GetReferenceBodies(vessel, star);
            if (ReferenceEquals(bodies.parent, star))
                return new ShadowHandle(AlwaysInSun(star));

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

        var normal = planet.GetSurfaceNVector(vessel.latitude, vessel.longitude);
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
