using System;
using System.Collections.Generic;
using BackgroundResourceProcessing.Mathematics;
using BackgroundResourceProcessing.Shadow;
using KSP.Testing;

namespace BackgroundResourceProcessing.Test.Shadow;

using static ShadowTestSystem;
using Orbit = BackgroundResourceProcessing.Mathematics.Orbit;

/// <summary>
/// Accuracy tests for <see cref="OrbitShadow"/>, checked against the
/// brute-force <see cref="ShadowReference"/>.
/// </summary>
///
/// <remarks>
/// <para>
/// The solver returns either the time of the next shadow transition or, when
/// there is no transition on the next orbit, a lower bound on when the next
/// one could happen. Both are fine as long as they are never <em>later</em>
/// than the real transition. Every check here is built around that:
/// </para>
///
/// <list type="bullet">
///   <item>the reported state at <c>UT</c> matches the reference;</item>
///   <item>the reported time is never later than the real transition (by more
///   than <see cref="Tolerance"/>), and an infinite result counts as late;</item>
///   <item>a reported time that matches the real transition is on the far side
///   of it, so evaluating the state at the changepoint gives the new state;</item>
///   <item>the reported time is never NaN and never before <c>UT</c>.</item>
/// </list>
/// </remarks>
public sealed class OrbitShadowAccuracyTests : BRPTestBase
{
    const double Deg = Math.PI / 180.0;

    /// <summary>
    /// The largest error allowed in a reported terminator, in seconds.
    /// </summary>
    const double Tolerance = 1.0;

    /// <summary>
    /// Cases where the vessel starts within this many metres of the shadow edge
    /// are skipped, since the solver and the reference may disagree on which
    /// side of it the vessel is.
    /// </summary>
    const double EdgeMargin = 1.0;

    /// <summary>
    /// A terminator is allowed to land this far (in metres) on the wrong side
    /// of the shadow edge, to absorb rounding differences between the solver
    /// and the reference.
    /// </summary>
    const double SideMargin = 1e-2;

    enum Outcome
    {
        Skipped,
        Exact,
        Early,
        NoTransition,
        Failed,
    }

    struct CaseResult
    {
        public Outcome Outcome;
        public string Message;
        public OrbitShadow.Terminator Terminator;
        public double Reference;
    }

    static CaseResult Check(
        SystemBody[] bodies,
        in Orbit orbit,
        double UT,
        double horizon,
        int star = Kerbol
    )
    {
        var reference = new ShadowReference(bodies, in orbit, star);
        var margin = reference.Margin(UT);
        if (Math.Abs(margin) < EdgeMargin)
            return new() { Outcome = Outcome.Skipped };

        var start = margin < 0.0;
        var term = Compute(bodies, in orbit, UT, star);
        var next = reference.NextTransition(UT, horizon);
        var desc = Describe(in orbit);

        string Fail(string why) =>
            $"{why}: reported {(term.InShadow ? "shadow" : "lit")} until {term.UT}, "
            + $"reference {(start ? "shadow" : "lit")} until {next} "
            + $"(UT {UT}, {desc})";

        CaseResult Result(Outcome outcome, string message = null) =>
            new()
            {
                Outcome = outcome,
                Message = message,
                Terminator = term,
                Reference = next,
            };

        if (term.InShadow != start)
            return Result(Outcome.Failed, Fail("wrong state"));

        var outcome = CheckSegment(reference, UT, start, term.UT, next, out var why);
        if (outcome == Outcome.Failed)
            return Result(outcome, Fail(why));
        if (!term.HasNext)
            return Result(outcome);

        // The following terminator is checked the same way, starting from the
        // first one. If the first one matched a real transition, then the
        // reference starts just past that transition so it isn't found again.
        // Shadow windows can be shorter than the tolerance, so this can't skip
        // a whole tolerance.
        var from = outcome == Outcome.Exact ? Math.Max(term.UT, next + 1e-3) : term.UT;
        var nextStart = reference.Margin(from);
        if (Math.Abs(nextStart) >= EdgeMargin && term.NextInShadow != nextStart < 0.0)
            return Result(
                Outcome.Failed,
                Fail(
                    $"wrong following state, reported {(term.NextInShadow ? "shadow" : "lit")} "
                        + $"until {term.NextUT:R}, margin {nextStart:G4}m at {from:R}"
                )
            );

        var following = reference.NextTransition(from, horizon);
        var nextOutcome = CheckSegment(
            reference,
            term.UT,
            term.NextInShadow,
            term.NextUT,
            following,
            out why
        );
        if (nextOutcome == Outcome.Failed)
            return Result(
                Outcome.Failed,
                Fail($"following terminator {term.NextUT} vs reference {following}: {why}")
            );

        return Result(outcome);
    }

    /// <summary>
    /// Check a reported terminator <paramref name="termUT"/> for a segment that
    /// starts at <paramref name="UT"/> in state <paramref name="start"/>,
    /// against the real transition <paramref name="next"/>.
    /// </summary>
    static Outcome CheckSegment(
        ShadowReference reference,
        double UT,
        bool start,
        double termUT,
        double next,
        out string why
    )
    {
        why = null;
        if (double.IsNaN(termUT))
        {
            why = "NaN terminator";
            return Outcome.Failed;
        }
        if (termUT < UT)
        {
            why = "terminator in the past";
            return Outcome.Failed;
        }

        if (double.IsInfinity(next))
            return double.IsInfinity(termUT) ? Outcome.NoTransition : Outcome.Early;

        if (termUT > next + Tolerance)
        {
            why = $"late by {termUT - next:G4}s";
            return Outcome.Failed;
        }

        if (termUT < next - Tolerance)
            return Outcome.Early;

        // The terminator matches the real transition, so the state there
        // should already be the new one.
        var after = reference.Margin(termUT);
        var wrongSide = start ? after < -SideMargin : after > SideMargin;
        if (wrongSide)
        {
            why = $"terminator is on the old side of the edge by {Math.Abs(after):G4}m";
            return Outcome.Failed;
        }

        return Outcome.Exact;
    }

