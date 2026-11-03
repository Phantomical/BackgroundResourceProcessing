using System;
using System.Collections.Generic;
using BackgroundResourceProcessing.Mathematics;
using BackgroundResourceProcessing.Shadow;
using KSP.Testing;

namespace BackgroundResourceProcessing.Test.Shadow;

using static ShadowTestSystem;

/// <summary>
/// Tests for <see cref="EclipseSeason"/>, checked against sampling the eclipse
/// season condition <c>|ŝ·n̂| &lt; R/r_pe + R/|d|</c> directly.
/// </summary>
public sealed class EclipseSeasonTests : BRPTestBase
{
    /// <summary>
    /// The planet and star pairs to test, covering:
    /// <list type="bullet">
    ///   <item>a star that is a sibling of the planet (Kerbin, Kerbol B);</item>
    ///   <item>a moon lit by its planet's sibling (Mun, Kerbol B);</item>
    ///   <item>a star two levels up (Tabbo, Kerbol);</item>
    ///   <item>the star the planet orbits (Tabbo, Kerbol B; Kerbin, Kerbol),
    ///   which the exact bound normally handles;</item>
    ///   <item>a moon whose planet orbits the star (Laythe, Kerbol).</item>
    /// </list>
    /// </summary>
    static readonly (int Planet, int Star)[] Pairs =
    [
        (Kerbin, KerbolB),
        (Mun, KerbolB),
        (Moho, KerbolB),
        (Tabbo, Kerbol),
        (Tabbo, KerbolB),
        (Kerbin, Kerbol),
        (Laythe, Kerbol),
    ];

    const int CasesPerPair = 20;

    /// <summary>
    /// The most samples to take when searching for the start of a season.
    /// </summary>
    const double MaxSamples = 200_000;

    struct Case
    {
        public int Planet;
        public int Star;
        public BodyInfo Info;
        public string StarName;
        public double Pe;
        public double Inc;
        public double Lan;
        public double Seed;
    }

    struct CaseResult
    {
        public string Failure;

        /// <summary>
        /// Whether the case started outside of an eclipse season and found
        /// the start of the next one.
        /// </summary>
        public bool Counted;

        /// <summary>
        /// The number of calls to the bound before reaching the season.
        /// </summary>
        public int Calls;

        /// <summary>
        /// The number of vessel orbits until the season starts, which is
        /// how many times the solver would check without the bound.
        /// </summary>
        public double Orbits;

        public string Description;
    }

    struct Stats
    {
        public int Seasons;
        public int Calls;
        public int MaxCalls;
        public double Orbits;
        public string Worst;

        public void Add(in CaseResult result)
        {
            if (!result.Counted)
                return;

            Seasons += 1;
            Calls += result.Calls;
            Orbits += result.Orbits;
            if (result.Calls > MaxCalls)
            {
                MaxCalls = result.Calls;
                Worst = result.Description;
            }
        }

        public readonly string Summary =>
            $"{Seasons} off-seasons, {(double)Calls / Math.Max(1, Seasons):F1} calls per "
            + $"off-season (max {MaxCalls}), {Orbits / Math.Max(1, Seasons):F0} orbits per "
            + $"off-season. Most calls: {Worst}";
    }

    [TestInfo("EclipseSeason_NeverLaterThanSeasonStart")]
    public void NeverLaterThanSeasonStart()
    {
        var bodies = Create();
        var rng = new Random(2468);
        var cases = new List<Case>();

        foreach (var (planet, star) in Pairs)
        {
            for (int i = 0; i < CasesPerPair; ++i)
                cases.Add(RandomCase(rng, planet, star, Info[planet], Info[star].Name));
        }

        var results = ParallelCases.Run(cases.Count, i => RunCase(bodies, cases[i]));

        var failures = new List<string>();
        for (int p = 0; p < Pairs.Length; ++p)
        {
            var stats = new Stats();
            for (int i = p * CasesPerPair; i < (p + 1) * CasesPerPair; ++i)
            {
                stats.Add(results[i]);
                if (results[i].Failure is not null)
                    failures.Add(results[i].Failure);
            }

            var (planet, star) = Pairs[p];
            LogUtil.Log(
                $"[BRP.Test] EclipseSeason {Info[planet].Name} lit by {Info[star].Name}: "
                    + stats.Summary
            );
        }

        AssertNoFailures(failures, cases.Count);
    }

    /// <summary>
    /// Random orbits around every body in the installed solar system, lit by
    /// every star in it. With Kcalbeloh installed this covers planets lit by
    /// stars that orbit a black hole along with their own star.
    /// </summary>
    [TestInfo("EclipseSeason_InstalledSystem")]
    public void InstalledSystem()
    {
        var celestial = FlightGlobals.Bodies;
        if (celestial is null || celestial.Count == 0)
            return;

        var bodies = SolarSystem.Record();
        var stars = OrbitShadowAccuracyTests.FindStars(celestial);
        var rng = new Random(1357);
        var cases = new List<Case>();

        for (int b = 0; b < celestial.Count; ++b)
        {
            var body = celestial[b];
            if (!bodies[b].HasOrbit || stars.Contains(b))
                continue;

            var info = new BodyInfo(
                body.bodyName,
                body.Radius,
                body.gravParameter,
                body.sphereOfInfluence
            );

            foreach (var star in stars)
                cases.Add(RandomCase(rng, b, star, info, celestial[star].bodyName));
        }

        var results = ParallelCases.Run(cases.Count, i => RunCase(bodies, cases[i]));

        var failures = new List<string>();
        var stats = new Stats();
        foreach (var result in results)
        {
            stats.Add(result);
            if (result.Failure is not null)
                failures.Add(result.Failure);
        }

        LogUtil.Log(
            $"[BRP.Test] EclipseSeason installed system: {cases.Count} cases, " + stats.Summary
        );

        AssertNoFailures(failures, cases.Count);
    }

