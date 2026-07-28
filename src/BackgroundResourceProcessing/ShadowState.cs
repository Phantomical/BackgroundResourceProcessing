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

    /// <summary>
    /// Every star currently lighting the vessel, in descending flux order.
    /// <see cref="Star"/> is the first element. May be <c>null</c> when the
    /// per-star breakdown is unknown (legacy saves, states constructed from a
    /// single star); consumers must then fall back to <see cref="Star"/> alone.
    /// </summary>
    public List<CelestialBody> LitStars = star != null ? [star] : null;

    public static ShadowState AlwaysInSun(CelestialBody star) =>
        new(double.PositiveInfinity, false, star);

    public static ShadowState AlwaysInShadow() => new(double.PositiveInfinity, true);

    #region Solar Flux
    /// <summary>
    /// Get the relative solar flux available for the specified vessel as
    /// compared to the solar flux at home.
    /// </summary>
    ///
    /// <remarks>
    /// The loaded game accumulates <c>vessel.solarFlux</c> over every star, so
    /// this sums the contribution of every currently-lit star rather than only
    /// the strongest one.
    /// </remarks>
    public readonly double GetSolarFluxFactor(Vessel vessel)
    {
        if (LitStars != null)
        {
            double total = 0.0;
            foreach (var star in LitStars)
                total += StarProvider.GetSolarFluxFactor(star, vessel);
            return total;
        }

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
    /// the vessel is the strongest one currently lighting it; it becomes
    /// <see cref="Star"/>, and every lit star is collected into
    /// <see cref="LitStars"/> for flux summation.
    /// </para>
    ///
    /// <para>
    /// Because the available solar flux is the sum over every lit star, any
    /// star's rise or set changes the available power and is therefore a genuine
    /// changepoint: the reported terminator is the minimum across all stars,
    /// whether or not <see cref="InShadow"/> flips there.
    /// </para>
    ///
    /// <para>
    /// This is the single source of truth shared by the asynchronous
    /// (<see cref="BurstSolver.ShadowHandle.Complete"/>) and synchronous
    /// (<see cref="GetOrbitShadowState"/> / <see cref="GetLandedShadowState"/>)
    /// paths so the two cannot diverge.
    /// </para>
    /// </remarks>
    internal static ShadowState AggregateShadowState(
        ReadOnlySpan<OrbitShadow.Terminator> terminators,
        IReadOnlyList<CelestialBody> stars
    )
    {
        var estimate = double.PositiveInfinity;
        List<CelestialBody> lit = null;

        for (int i = 0; i < terminators.Length; i++)
        {
            var term = terminators[i];
            var ut = term.UT == double.MaxValue ? double.PositiveInfinity : term.UT;

            estimate = Math.Min(estimate, ut);
            if (!term.InShadow)
                (lit ??= []).Add(stars[i]);
        }

        if (lit == null)
            return new ShadowState(estimate, true);

        return new ShadowState(estimate, false, lit[0]) { LitStars = lit };
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

        // A vessel orbiting a star directly is reported lit with an infinite
        // terminator by ComputeOrbitTerminator itself (it guards the
        // ParentBodyIndex == starIndex case before building the umbra geometry),
        // so every star gets a terminator computed unconditionally — exactly the
        // per-star results the async ShadowJob produces.
        var terminators = new OrbitShadow.Terminator[stars.Count];
        for (int i = 0; i < stars.Count; i++)
        {
            terminators[i] = OrbitShadow.ComputeOrbitTerminator(
                system,
                orbit,
                (int)SolarSystem.GetBodyIndex(stars[i]),
                UT
            );
        }

        return AggregateShadowState(terminators, stars);
    }

    private static ShadowState GetLandedShadowState(Vessel vessel)
    {
        var stars = StarProvider.GetRelevantStars(vessel) ?? [];
        var settings = HighLogic.CurrentGame?.Parameters.CustomParams<Settings>();
        if (!(settings?.EnableLandedShadows ?? false))
            return DefaultForStar(stars.FirstOrDefault());

        var currentUT = Planetarium.GetUniversalTime();
        var planet = vessel.orbit.referenceBody;

        var system = SolarSystem.Record();
        var planetIndex = (int)SolarSystem.GetBodyIndex(planet);
        var normal = GetLandedSurfaceNormal(planet, vessel.latitude, vessel.longitude, currentUT);
        var cosLatitude = Math.Cos(Math.Abs(vessel.latitude) * MathUtil.DEG2RAD);

        // Compute every star's terminator unconditionally — exactly the per-star
        // results the async ShadowJob produces — then aggregate them through the
        // shared helper.
        var terminators = new OrbitShadow.Terminator[stars.Count];
        for (int i = 0; i < stars.Count; i++)
        {
            terminators[i] = LandedShadow.ComputeLandedTerminator(
                system,
                planetIndex,
                (int)SolarSystem.GetBodyIndex(stars[i]),
                FindReferenceBodyIndex(planet, stars[i]),
                normal,
                cosLatitude,
                currentUT
            );
        }

        return AggregateShadowState(terminators, stars);
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

        var normal = GetLandedSurfaceNormal(planet, latitude, longitude, currentUT);
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
    /// <para>
    /// The normal must be expressed in KSP's internal (Z-up) orbit frame — the
    /// frame <see cref="SolarSystem"/> reconstructs body positions in. Mixing
    /// frames silently inverts the lit/shadow result for landed vessels
    /// (issue #26).
    /// </para>
    ///
    /// <para>
    /// It is reconstructed here from KSP's rotation model
    /// (<c>initialRotation + 360 * rotPeriodRecip * UT</c> about the polar
    /// axis) rather than read from the body's world-space transform via
    /// <see cref="CelestialBody.GetSurfaceNVector"/>. The two agree exactly
    /// whenever the body's world state is live (converting the world normal
    /// through <see cref="Planetarium.Zup"/> cancels
    /// <see cref="Planetarium.InverseRotAngle"/>), but scenes that do not
    /// update every body (e.g. the main menu) leave stale world transforms
    /// behind, while the orbit positions the normal is dotted against are
    /// always computed from the model.
    /// </para>
    /// </remarks>
    internal static Vector3d GetLandedSurfaceNormal(
        CelestialBody planet,
        double latitude,
        double longitude,
        double currentUT
    )
    {
        double rotationAngle = planet.initialRotation % 360.0;
        if (planet.rotates && planet.rotationPeriod != 0.0)
            rotationAngle =
                (planet.initialRotation + 360.0 * planet.rotPeriodRecip * currentUT) % 360.0;

        Planetarium.CelestialFrame frame = default;
        Planetarium.CelestialFrame.PlanetaryFrame(0.0, 90.0, rotationAngle, ref frame);

        var local = Planetarium.SphericalVector(
            latitude * MathUtil.DEG2RAD,
            longitude * MathUtil.DEG2RAD
        );
        return frame.LocalToWorld(local);
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

        var normal = GetLandedSurfaceNormal(planet, vessel.latitude, vessel.longitude, currentUT);
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
        if (!node.TryGetDouble("NextTerminatorEstimate", ref state.NextTerminatorEstimate))
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

            // Older saves have no LitStar entries; fall back to the single
            // primary star.
            state.LitStars = ResolveLitStars(node) ?? [state.Star];
        }

        return state;
    }

    private static List<CelestialBody> ResolveLitStars(ConfigNode node)
    {
        var names = node.GetValues("LitStar");
        if (names == null || names.Length == 0)
            return null;

        List<CelestialBody> stars = [];
        foreach (var name in names)
        {
            var body = PSystemManager.Instance?.localBodies?.Find(body =>
                body.isStar && body.bodyName == name
            );
            if (body != null)
                stars.Add(body);
        }

        return stars.Count != 0 ? stars : null;
    }

    public readonly void Save(ConfigNode node)
    {
        node.AddValue("NextTerminatorEstimate", NextTerminatorEstimate);
        node.AddValue("InShadow", InShadow);
        if (Star != null)
            node.AddValue("Star", Star.bodyName);
        if (LitStars != null)
        {
            foreach (var star in LitStars)
            {
                if (star != null)
                    node.AddValue("LitStar", star.bodyName);
            }
        }
    }
}

internal ref struct OrbitalShadowCalcs { }