    /// <summary>
    /// <see cref="Check"/>, returning the failure message if it failed and
    /// null otherwise.
    /// </summary>
    static string CheckFailure(SystemBody[] bodies, in Orbit orbit, double UT, double horizon)
    {
        var result = Check(bodies, in orbit, UT, horizon);
        return result.Outcome == Outcome.Failed ? result.Message : null;
    }

    static string Describe(in Orbit orbit) =>
        $"parent {DescribeBody((int)orbit.ParentBodyIndex)}, sma {orbit.SemiMajorAxis:R}, "
        + $"ecc {orbit.Eccentricity:R}, inc {orbit.Inclination / Deg:R}°, "
        + $"lan {orbit.LAN / Deg:R}°, argPe {orbit.ArgumentOfPeriapsis / Deg:R}°, "
        + $"M0 {orbit.MeanAnomalyAtEpoch:R}";

    /// <summary>
    /// The name of a body in <see cref="ShadowTestSystem"/>, or of an installed
    /// body when testing against the installed system.
    /// </summary>
    static string DescribeBody(int index)
    {
        if (installedNames is not null && index >= 0 && index < installedNames.Length)
            return installedNames[index];
        if (index >= 0 && index < Info.Length)
            return Info[index].Name;
        return $"body {index}";
    }

    // Not thread static, since cases are checked on worker threads. Tests run
    // one at a time, so only one test sets this at once.
    static volatile string[] installedNames;

    /// <summary>
    /// The angle of a planet around the star at <paramref name="UT"/>. This
    /// only works for the planets in <see cref="ShadowTestSystem"/> that have
    /// no inclination.
    /// </summary>
    static double SunAngle(SystemBody[] bodies, int planet, double UT)
    {
        var dir = GetPosition(bodies, planet, UT) - GetPosition(bodies, Kerbol, UT);
        return Math.Atan2(dir.y, dir.x);
    }

    static void AssertNoFailures(List<string> failures, int total, string what)
    {
        if (failures.Count == 0)
            return;

        var shown = string.Join("\n  ", failures.GetRange(0, Math.Min(failures.Count, 10)));
        throw new Exception(
            $"{failures.Count} failures across {total} {what}. The first few were:\n  {shown}"
        );
    }

    #region Grid sweep
    struct GridCase
    {
        public Orbit Orbit;
        public double UT;
    }

    static readonly int[] GridParents = [Kerbin, Mun, Moho, Laythe];
    static readonly double[] GridEccentricities = [0.0, 0.0, 0.01, 0.1, 0.3, 0.6, 0.9];

    /// <summary>
    /// Generate a fixed, seeded set of random orbits so that failures can be
    /// reproduced.
    /// </summary>
    static List<GridCase> GenerateGrid(SystemBody[] bodies, int count, int seed)
    {
        var rng = new Random(seed);
        var cases = new List<GridCase>(count);

        while (cases.Count < count)
        {
            var parent = GridParents[rng.Next(GridParents.Length)];
            var info = Info[parent];
            var ecc = GridEccentricities[rng.Next(GridEccentricities.Length)];

            // Keep periapsis above the surface and apoapsis inside the SOI.
            var minPe = 1.05 * info.Radius;
            var maxPe = 0.9 * info.SOI * (1.0 - ecc) / (1.0 + ecc);
            if (maxPe <= minPe)
                continue;

            var pe = minPe * Math.Pow(maxPe / minPe, rng.NextDouble());
            var orbit = MakeOrbitPe(
                bodies,
                parent,
                pe,
                ecc,
                rng.NextDouble() * Math.PI,
                rng.NextDouble() * 2 * Math.PI,
                rng.NextDouble() * 2 * Math.PI,
                rng.NextDouble() * 2 * Math.PI - Math.PI
            );

            // Pick a time anywhere in the planet's year, so the sun can be in
            // any direction relative to the orbit.
            var planet =
                parent == Mun ? Kerbin
                : parent == Laythe ? Jool
                : parent;
            var year = bodies[planet].Orbit.Period;

            cases.Add(new() { Orbit = orbit, UT = rng.NextDouble() * year });
        }

        return cases;
    }

    static void RunGrid(int count, int seed)
    {
        var bodies = Create();
        var failures = new List<string>();
        var counts = new int[5];
        int solverFailures = 0;

        var cases = GenerateGrid(bodies, count, seed);
        var results = ParallelCases.Run(
            cases.Count,
            i => Check(bodies, cases[i].Orbit, cases[i].UT, 2.5 * cases[i].Orbit.Period)
        );

        for (int i = 0; i < cases.Count; ++i)
        {
            var c = cases[i];
            var result = results[i];
            counts[(int)result.Outcome] += 1;
            if (result.Terminator.Failed)
            {
                solverFailures += 1;
                LogUtil.Log(
                    $"[BRP.Test] OrbitShadow solver gave up at UT {c.UT:R} ({Describe(in c.Orbit)})"
                );
            }
            if (result.Outcome == Outcome.Failed)
                failures.Add(result.Message);
        }

        LogUtil.Log(
            $"[BRP.Test] OrbitShadow grid (seed {seed}): "
                + $"{counts[(int)Outcome.Exact]} exact, {counts[(int)Outcome.Early]} early, "
                + $"{counts[(int)Outcome.NoTransition]} with no transition, "
                + $"{counts[(int)Outcome.Skipped]} skipped, {counts[(int)Outcome.Failed]} failed, "
                + $"{solverFailures} where the solver gave up"
        );

        AssertNoFailures(failures, count, "grid cases");
    }