    static void AssertNoFailures(List<string> failures, int total)
    {
        if (failures.Count == 0)
            return;

        var shown = string.Join("\n  ", failures.GetRange(0, Math.Min(failures.Count, 10)));
        throw new Exception(
            $"{failures.Count} failures across {total} cases. The first few were:\n  {shown}"
        );
    }

    static Case RandomCase(Random rng, int planet, int star, BodyInfo info, string starName) =>
        new()
        {
            Planet = planet,
            Star = star,
            Info = info,
            StarName = starName,
            Pe =
                1.1
                * info.Radius
                * Math.Pow(0.3 * info.SOI / (1.1 * info.Radius), rng.NextDouble()),
            Inc = Math.Acos(2.0 * rng.NextDouble() - 1.0),
            Lan = 2.0 * Math.PI * rng.NextDouble(),
            Seed = rng.NextDouble(),
        };

    /// <summary>
    /// Check the bound for one case. This runs on a worker thread, so it must
    /// not log.
    /// </summary>
    static CaseResult RunCase(SystemBody[] bodies, Case c)
    {
        var info = c.Info;
        var pe = c.Pe;
        var period = 2.0 * Math.PI * Math.Sqrt(pe * pe * pe / info.Mu);
        var normal = MakeOrbit(c.Planet, info.Mu, pe, 0.0, c.Inc, c.Lan, 0.0, 0.0).OrbitFrame.Z;

        var (fast, slow) = GetPeriods(bodies, c.Planet, c.Star);
        if (!(slow > 0.0))
            return default;

        var UT = c.Seed * slow;
        var horizon = 1.2 * slow;
        var step = Math.Min(fast / 100.0, slow / 20_000.0);
        step = Math.Max(step, horizon / MaxSamples);

        var desc =
            $"{info.Name} lit by {c.StarName}, pe {pe:R}, inc {c.Inc:R}, lan {c.Lan:R}, "
            + $"UT {UT:R}";

        var radius = info.Radius;
        if (IsInSeason(bodies, c.Planet, c.Star, radius, normal, pe, UT))
            return default;

        var season = FindSeason(
            bodies,
            c.Planet,
            c.Star,
            radius,
            normal,
            pe,
            UT,
            UT + horizon,
            step
        );

        // Call the bound like the solver does: search one vessel orbit, then
        // ask again from the end of it, until the bound says that an eclipse
        // is possible.
        var t = UT;
        int calls = 0;
        while (calls < 100_000)
        {
            var next = EclipseSeason.ComputeNextPossibleEclipse(
                bodies,
                c.Planet,
                c.Star,
                normal,
                pe,
                period,
                t
            );
            calls += 1;

            if (double.IsNaN(next) || next < t)
                return new() { Failure = $"returned {next} from {t} ({desc})" };
            if (next > season)
                return new()
                {
                    Failure = $"returned {next:R} but the season starts at {season:R} ({desc})",
                };
            if (double.IsPositiveInfinity(next))
                return default;
            if (IsInSeason(bodies, c.Planet, c.Star, radius, normal, pe, next))
                break;

            t = next + period;
            if (t >= season || t >= UT + horizon)
                break;
        }

        if (double.IsInfinity(season))
            return default;

        return new()
        {
            Counted = true,
            Calls = calls,
            Orbits = (season - UT) / period,
            Description = $"{desc}, season {season:R}, vessel period {period:R}",
        };
    }

    /// <summary>
    /// The shortest and longest orbital periods of the bodies whose motion
    /// moves the star relative to the planet: those below the common ancestor
    /// of the two.
    /// </summary>
    static (double Fast, double Slow) GetPeriods(SystemBody[] bodies, int planet, int star)
    {
        var planetChain = GetAncestors(bodies, planet);
        var starChain = GetAncestors(bodies, star);

        double fast = double.PositiveInfinity;
        double slow = 0.0;
        foreach (var (chain, other) in new[] { (planetChain, starChain), (starChain, planetChain) })
        {
            foreach (var index in chain)
            {
                if (other.Contains(index))
                    break;

                var period = bodies[index].Orbit.Period;
                fast = Math.Min(fast, period);
                slow = Math.Max(slow, period);
            }
        }

        return (fast, slow);
    }

    static List<int> GetAncestors(SystemBody[] bodies, int index)
    {
        var chain = new List<int>();
        while (index >= 0 && chain.Count <= bodies.Length)
        {
            chain.Add(index);
            if (!bodies[index].HasOrbit)
                break;
            index = bodies[index].Orbit.ParentBodyIndex ?? -1;
        }

        return chain;
    }

    static bool IsInSeason(
        SystemBody[] bodies,
        int planet,
        int star,
        double radius,
        Vector3d normal,
        double pe,
        double UT
    )
    {
        var d = GetPosition(bodies, star, UT) - GetPosition(bodies, planet, UT);
        var D = d.magnitude;
        return Math.Abs(Vector3d.Dot(d, normal)) / D < radius / pe + radius / D;
    }

    /// <summary>
    /// The first sampled time in the range at which an eclipse is possible,
    /// or infinity if there is none. Sampling can only find a season later
    /// than it really starts, never earlier, so a bound that is later than
    /// this is definitely wrong.
    /// </summary>
    static double FindSeason(
        SystemBody[] bodies,
        int planet,
        int star,
        double radius,
        Vector3d normal,
        double pe,
        double start,
        double end,
        double step
    )
    {
        for (var t = start; t < end; t += step)
        {
            if (IsInSeason(bodies, planet, star, radius, normal, pe, t))
                return t;
        }

        return double.PositiveInfinity;
    }
}
