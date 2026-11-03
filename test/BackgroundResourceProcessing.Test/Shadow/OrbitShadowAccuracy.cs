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
        bool burst = false
    )
    {
        var reference = new ShadowReference(bodies, in orbit);
        var margin = reference.Margin(UT);
        if (Math.Abs(margin) < EdgeMargin)
            return new() { Outcome = Outcome.Skipped };

        var start = margin < 0.0;
        var term = Compute(bodies, in orbit, UT, burst);
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
        if (double.IsNaN(term.UT))
            return Result(Outcome.Failed, Fail("NaN terminator"));
        if (term.UT < UT)
            return Result(Outcome.Failed, Fail("terminator in the past"));

        if (double.IsInfinity(next))
            return Result(double.IsInfinity(term.UT) ? Outcome.NoTransition : Outcome.Early);

        if (term.UT > next + Tolerance)
            return Result(Outcome.Failed, Fail($"late by {term.UT - next:G4}s"));

        if (term.UT < next - Tolerance)
            return Result(Outcome.Early);

        // The terminator matches the real transition, so the state there
        // should already be the new one.
        var after = reference.Margin(term.UT);
        var wrongSide = start ? after < -SideMargin : after > SideMargin;
        if (wrongSide)
            return Result(
                Outcome.Failed,
                Fail($"terminator is on the old side of the edge by {Math.Abs(after):G4}m")
            );

        return Result(Outcome.Exact);
    }

    static string Describe(in Orbit orbit) =>
        $"parent {Info[(int)orbit.ParentBodyIndex].Name}, sma {orbit.SemiMajorAxis:R}, "
        + $"ecc {orbit.Eccentricity:R}, inc {orbit.Inclination / Deg:R}°, "
        + $"lan {orbit.LAN / Deg:R}°, argPe {orbit.ArgumentOfPeriapsis / Deg:R}°, "
        + $"M0 {orbit.MeanAnomalyAtEpoch:R}";

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

    static void RunGrid(int count, int seed, bool burst)
    {
        var bodies = Create();
        var failures = new List<string>();
        var counts = new int[5];
        int solverFailures = 0;

        foreach (var c in GenerateGrid(bodies, count, seed))
        {
            var result = Check(bodies, in c.Orbit, c.UT, 2.5 * c.Orbit.Period, burst);
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
            $"[BRP.Test] OrbitShadow grid (seed {seed}, burst {burst}): "
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
    public void GridSweep() => RunGrid(300, 1234, burst: false);
    #endregion

    #region Burst parity
    /// <summary>
    /// The Burst-compiled solver must give the same results as the managed
    /// one. This catches <c>FloatMode.Fast</c> optimising away NaN and
    /// infinity checks (F7).
    /// </summary>
    [TestInfo("OrbitShadowAccuracy_BurstParity")]
    public void BurstParity()
    {
        var bodies = Create();
        var failures = new List<string>();
        var cases = GenerateGrid(bodies, 300, 1234);

        foreach (var c in cases)
        {
            var managed = Compute(bodies, in c.Orbit, c.UT, burst: false);
            var burst = Compute(bodies, in c.Orbit, c.UT, burst: true);

            var same =
                managed.InShadow == burst.InShadow
                && (managed.UT.Equals(burst.UT) || Math.Abs(managed.UT - burst.UT) <= 1e-3);

            if (!same)
                failures.Add(
                    $"managed {(managed.InShadow ? "shadow" : "lit")} until {managed.UT:R}, "
                        + $"burst {(burst.InShadow ? "shadow" : "lit")} until {burst.UT:R} "
                        + $"(UT {c.UT:R}, {Describe(in c.Orbit)})"
                );
        }

        AssertNoFailures(failures, cases.Count, "parity cases");
    }

    /// <summary>
    /// The Burst-compiled solver must also pass the grid sweep on its own.
    /// </summary>
    [TestInfo("OrbitShadowAccuracy_GridSweepBurst")]
    public void GridSweepBurst() => RunGrid(300, 1234, burst: true);
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
        var failures = new List<string>();
        int total = 0;

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

                var reference = new ShadowReference(bodies, in orbit);
                var term = Compute(bodies, in orbit, UT);
                total += 1;

                // The plane needs to turn by about 30° before the vessel can
                // be shadowed, which is about a twelfth of a Kerbin year.
                var season = reference.NextTransition(UT, 1.2e6);

                if (term.InShadow || double.IsNaN(term.UT) || term.UT < UT)
                    failures.Add($"invalid result {term.UT} (UT {UT}, offset {offset})");
                else if (term.UT > season + Tolerance)
                    failures.Add(
                        $"reported lit until {term.UT}, but the season starts at {season} "
                            + $"(UT {UT}, offset {offset})"
                    );
            }
        }

        AssertNoFailures(failures, total, "polar orbits");
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
        var failures = new List<string>();
        int total = 0;

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

                var reference = new ShadowReference(bodies, in orbit);
                var term = Compute(bodies, in orbit, UT);
                total += 1;

                if (reference.InShadow(UT))
                    continue;

                // At most 40° of sun motion, which is 1/9 of a Kerbin year.
                var season = reference.NextTransition(UT, 1.1e6);

                if (term.InShadow || double.IsNaN(term.UT) || term.UT < UT)
                    failures.Add($"invalid result {term.UT} (UT {UT}, start {start}°)");
                else if (term.UT > season + Tolerance)
                    failures.Add(
                        $"reported lit until {term.UT}, but the first eclipse is at {season}, "
                            + $"{(term.UT - season) / orbit.Period:F1} orbits earlier "
                            + $"(UT {UT}, start {start}°)"
                    );
            }
        }

        AssertNoFailures(failures, total, "eccentric orbits");
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
        var failures = new List<string>();
        int total = 0;

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
                        var result = Check(bodies, in orbit, t, 2.5 * orbit.Period);
                        total += 1;
                        if (result.Outcome == Outcome.Failed)
                            failures.Add(result.Message);
                    }
                }
            }
        }

        AssertNoFailures(failures, total, "grazing passes");
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
        var failures = new List<string>();
        int total = 0;

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
                        var result = Check(bodies, in orbit, t, 1.5 * orbit.Period);
                        total += 1;
                        if (result.Outcome == Outcome.Failed)
                            failures.Add(result.Message);
                    }
                }
            }
        }

        AssertNoFailures(failures, total, "high orbits");
    }
    #endregion

    #region Lag budget
    struct Simulation
    {
        public int Recomputes;
        public int Orbits;
        public List<string> Failures;
    }

    /// <summary>
    /// Simulate the changepoints that a background vessel would run over
    /// <paramref name="duration"/> seconds, using the real validator. Every
    /// result is also checked against the reference, so that an eclipse that
    /// is skipped over entirely is reported as a failure.
    /// </summary>
    static Simulation Simulate(SystemBody[] bodies, in Orbit orbit, double UT, double duration)
    {
        var reference = new ShadowReference(bodies, in orbit);
        var failures = new List<string>();
        var end = UT + duration;
        int recomputes = 0;

        while (UT < end && recomputes < 100_000)
        {
            var term = Compute(bodies, in orbit, UT);
            var state = ShadowState.ValidateShadowState(
                new(term.UT, term.InShadow),
                UT,
                ShadowState.GetRetryInterval(orbit.Period)
            );
            recomputes += 1;

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
            Orbits = (int)Math.Ceiling(duration / orbit.Period),
            Failures = failures,
        };
    }

    /// <summary>
    /// Over a whole Kerbin year, through both eclipse seasons, the number of
    /// shadow recomputations should stay at roughly two per orbit. This turns
    /// the no-lag policy into a test: a fixed <c>t + N</c> retry loop would blow
    /// through the budget for any orbit longer than a few minutes.
    /// </summary>
    [TestInfo("OrbitShadowAccuracy_LagBudget")]
    public void LagBudget()
    {
        var bodies = Create();
        var year = bodies[Kerbin].Orbit.Period;
        var failures = new List<string>();

        (string, Orbit)[] orbits =
        [
            ("polar", MakeOrbitPe(bodies, Kerbin, 2_000_000, 0.0, 90 * Deg, 0.4, 0.0, 0.0)),
            ("polar eccentric", MakeOrbitPe(bodies, Kerbin, 800_000, 0.5, 85 * Deg, 1.3, 2.0, 0.0)),
            ("inclined", MakeOrbitPe(bodies, Kerbin, 5_000_000, 0.1, 40 * Deg, 2.1, 0.7, 0.0)),
            ("munar polar", MakeOrbitPe(bodies, Mun, 400_000, 0.2, 90 * Deg, 0.9, 1.0, 0.0)),
        ];

        foreach (var (name, orbit) in orbits)
        {
            var sim = Simulate(bodies, in orbit, 1.0e5, year);
            failures.AddRange(sim.Failures);

            LogUtil.Log(
                $"[BRP.Test] OrbitShadow lag budget ({name}): {sim.Recomputes} recomputes "
                    + $"over {sim.Orbits} orbits"
            );

            if (sim.Recomputes > 3 * sim.Orbits + 10)
                failures.Add(
                    $"{name}: {sim.Recomputes} recomputes over {sim.Orbits} orbits "
                        + "is more than 3 per orbit"
                );
        }

        AssertNoFailures(failures, orbits.Length, "lag budget simulations");
    }
    #endregion
}