    /// <summary>
    /// A seeded sweep over orbits around Kerbin, the Mun, Moho and Laythe,
    /// with every eccentricity, inclination and orientation, at every point in
    /// the planet's year.
    /// </summary>
    [TestInfo("OrbitShadowAccuracy_GridSweep")]
    public void GridSweep() => RunGrid(300, 1234);
    #endregion

    #region Multiple stars
    /// <summary>
    /// Random orbits around planets lit by a star that is not their ancestor,
    /// or that is an ancestor more than one level up:
    /// <list type="bullet">
    ///   <item>Kerbin and the Mun lit by Kerbol B, which orbits Kerbol like
    ///   Kerbin does, so the planet doesn't orbit the star and they share a
    ///   parent;</item>
    ///   <item>Tabbo lit by Kerbol, which is the parent of Tabbo's star.</item>
    /// </list>
    /// </summary>
    [TestInfo("OrbitShadowAccuracy_MultipleStars")]
    public void MultipleStars()
    {
        var bodies = Create();
        var rng = new Random(4321);
        var failures = new List<string>();
        var counts = new int[5];
        int total = 0;

        (int Parent, int Star)[] pairs =
        [
            (Kerbin, KerbolB),
            (Mun, KerbolB),
            (Tabbo, Kerbol),
            (Tabbo, KerbolB),
        ];

        var cases = new List<(Orbit Orbit, double UT, int Star)>();
        foreach (var (parent, star) in pairs)
        {
            for (int i = 0; i < 100; ++i)
            {
                var orbit = RandomOrbit(rng, bodies, parent, Info[parent]);
                var year = bodies[parent == Mun ? Kerbin : parent].Orbit.Period;
                cases.Add((orbit, rng.NextDouble() * year, star));
            }
        }

        var results = ParallelCases.Run(
            cases.Count,
            i =>
                Check(
                    bodies,
                    cases[i].Orbit,
                    cases[i].UT,
                    2.5 * cases[i].Orbit.Period,
                    star: cases[i].Star
                )
        );

        for (int i = 0; i < cases.Count; ++i)
        {
            var result = results[i];
            counts[(int)result.Outcome] += 1;
            total += 1;
            if (result.Outcome == Outcome.Failed)
                failures.Add($"star {DescribeBody(cases[i].Star)}: {result.Message}");
        }

        LogUtil.Log(
            $"[BRP.Test] OrbitShadow multiple stars: {counts[(int)Outcome.Exact]} exact, "
                + $"{counts[(int)Outcome.Early]} early, {counts[(int)Outcome.Failed]} failed"
        );

        AssertNoFailures(failures, total, "multiple star cases");
    }

    /// <summary>
    /// Random orbits around a moon, at times close to when the moon passes
    /// through its planet's shadow, so that the planet's shadow is usually
    /// the next one that the vessel enters or leaves.
    /// </summary>
    [TestInfo("OrbitShadowAccuracy_MoonInPlanetShadow")]
    public void MoonInPlanetShadow()
    {
        var bodies = Create();
        var rng = new Random(8642);

        (int Moon, int Planet, int Star)[] configs =
        [
            (Mun, Kerbin, Kerbol),
            (Laythe, Jool, Kerbol),
            (Mun, Kerbin, KerbolB),
        ];

        var cases = new List<(Orbit Orbit, double UT, int Star, double Horizon)>();
        foreach (var (moon, planet, star) in configs)
        {
            var moonOrbit = bodies[moon].Orbit;
            var moonSpeed = 2.0 * Math.PI * moonOrbit.SemiMajorAxis / moonOrbit.Period;

            for (int i = 0; i < 150; ++i)
            {
                var orbit = RandomOrbit(rng, bodies, moon, Info[moon]);
                var pass = FindAntiSunPass(
                    bodies,
                    moon,
                    planet,
                    star,
                    rng.NextDouble() * 10.0 * moonOrbit.Period
                );

                // The vessel can be in the planet's shadow while the moon is
                // within the planet's radius plus the vessel's apoapsis of
                // the shadow axis.
                var reach = Info[planet].Radius + orbit.ApR;
                var width = reach / moonSpeed;
                var UT = pass + (2.0 * rng.NextDouble() - 1.0) * 1.5 * width;
                cases.Add((orbit, UT, star, 2.5 * orbit.Period + 4.0 * width));
            }
        }

        var results = ParallelCases.Run(
            cases.Count,
            i => Check(bodies, cases[i].Orbit, cases[i].UT, cases[i].Horizon, star: cases[i].Star)
        );

        var failures = new List<string>();
        var counts = new int[5];
        for (int i = 0; i < cases.Count; ++i)
        {
            var result = results[i];
            counts[(int)result.Outcome] += 1;
            if (result.Outcome == Outcome.Failed)
                failures.Add($"star {DescribeBody(cases[i].Star)}: {result.Message}");
        }

        LogUtil.Log(
            $"[BRP.Test] OrbitShadow moon in planet shadow: {counts[(int)Outcome.Exact]} exact, "
                + $"{counts[(int)Outcome.Early]} early, "
                + $"{counts[(int)Outcome.NoTransition]} with no transition, "
                + $"{counts[(int)Outcome.Skipped]} skipped, {counts[(int)Outcome.Failed]} failed"
        );

        AssertNoFailures(failures, cases.Count, "moon cases");
    }

