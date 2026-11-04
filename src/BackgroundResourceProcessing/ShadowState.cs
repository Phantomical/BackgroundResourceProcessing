using System;
using System.Collections.Generic;
using System.Linq;
using BackgroundResourceProcessing.Mathematics;
using BackgroundResourceProcessing.Shadow;
using BackgroundResourceProcessing.Tracing;
using BackgroundResourceProcessing.Utils;

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

    /// <summary>
    /// The terminator for each star that this state was computed from, in the
    /// same order as the stars. This is used to move the state past
    /// <see cref="NextTerminatorEstimate"/> without computing it again (see
    /// <see cref="TryAdvance"/>). May be <c>null</c>, in which case the state
    /// has to be recomputed at <see cref="NextTerminatorEstimate"/>.
    /// </summary>
    internal StarTerminator[] Terminators = null;

    /// <summary>
    /// The shadow state of a vessel with respect to a single star.
    /// </summary>
    internal struct StarTerminator
    {
        public CelestialBody Star;

        /// <summary>
        /// Whether the vessel is in shadow until <see cref="UT"/>.
        /// </summary>
        public bool InShadow;

        /// <summary>
        /// The next time the vessel's shadow state for this star needs to be
        /// updated.
        /// </summary>
        public double UT;

        /// <summary>
        /// Whether the state after <see cref="UT"/> is known.
        /// </summary>
        public bool HasNext;

        /// <summary>
        /// Whether the vessel is in shadow from <see cref="UT"/> until
        /// <see cref="NextUT"/>.
        /// </summary>
        public bool NextInShadow;

        /// <summary>
        /// The time after <see cref="UT"/> at which the state needs to be
        /// updated again.
        /// </summary>
        public double NextUT;
    }

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

        /// <summary>
        /// Whether <paramref name="body"/> blocks light. Planet packs use
        /// invisible bodies that don't as the barycentres of binary systems.
        /// </summary>
        public virtual bool BlocksLight(CelestialBody body) => true;
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
    /// (<see cref="Shadow.ShadowBatch.GetResult"/>) and synchronous
    /// (<see cref="GetOrbitShadowState"/> / <see cref="GetLandedShadowState"/>)
    /// paths so the two cannot diverge.
    /// </para>
    /// </remarks>
    internal static ShadowState AggregateShadowState(
        ReadOnlySpan<OrbitShadow.Terminator> terminators,
        IReadOnlyList<CelestialBody> stars
    )
    {
        var result = new StarTerminator[terminators.Length];
        for (int i = 0; i < terminators.Length; i++)
        {
            var term = terminators[i];
            result[i] = new StarTerminator
            {
                Star = stars[i],
                InShadow = term.InShadow,
                UT = ToInfinity(term.UT),
                HasNext = term.HasNext,
                NextInShadow = term.NextInShadow,
                NextUT = ToInfinity(term.NextUT),
            };
        }

        return AggregateShadowState(result);
    }

    /// <summary>
    /// Combine per-star terminators into a single <see cref="ShadowState"/>,
    /// in the same way as the overload taking solver results.
    /// </summary>
    internal static ShadowState AggregateShadowState(StarTerminator[] terminators)
    {
        var estimate = double.PositiveInfinity;
        List<CelestialBody> lit = null;

        foreach (var term in terminators)
        {
            estimate = Math.Min(estimate, term.UT);
            if (!term.InShadow)
                (lit ??= []).Add(term.Star);
        }

        if (lit == null)
            return new ShadowState(estimate, true) { Terminators = terminators };

        return new ShadowState(estimate, false, lit[0])
        {
            LitStars = lit,
            Terminators = terminators,
        };
    }

    static double ToInfinity(double UT) => UT == double.MaxValue ? double.PositiveInfinity : UT;

    /// <summary>
    /// Move this state forward to <paramref name="UT"/> using the stored
    /// terminators, without computing anything.
    /// </summary>
    ///
    /// <returns>
    /// Whether the state at <paramref name="UT"/> is known. If not then the
    /// shadow state needs to be recomputed.
    /// </returns>
    ///
    /// <remarks>
    /// Each terminator is computed together with the one after it, so this
    /// can move each star past one terminator. A star that has already been
    /// moved past one, or that has no following terminator, needs a
    /// recompute.
    /// </remarks>
    internal readonly bool TryAdvance(double UT, out ShadowState advanced)
    {
        advanced = this;
        if (!(NextTerminatorEstimate <= UT))
            return true;
        if (Terminators is null)
            return false;

        var result = new StarTerminator[Terminators.Length];
        for (int i = 0; i < result.Length; i++)
        {
            var term = Terminators[i];
            if (term.UT <= UT)
            {
                if (!term.HasNext || !(term.NextUT > UT))
                    return false;

                term = new StarTerminator
                {
                    Star = term.Star,
                    InShadow = term.NextInShadow,
                    UT = term.NextUT,
                };
            }

            result[i] = term;
        }

        advanced = AggregateShadowState(result);
        return true;
    }

    /// <summary>
    /// Reject a computed shadow state whose terminator estimate is NaN or already
    /// in the past, falling back to a safe default for the given star.
    /// </summary>
    ///
    /// <remarks>
    /// Shared by the synchronous (<see cref="GetShadowState"/>) and asynchronous
    /// (<see cref="Shadow.ShadowBatch.GetResult"/>) paths so that every result
    /// — lit or shadowed — is validated identically.
    /// </remarks>
    ///
    /// <param name="state">The computed shadow state.</param>
    /// <param name="currentUT">The UT the state was computed at.</param>
    /// <param name="retryInterval">
    /// How long to wait before recomputing. See
    /// <see cref="GetRetryInterval(Vessel)"/>.
    /// </param>
    internal static ShadowState ValidateShadowState(
        ShadowState state,
        double currentUT,
        double retryInterval = ShadowRetryInterval
    )
    {
        // A bad terminator estimate doesn't mean the lit/shadowed state is
        // wrong, so keep that and only replace the estimate with a retry.
        // Falling back to "always lit" with an infinite estimate would mean the
        // shadow state never gets recomputed, so a vessel in shadow would run
        // its solar panels until next loaded.
        if (double.IsNaN(state.NextTerminatorEstimate))
        {
            LogUtil.Error("Shadow state calculations returned NaN terminator estimate");
            state.NextTerminatorEstimate = currentUT + retryInterval;
            state.Terminators = null;
            return state;
        }
        if (state.NextTerminatorEstimate < currentUT)
        {
            LogUtil.Error("Shadow state calculations returned a terminator in the past");
            state.NextTerminatorEstimate = currentUT + retryInterval;
            state.Terminators = null;
            return state;
        }

        return state;
    }

    /// <summary>
    /// The shortest time to wait before recomputing the shadow state when a
    /// shadow calculation fails.
    /// </summary>
    internal const double ShadowRetryInterval = 120.0;

    /// <summary>
    /// How long to wait before recomputing the shadow state for
    /// <paramref name="vessel"/> when a shadow calculation fails.
    /// </summary>
    ///
    /// <remarks>
    /// This is one orbit (or one solar day when landed), so that a vessel whose
    /// calculation keeps failing costs no more than a vessel in an eclipse
    /// season. A fixed interval would be recomputed on every frame at high
    /// time warp.
    /// </remarks>
    internal static double GetRetryInterval(Vessel vessel)
    {
        var orbit = vessel?.orbit;
        if (orbit is null)
            return ShadowRetryInterval;

        double period = vessel.situation switch
        {
            Vessel.Situations.LANDED
            or Vessel.Situations.SPLASHED
            or Vessel.Situations.PRELAUNCH
            or Vessel.Situations.FLYING => Math.Abs(orbit.referenceBody?.solarDayLength ?? 0.0),
            _ => orbit.eccentricity < 1.0 ? orbit.period : 0.0,
        };

        return GetRetryInterval(period);
    }

    /// <summary>
    /// The retry interval for a vessel whose orbit (or solar day) lasts
    /// <paramref name="period"/> seconds.
    /// </summary>
    internal static double GetRetryInterval(double period)
    {
        if (!MathUtil.IsFinite(period))
            return ShadowRetryInterval;
        return Math.Max(ShadowRetryInterval, period);
    }

    /// <summary>
    /// The shadow state to use when the shadow calculation failed outright.
    /// This is lit by the root star, but only until a retry
    /// <paramref name="retryInterval"/> after <paramref name="currentUT"/>.
    /// </summary>
    internal static ShadowState FallbackShadowState(
        double currentUT,
        double retryInterval = ShadowRetryInterval
    ) => new(currentUT + retryInterval, false, Planetarium.fetch?.Sun);

    /// <summary>
    /// Log any terminators for which the orbit solver gave up and reported the
    /// vessel lit forever, along with enough detail to reproduce it in a test.
    /// </summary>
    internal static void LogSolverFailures(
        ReadOnlySpan<OrbitShadow.Terminator> terminators,
        IReadOnlyList<CelestialBody> stars,
        string vesselName,
        in Mathematics.Orbit orbit,
        double UT
    )
    {
        for (int i = 0; i < terminators.Length; i++)
        {
            if (!terminators[i].Failed)
                continue;

            LogUtil.Warn(
                $"Orbit shadow calculation failed for vessel {vesselName} and star "
                    + $"{stars[i]?.bodyName}. The vessel will be treated as lit until its "
                    + $"orbit changes. UT={UT:R} parent={orbit.ParentBodyIndex} "
                    + $"sma={orbit.SemiMajorAxis:R} ecc={orbit.Eccentricity:R} "
                    + $"inc={orbit.Inclination:R} lan={orbit.LAN:R} "
                    + $"argPe={orbit.ArgumentOfPeriapsis:R} epoch={orbit.Epoch:R} "
                    + $"M0={orbit.MeanAnomalyAtEpoch:R}"
            );
        }
    }

    /// <summary>
    /// Log any terminators for which the landed solver gave up and reported
    /// the vessel lit forever, along with enough detail to reproduce it in a
    /// test.
    /// </summary>
    internal static void LogLandedSolverFailures(
        ReadOnlySpan<OrbitShadow.Terminator> terminators,
        IReadOnlyList<CelestialBody> stars,
        string vesselName,
        int planetIndex,
        Vector3d normal,
        double cosLatitude,
        double UT
    )
    {
        for (int i = 0; i < terminators.Length; i++)
        {
            if (!terminators[i].Failed)
                continue;

            LogUtil.Warn(
                $"Landed shadow calculation failed for vessel {vesselName} and star "
                    + $"{stars[i]?.bodyName}. The vessel will be treated as lit until it "
                    + $"moves. UT={UT:R} planet={planetIndex} "
                    + $"normal=({normal.x:R}, {normal.y:R}, {normal.z:R}) "
                    + $"cosLatitude={cosLatitude:R}"
            );
        }
    }

    public static ShadowState GetShadowState(Vessel vessel)
    {
        using var span = new TraceSpan("ShadowState.GetShadowState");

        return ValidateShadowState(
            GetShadowStateImpl(vessel),
            Planetarium.GetUniversalTime(),
            GetRetryInterval(vessel)
        );
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

        LogSolverFailures(terminators, stars, vessel.GetDisplayName(), in orbit, UT);
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

        LogLandedSolverFailures(
            terminators,
            stars,
            vessel.GetDisplayName(),
            planetIndex,
            normal,
            cosLatitude,
            currentUT
        );
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

    internal static int FindReferenceBodyIndex(CelestialBody planet, CelestialBody star)
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

        state.Terminators = LoadTerminators(node, state.NextTerminatorEstimate);
        return state;
    }

    /// <summary>
    /// Load the per-star terminators. Older saves don't have them, and any
    /// that don't match the rest of the state are dropped, since the only
    /// cost of dropping them is a recompute at the next terminator.
    /// </summary>
    private static StarTerminator[] LoadTerminators(ConfigNode node, double estimate)
    {
        var nodes = node.GetNodes("TERMINATOR");
        if (nodes == null || nodes.Length == 0)
            return null;

        var terminators = new StarTerminator[nodes.Length];
        var min = double.PositiveInfinity;
        for (int i = 0; i < nodes.Length; i++)
        {
            var child = nodes[i];
            StarTerminator term = default;

            string name = null;
            if (!child.TryGetValue("Star", ref name))
                return null;
            term.Star = PSystemManager.Instance?.localBodies?.Find(body =>
                body.isStar && body.bodyName == name
            );
            if (term.Star == null)
                return null;

            if (!child.TryGetValue("InShadow", ref term.InShadow))
                return null;
            if (!child.TryGetDouble("UT", ref term.UT))
                return null;
            if (
                child.TryGetDouble("NextUT", ref term.NextUT)
                && child.TryGetValue("NextInShadow", ref term.NextInShadow)
            )
                term.HasNext = true;

            min = Math.Min(min, term.UT);
            terminators[i] = term;
        }

        if (min != estimate)
            return null;

        return terminators;
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

        if (Terminators != null && Array.TrueForAll(Terminators, term => term.Star != null))
        {
            foreach (var term in Terminators)
            {
                var child = node.AddNode("TERMINATOR");
                child.AddValue("Star", term.Star.bodyName);
                child.AddValue("InShadow", term.InShadow);
                child.AddValue("UT", term.UT);
                if (term.HasNext)
                {
                    child.AddValue("NextInShadow", term.NextInShadow);
                    child.AddValue("NextUT", term.NextUT);
                }
            }
        }
    }
}

internal ref struct OrbitalShadowCalcs { }