    #region Kcalbeloh regressions
    /// <summary>
    /// Check one case in <see cref="KcalbelohTestSystem"/>, with an orbit
    /// given as it is printed by <see cref="Describe"/>.
    /// </summary>
    static void CheckKcalbeloh(
        int parent,
        int star,
        double UT,
        double sma,
        double ecc,
        double incDeg,
        double lanDeg,
        double argPeDeg,
        double meanAnomalyAtEpoch
    )
    {
        var bodies = KcalbelohTestSystem.Create();
        var orbit = MakeOrbit(
            parent,
            KcalbelohTestSystem.Mu[parent],
            sma,
            ecc,
            incDeg * Deg,
            lanDeg * Deg,
            argPeDeg * Deg,
            meanAnomalyAtEpoch
        );

        installedNames = KcalbelohTestSystem.Names;
        try
        {
            var result = Check(bodies, in orbit, UT, 2.5 * orbit.Period, star);
            if (result.Outcome == Outcome.Failed)
                throw new Exception(result.Message);
        }
        finally
        {
            installedNames = null;
        }
    }

    /// <summary>
    /// A vessel around Simetra, one half of a binary planet, lit by AralcB.
    /// Simetra orbits the barycentre of the binary, which has a 10 km radius
    /// but doesn't block light, so it must not cast a shadow. The star Sunorc
    /// above it still does.
    /// </summary>
    [TestInfo("OrbitShadowAccuracy_KcalbelohBinaryPlanet")]
    public void KcalbelohBinaryPlanet() =>
        CheckKcalbeloh(
            KcalbelohTestSystem.Simetra,
            KcalbelohTestSystem.AralcB,
            24021.3590219167,
            8170597.199066828,
            0,
            58.361618192103514,
            240.30252456679125,
            122.55182275667404,
            0.881606545581822
        );

    /// <summary>
    /// A vessel around Elad, a moon of a planet orbiting the star AralcA, lit
    /// by the black hole Kcalbeloh. Positions here are around 10¹³ m, so
    /// rounding in absolute positions is around a millimetre.
    /// </summary>
    [TestInfo("OrbitShadowAccuracy_KcalbelohMoonOfPlanet")]
    public void KcalbelohMoonOfPlanet() =>
        CheckKcalbeloh(
            KcalbelohTestSystem.Elad,
            KcalbelohTestSystem.Kcalbeloh,
            341920.38132561,
            5423089.0278869914,
            0,
            32.872278882596774,
            127.28413096036954,
            290.513517917373,
            2.7475696630300481
        );

    /// <summary>
    /// Random orbits in Kcalbeloh's binary planet, where Simetra and Simeht
    /// orbit an invisible barycentre along with three small moons. A vessel
    /// around one of them can be in the shadow of any of the others, and a
    /// vessel orbiting the barycentre itself is only shadowed by them.
    /// </summary>
    [TestInfo("OrbitShadowAccuracy_KcalbelohBinarySystem")]
    public void KcalbelohBinarySystem()
    {
        var bodies = KcalbelohTestSystem.Create();
        var rng = new Random(2468);
        var year = bodies[KcalbelohTestSystem.SimeSystem].Orbit.Period;

        (int Parent, int Star, double Radius, double SOI)[] configs =
        [
            (KcalbelohTestSystem.Simetra, KcalbelohTestSystem.Sunorc, 4.8e6, 1.5e7),
            (KcalbelohTestSystem.Simeht, KcalbelohTestSystem.Sunorc, 4.0e6, 1.2e7),
            (KcalbelohTestSystem.Norihc, KcalbelohTestSystem.Sunorc, 3.36e5, 8.6e6),
            (KcalbelohTestSystem.Simetra, KcalbelohTestSystem.AralcB, 4.8e6, 1.5e7),
        ];

        var cases = new List<(Orbit Orbit, double UT, int Star)>();
        foreach (var (parent, star, radius, soi) in configs)
        {
            var info = new BodyInfo(
                KcalbelohTestSystem.Names[parent],
                radius,
                KcalbelohTestSystem.Mu[parent],
                soi
            );

            for (int i = 0; i < 50; ++i)
                cases.Add((RandomOrbit(rng, bodies, parent, info), rng.NextDouble() * year, star));
        }

        // Orbits around the barycentre, between the binary pair (which reach
        // out to about 29 Mm including Simeht's SOI) and Noira.
        for (int i = 0; i < 50; ++i)
        {
            var ecc = 0.1 * rng.NextDouble();
            var pe = 3.2e7 + 1.0e7 * rng.NextDouble();
            var orbit = MakeOrbit(
                KcalbelohTestSystem.SimeSystem,
                KcalbelohTestSystem.Mu[KcalbelohTestSystem.SimeSystem],
                pe / (1.0 - ecc),
                ecc,
                rng.NextDouble() * Math.PI,
                rng.NextDouble() * 2 * Math.PI,
                rng.NextDouble() * 2 * Math.PI,
                rng.NextDouble() * 2 * Math.PI - Math.PI
            );
            cases.Add((orbit, rng.NextDouble() * year, KcalbelohTestSystem.Sunorc));
        }

        installedNames = KcalbelohTestSystem.Names;
        CaseResult[] results;
        try
        {
            results = ParallelCases.Run(
                cases.Count,
                i =>
                    Check(
                        bodies,
                        cases[i].Orbit,
                        cases[i].UT,
                        2.5 * cases[i].Orbit.Period,
                        star: cases[i].Star
                    )
            );
        }
        finally
        {
            installedNames = null;
        }

        var failures = new List<string>();
        var counts = new int[5];
        for (int i = 0; i < cases.Count; ++i)
        {
            var result = results[i];
            counts[(int)result.Outcome] += 1;
            if (result.Outcome == Outcome.Failed)
                failures.Add($"star {KcalbelohTestSystem.Names[cases[i].Star]}: {result.Message}");
        }

        LogUtil.Log(
            $"[BRP.Test] OrbitShadow Kcalbeloh binary system: {counts[(int)Outcome.Exact]} exact, "
                + $"{counts[(int)Outcome.Early]} early, "
                + $"{counts[(int)Outcome.NoTransition]} with no transition, "
                + $"{counts[(int)Outcome.Skipped]} skipped, {counts[(int)Outcome.Failed]} failed"
        );

        AssertNoFailures(failures, cases.Count, "binary system cases");
    }
    #endregion

    /// <summary>
    /// The first time after <paramref name="from"/> at which
    /// <paramref name="moon"/> is closest to the direction pointing directly
    /// away from <paramref name="star"/>, as seen from
    /// <paramref name="planet"/>.
    /// </summary>
    static double FindAntiSunPass(SystemBody[] bodies, int moon, int planet, int star, double from)
    {
        double Cosine(double UT)
        {
            var planetPos = GetPosition(bodies, planet, UT);
            var sun = planetPos - GetPosition(bodies, star, UT);
            var rel = GetPosition(bodies, moon, UT) - planetPos;
            return Vector3d.Dot(sun, rel) / (sun.magnitude * rel.magnitude);
        }

        var period = bodies[moon].Orbit.Period;
        const int Samples = 720;
        var best = from;
        var bestCos = double.NegativeInfinity;
        for (int i = 0; i < Samples; ++i)
        {
            var t = from + period * i / Samples;
            var c = Cosine(t);
            if (c > bestCos)
                (best, bestCos) = (t, c);
        }

        // Golden section search around the best sample.
        var lo = best - period / Samples;
        var hi = best + period / Samples;
        for (int i = 0; i < 60; ++i)
        {
            var a = hi - 0.618 * (hi - lo);
            var b = lo + 0.618 * (hi - lo);
            if (Cosine(a) > Cosine(b))
                hi = b;
            else
                lo = a;
        }

        return 0.5 * (lo + hi);
    }

    /// <summary>
    /// A random orbit around <paramref name="parent"/> that stays above the
    /// surface and inside the SOI.
    /// </summary>
    static Orbit RandomOrbit(Random rng, SystemBody[] bodies, int parent, BodyInfo info)
    {
        while (true)
        {
            var ecc = GridEccentricities[rng.Next(GridEccentricities.Length)];
            var minPe = 1.05 * info.Radius;
            var maxPe = 0.9 * info.SOI * (1.0 - ecc) / (1.0 + ecc);
            if (maxPe <= minPe)
                continue;

            var pe = minPe * Math.Pow(maxPe / minPe, rng.NextDouble());
            return MakeOrbit(
                parent,
                info.Mu,
                pe / (1.0 - ecc),
                ecc,
                rng.NextDouble() * Math.PI,
                rng.NextDouble() * 2 * Math.PI,
                rng.NextDouble() * 2 * Math.PI,
                rng.NextDouble() * 2 * Math.PI - Math.PI
            );
        }
    }

    /// <summary>
    /// Random orbits around every body in the installed solar system, lit by
    /// every star in it. With a planet pack like Kcalbeloh installed this
    /// covers stars orbiting a black hole together with their planets, and
    /// planets lit by stars other than their own.
    /// </summary>
    ///
    /// <remarks>
    /// Stars are taken from Kopernicus if it is installed, or are just the
    /// root body otherwise. Only orbital data is used, so this works at the
    /// main menu.
    /// </remarks>
    [TestInfo("OrbitShadowAccuracy_InstalledSystem")]
    public void InstalledSystem()
    {
        var celestial = FlightGlobals.Bodies;
        if (celestial is null || celestial.Count == 0)
            return;

        var bodies = SolarSystem.Record();
        var stars = FindStars(celestial);
        var rng = new Random(9876);
        var failures = new List<string>();
        var counts = new int[5];
        int total = 0;
        int ancestor = 0;
        int other = 0;

        var names = new string[celestial.Count];
        for (int i = 0; i < names.Length; ++i)
            names[i] = celestial[i].bodyName;

        var cases = new List<(Orbit Orbit, double UT, int Star, bool IsAncestor)>();
        installedNames = names;
        try
        {
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
                {
                    for (int i = 0; i < 4; ++i)
                    {
                        var orbit = RandomOrbit(rng, bodies, b, info);
                        var UT = rng.NextDouble() * bodies[b].Orbit.Period;
                        cases.Add((orbit, UT, star, IsAncestor(bodies, star, b)));
                    }
                }
            }

            var results = ParallelCases.Run(
                cases.Count,
                i =>
                    Check(
                        bodies,
                        cases[i].Orbit,
                        cases[i].UT,
                        2.5 * cases[i].Orbit.Period,
                        star: cases[i].Star
                    )
            );

            for (int i = 0; i < cases.Count; ++i)
            {
                var (_, _, star, isAncestor) = cases[i];
                var result = results[i];
                counts[(int)result.Outcome] += 1;
                total += 1;
                if (isAncestor)
                    ancestor += 1;
                else
                    other += 1;

                if (result.Outcome == Outcome.Failed)
                    failures.Add(
                        $"star {names[star]} ({(isAncestor ? "ancestor" : "not an ancestor")}): "
                            + result.Message
                    );
            }
        }
        finally
        {
            installedNames = null;
        }

        LogUtil.Log(
            $"[BRP.Test] OrbitShadow installed system: {celestial.Count} bodies, "
                + $"{stars.Count} stars, {ancestor} cases with an ancestor star, {other} "
                + $"with another star: {counts[(int)Outcome.Exact]} exact, "
                + $"{counts[(int)Outcome.Early]} early, {counts[(int)Outcome.Skipped]} skipped, "
                + $"{counts[(int)Outcome.Failed]} failed"
        );

        AssertNoFailures(failures, total, "installed system cases");
    }

    static bool IsAncestor(SystemBody[] bodies, int star, int body)
    {
        for (int depth = 0; depth < bodies.Length; ++depth)
        {
            if (!bodies[body].HasOrbit)
                return false;

            var parent = bodies[body].Orbit.ParentBodyIndex ?? -1;
            if (parent < 0)
                return false;
            if (parent == star)
                return true;
            body = parent;
        }

        return false;
    }

    /// <summary>
    /// Find the indices of the stars, using Kopernicus if it is installed.
    /// </summary>
    internal static List<int> FindStars(List<CelestialBody> celestial)
    {
        var stars = new List<int>();

        Type kopernicusStar = null;
        foreach (var loaded in AssemblyLoader.loadedAssemblies)
        {
            kopernicusStar = loaded.assembly.GetType("Kopernicus.Components.KopernicusStar");
            if (kopernicusStar is not null)
                break;
        }

        var field = kopernicusStar?.GetField(
            "Stars",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static
        );
        if (field?.GetValue(null) is System.Collections.IEnumerable list)
        {
            var sunField = kopernicusStar.GetField("sun");
            foreach (var star in list)
            {
                if (sunField?.GetValue(star) is CelestialBody sun)
                {
                    var index = celestial.IndexOf(sun);
                    if (index >= 0 && !stars.Contains(index))
                        stars.Add(index);
                }
            }
        }

        if (stars.Count == 0 && Planetarium.fetch?.Sun is CelestialBody root)
            stars.Add(celestial.IndexOf(root));

        return stars;
    }
    #endregion

    #region Named regressions
    /// <summary>
    /// A polar orbit whose plane is close to perpendicular to the sun (a beta
    /// angle near 90°), so <c>ŝ·v</c> is tiny everywhere. The solver must still
    /// predict the start of the next eclipse season instead of giving up and
    /// returning an infinite terminator (F1).
    /// </summary>
    [TestInfo("OrbitShadowAccuracy_PolarOrbitNearBeta90")]
    public void PolarOrbitNearBeta90()
    {
        var bodies = Create();
        var cases = new List<Func<string>>();

        foreach (var UT in new double[] { 1.0e6, 3.3e6, 7.7e6 })
        {
            var theta = SunAngle(bodies, Kerbin, UT);

            foreach (var offset in new double[] { 0.0, 1e-9, 1e-6, 1e-3, 0.05, -0.05 })
            {
                var orbit = MakeOrbitPe(
                    bodies,
                    Kerbin,
                    700_000,
                    0.0,
                    90 * Deg,
                    theta + 90 * Deg + offset,
                    0.0,
                    0.3
                );

                cases.Add(() =>
                {
                    var reference = new ShadowReference(bodies, in orbit);
                    var term = Compute(bodies, in orbit, UT);

                    // The plane needs to turn by about 30° before the vessel
                    // can be shadowed, which is about a twelfth of a Kerbin
                    // year.
                    var season = reference.NextTransition(UT, 1.2e6);

                    if (term.InShadow || double.IsNaN(term.UT) || term.UT < UT)
                        return $"invalid result {term.UT} (UT {UT}, offset {offset})";
                    if (term.UT > season + Tolerance)
                        return $"reported lit until {term.UT}, but the season starts at "
                            + $"{season} (UT {UT}, offset {offset})";
                    return null;
                });
            }
        }

        AssertNoFailures(ParallelCases.Failures(cases), cases.Count, "polar orbits");
    }

    /// <summary>
    /// An eccentric polar orbit whose apoapsis is on the night side, shortly
    /// before an eclipse season starts. The first eclipses happen near
    /// apoapsis, which is not the point that is closest to the shadow in
    /// metres, so the season estimate must not be measured from there (F2).
    /// </summary>
    [TestInfo("OrbitShadowAccuracy_EccentricSeasonStart")]
    public void EccentricSeasonStart()
    {
        var bodies = Create();
        var cases = new List<Func<string>>();

        foreach (var UT in new double[] { 2.0e6, 6.0e6 })
        {
            var theta = SunAngle(bodies, Kerbin, UT);

            // The beta angle is sin(LAN - theta) and the apoapsis is on the
            // night side when LAN - theta is between 90° and 270°. The sun's
            // angle increases over time, so the beta angle approaches 0 from
            // these starting points.
            foreach (var start in new double[] { 14, 16, 20, 25, 40 })
            {
                var orbit = MakeOrbitPe(
                    bodies,
                    Kerbin,
                    700_000,
                    0.6,
                    90 * Deg,
                    theta + (180 + start) * Deg,
                    0.0,
                    1.0
                );

                cases.Add(() =>
                {
                    var reference = new ShadowReference(bodies, in orbit);
                    var term = Compute(bodies, in orbit, UT);

                    if (reference.InShadow(UT))
                        return null;

                    // At most 40° of sun motion, which is 1/9 of a Kerbin year.
                    var season = reference.NextTransition(UT, 1.1e6);

                    if (term.InShadow || double.IsNaN(term.UT) || term.UT < UT)
                        return $"invalid result {term.UT} (UT {UT}, start {start}°)";
                    if (term.UT > season + Tolerance)
                        return $"reported lit until {term.UT}, but the first eclipse is at "
                            + $"{season}, {(term.UT - season) / orbit.Period:F1} orbits earlier "
                            + $"(UT {UT}, start {start}°)";
                    return null;
                });
            }
        }

        AssertNoFailures(ParallelCases.Failures(cases), cases.Count, "eccentric orbits");
    }

    /// <summary>
    /// Circular orbits that only just pass through the shadow, so the shadow
    /// distance has a very shallow minimum. The entry and exit times must
    /// still be accurate and on the correct side of the edge (F3).
    /// </summary>
    [TestInfo("OrbitShadowAccuracy_GrazingPass")]
    public void GrazingPass()
    {
        var bodies = Create();
        var cases = new List<Func<string>>();

        foreach (var radius in new double[] { 700_000, 2_000_000, 10_000_000 })
        {
            foreach (var depth in new double[] { 1e-2, 1e-3, 1e-4, 1e-5 })
            {
                // Pick the beta angle so that the closest approach to the shadow
                // axis is `depth` planet radii inside the shadow.
                var beta = Math.Asin((1.0 - depth) * Info[Kerbin].Radius / radius);

                foreach (var UT in new double[] { 1.0e6, 5.0e6 })
                {
                    var theta = SunAngle(bodies, Kerbin, UT);
                    var orbit = MakeOrbitPe(
                        bodies,
                        Kerbin,
                        radius,
                        0.0,
                        90 * Deg,
                        theta + 90 * Deg - beta,
                        0.0,
                        0.0
                    );

                    // Start at a few points around the orbit.
                    for (int i = 0; i < 8; ++i)
                    {
                        var t = UT + i * orbit.Period / 8;
                        cases.Add(() => CheckFailure(bodies, in orbit, t, 2.5 * orbit.Period));
                    }
                }
            }
        }

        AssertNoFailures(ParallelCases.Failures(cases), cases.Count, "grazing passes");
    }

    /// <summary>
    /// A hyperbolic flyby whose periapsis is directly behind the planet. The
    /// vessel passes through the shadow near periapsis, and that must be found
    /// instead of reporting the vessel lit forever (F4).
    /// </summary>
    [TestInfo("OrbitShadowAccuracy_HyperbolicFlyby")]
    public void HyperbolicFlyby()
    {
        var bodies = Create();
        var failures = new List<string>();
        int total = 0;

        foreach (var UT in new double[] { 1.0e6, 5.0e6 })
        {
            var theta = SunAngle(bodies, Kerbin, UT);

            foreach (var ecc in new double[] { 1.05, 1.5, 3.0 })
            {
                foreach (var before in new double[] { 3000, 600, 100 })
                {
                    // Periapsis is on the anti-sunward side, and the vessel
                    // reaches it `before` seconds after UT.
                    var orbit = MakeOrbitPe(bodies, Kerbin, 700_000, ecc, 0.0, 0.0, theta, 0.0, UT);
                    orbit.ObTAtEpoch = -before;
                    orbit.MeanAnomalyAtEpoch = -before * orbit.MeanMotion;

                    var result = Check(bodies, in orbit, UT, 2 * before + 3000);
                    total += 1;
                    if (result.Outcome == Outcome.Failed)
                        failures.Add(result.Message);
                    else if (result.Outcome != Outcome.Exact)
                        failures.Add(
                            $"expected the shadow entry at {result.Reference}, "
                                + $"got {result.Terminator.UT} "
                                + $"(UT {UT}, ecc {ecc}, periapsis in {before}s)"
                        );
                }
            }
        }

        AssertNoFailures(failures, total, "hyperbolic flybys");
    }

    /// <summary>
    /// High orbits where the shadow covers only a small fraction of the orbit,
    /// smaller than the step size of the solver's fallback search (F6).
    /// </summary>
    [TestInfo("OrbitShadowAccuracy_HighOrbit")]
    public void HighOrbit()
    {
        var bodies = Create();
        var cases = new List<Func<string>>();

        foreach (var UT in new double[] { 1.0e6, 5.0e6 })
        {
            var theta = SunAngle(bodies, Kerbin, UT);

            foreach (
                var (pe, ecc) in new (double, double)[] { (30e6, 0.0), (60e6, 0.0), (10e6, 0.6) }
            )
            {
                foreach (var inc in new double[] { 0.0, 3.0, 20.0 })
                {
                    var orbit = MakeOrbitPe(
                        bodies,
                        Kerbin,
                        pe,
                        ecc,
                        inc * Deg,
                        theta + 90 * Deg,
                        90 * Deg,
                        0.0
                    );

                    for (int i = 0; i < 8; ++i)
                    {
                        var t = UT + i * orbit.Period / 8;
                        cases.Add(() => CheckFailure(bodies, in orbit, t, 1.5 * orbit.Period));
                    }
                }
            }
        }

        AssertNoFailures(ParallelCases.Failures(cases), cases.Count, "high orbits");
    }
    #endregion

    #region Lag budget
    struct Simulation
    {
        public int Recomputes;
        public int Changepoints;

        /// <summary>
        /// Changepoints at which the state didn't change.
        /// </summary>
        public int Idle;
        public int Orbits;
        public List<string> Failures;
    }

    /// <summary>
    /// Simulate the changepoints that a background vessel would run over
    /// <paramref name="duration"/> seconds, using the real validator. Every
    /// result is also checked against the reference, so that an eclipse that
    /// is skipped over entirely is reported as a failure.
    /// </summary>
    static Simulation Simulate(
        SystemBody[] bodies,
        in Orbit orbit,
        double UT,
        double duration,
        int star = Kerbol
    )
    {
        var reference = new ShadowReference(bodies, in orbit, star);
        var failures = new List<string>();
        var end = UT + duration;
        int recomputes = 0;
        int changepoints = 0;
        int idle = 0;
        ShadowState? previous = null;

        while (UT < end && changepoints < 200_000)
        {
            // Move forward through the stored terminators like the processor
            // does, and only recompute when that isn't possible.
            ShadowState state;
            if (previous is ShadowState prev && prev.TryAdvance(UT, out var advanced))
            {
                state = advanced;
            }
            else
            {
                var term = Compute(bodies, in orbit, UT, star: star);
                state = ShadowState.ValidateShadowState(
                    ShadowState.AggregateShadowState(
                        new OrbitShadow.Terminator[] { term },
                        new CelestialBody[] { null }
                    ),
                    UT,
                    ShadowState.GetRetryInterval(orbit.Period)
                );
                recomputes += 1;
            }
            // A changepoint that doesn't change the state is only there to
            // check whether an eclipse season has started.
            if (previous is ShadowState last && last.InShadow == state.InShadow)
                idle += 1;

            previous = state;
            changepoints += 1;

            var margin = reference.Margin(UT);
            if (Math.Abs(margin) >= EdgeMargin && state.InShadow != margin < 0.0)
                failures.Add($"wrong state at UT {UT} ({Describe(in orbit)})");

            // A changepoint can't happen before the next frame, even at the
            // highest time warp.
            var next = Math.Max(state.NextTerminatorEstimate, UT + 1e-3);
            if (double.IsInfinity(next))
            {
                var missed = reference.NextTransition(UT, end - UT);
                if (!double.IsInfinity(missed))
                    failures.Add(
                        $"stopped recomputing at UT {UT}, but the state changes at {missed} "
                            + $"({Describe(in orbit)})"
                    );
                break;
            }

            // Check that the state doesn't change before the next recompute,
            // other than right at the end.
            var change = reference.NextTransition(UT, Math.Min(next, end) - UT);
            if (change < next - Tolerance)
                failures.Add(
                    $"missed a transition at {change}, next recompute was at {next} "
                        + $"(UT {UT}, {Describe(in orbit)})"
                );

            UT = next;
        }

        return new()
        {
            Recomputes = recomputes,
            Changepoints = changepoints,
            Idle = idle,
            Orbits = (int)Math.Ceiling(duration / orbit.Period),
            Failures = failures,
        };
    }

    /// <summary>
    /// Over a whole Kerbin year, through both eclipse seasons, the number of
    /// shadow changepoints should stay at roughly two per orbit, and only every
    /// other one should need the shadow state to be recomputed. This turns
    /// the no-lag policy into a test: a fixed <c>t + N</c> retry loop would blow
    /// through the budget for any orbit longer than a few minutes.
    /// </summary>
    [TestInfo("OrbitShadowAccuracy_LagBudget")]
    public void LagBudget()
    {
        var bodies = Create();
        var year = bodies[Kerbin].Orbit.Period;
        var failures = new List<string>();

        (string, Orbit, int)[] orbits =
        [
            ("polar", MakeOrbitPe(bodies, Kerbin, 2_000_000, 0.0, 90 * Deg, 0.4, 0.0, 0.0), Kerbol),
            (
                "polar eccentric",
                MakeOrbitPe(bodies, Kerbin, 800_000, 0.5, 85 * Deg, 1.3, 2.0, 0.0),
                Kerbol
            ),
            (
                "inclined",
                MakeOrbitPe(bodies, Kerbin, 5_000_000, 0.1, 40 * Deg, 2.1, 0.7, 0.0),
                Kerbol
            ),
            (
                "munar polar",
                MakeOrbitPe(bodies, Mun, 400_000, 0.2, 90 * Deg, 0.9, 1.0, 0.0),
                Kerbol
            ),
            (
                "polar, lit by Kerbol B",
                MakeOrbitPe(bodies, Kerbin, 2_000_000, 0.0, 90 * Deg, 0.4, 0.0, 0.0),
                KerbolB
            ),
        ];

        var sims = ParallelCases.Run(
            orbits.Length,
            i => Simulate(bodies, in orbits[i].Item2, 1.0e5, year, orbits[i].Item3)
        );

        for (int i = 0; i < orbits.Length; ++i)
        {
            var name = orbits[i].Item1;
            var sim = sims[i];
            failures.AddRange(sim.Failures);

            LogUtil.Log(
                $"[BRP.Test] OrbitShadow lag budget ({name}): {sim.Recomputes} recomputes "
                    + $"and {sim.Changepoints} changepoints ({sim.Idle} idle) over {sim.Orbits} orbits"
            );

            if (sim.Changepoints > 3 * sim.Orbits + 10)
                failures.Add(
                    $"{name}: {sim.Changepoints} changepoints over {sim.Orbits} orbits "
                        + "is more than 3 per orbit"
                );

            // Outside of eclipse season the state shouldn't be checked every
            // orbit. In season an orbit can still miss the shadow near the
            // edges of the season, which is where idle changepoints come from.
            if (sim.Idle > sim.Orbits / 4 + 20)
                failures.Add(
                    $"{name}: {sim.Idle} changepoints that didn't change the state over "
                        + $"{sim.Orbits} orbits is more than one every 4 orbits"
                );

            // Every recompute also computes the terminator after the next one,
            // so only every other changepoint should need one. Around a moon,
            // each pass through the planet's shadow breaks up those pairs for
            // the moon's own shadow, so allow one more for each moon orbit.
            var parent = (int)orbits[i].Item2.ParentBodyIndex;
            var moonOrbits = parent == Mun ? (int)Math.Ceiling(year / bodies[Mun].Orbit.Period) : 0;
            if (sim.Recomputes > sim.Changepoints / 2 + 10 + moonOrbits)
                failures.Add(
                    $"{name}: {sim.Recomputes} recomputes for {sim.Changepoints} changepoints "
                        + "is more than one for every two"
                );
        }

        AssertNoFailures(failures, orbits.Length, "lag budget simulations");
    }
    #endregion
}
