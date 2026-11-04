using System;
using System.Collections.Generic;
using BackgroundResourceProcessing.Mathematics;
using BackgroundResourceProcessing.Maths;
using BackgroundResourceProcessing.Shadow;
using KSP.Testing;

namespace BackgroundResourceProcessing.Test.Shadow;

using static ShadowTestSystem;

/// <summary>
/// Accuracy tests for <see cref="LandedShadow"/>, checked against a
/// brute-force search of the same shadow model.
/// </summary>
///
/// <remarks>
/// <para>
/// A landed vessel is in its planet's shadow when the surface normal points
/// away from the star, and in the shadow of a body that its planet orbits
/// (such as Jool for a vessel on Laythe) when its position on the surface is
/// inside that body's shadow cone. The reference evaluates both directly,
/// steps forward in time, and bisects every sign change.
/// </para>
///
/// <para>
/// The bodies cover a tilted, rotating planet with polar circles (Kerbin),
/// tidally locked moons that are eclipsed by their planet (Mun and Laythe),
/// and a planet that doesn't rotate at all (Moho), for which the sun still
/// moves across the sky once per orbit.
/// </para>
/// </remarks>
public sealed class LandedShadowAccuracyTests : BRPTestBase
{
    const int CasesPerBody = 48;

    /// <summary>
    /// Crossings must match the reference to within this many seconds.
    /// </summary>
    const double Tolerance = 0.1;

    const double Deg = Math.PI / 180.0;

    readonly struct BodyCase(int planet, int reference, int[] occluders, double step, double span)
    {
        public readonly int Planet = planet;
        public readonly int Reference = reference;

        /// <summary>The bodies other than the planet that can shadow it.</summary>
        public readonly int[] Occluders = occluders;

        /// <summary>The step for the reference search, in seconds.</summary>
        public readonly double Step = step;

        /// <summary>How far ahead the reference searches, at most.</summary>
        public readonly double Span = span;
    }

    struct Case
    {
        public BodyCase Body;
        public Vector3d Normal;
        public double CosLatitude;
        public double UT;
        public string Description;
    }

    /// <summary>
    /// The test system, with rotation added to the bodies that the vessel
    /// lands on.
    /// </summary>
    static SystemBody[] CreateRotating()
    {
        var bodies = Create();

        // Kerbin is tilted so that there are polar circles.
        var tilt = 25.0 * Deg;
        SetRotation(ref bodies[Kerbin], 21_549.425, new(0, Math.Sin(tilt), Math.Cos(tilt)), false);
        SetRotation(ref bodies[Mun], bodies[Mun].Orbit.Period, new(0, 0, 1), true);
        SetRotation(ref bodies[Laythe], bodies[Laythe].Orbit.Period, new(0, 0, 1), true);

        // Moho doesn't rotate at all.
        bodies[Moho].Rotates = false;
        bodies[Moho].RotationAxis = new(0, 0, 1);

        return bodies;
    }

    static void SetRotation(ref SystemBody body, double period, Vector3d axis, bool locked)
    {
        body.Rotates = true;
        body.RotationAxis = axis;
        body.RotationPeriod = period;
        body.AngularVelocity = 2.0 * Math.PI / period;
        body.TidallyLocked = locked;
    }

    static BodyCase[] GetBodies(SystemBody[] bodies) =>
        [
            new(Kerbin, Kerbin, [], 5.0, 3.0 * 21_549.425),
            new(Mun, Kerbin, [Kerbin], 5.0, 3.0 * bodies[Mun].Orbit.Period),
            new(Laythe, Jool, [Jool], 5.0, 3.0 * bodies[Laythe].Orbit.Period),
            new(Moho, Moho, [], 60.0, 2.0 * bodies[Moho].Orbit.Period),
        ];

    [TestInfo("LandedShadowAccuracy_MatchesReference")]
    public void MatchesReference()
    {
        var bodies = CreateRotating();
        var rng = new Random(97531);
        var cases = new List<Case>();

        foreach (var body in GetBodies(bodies))
        {
            var axis = bodies[body.Planet].RotationAxis;
            var e1 = Vector3d.Cross(axis, new Vector3d(1, 0, 0)).normalized;
            var e2 = Vector3d.Cross(axis, e1);

            for (int i = 0; i < CasesPerBody; ++i)
            {
                var lat = Math.Asin(2.0 * rng.NextDouble() - 1.0);
                var lon = 2.0 * Math.PI * rng.NextDouble();
                var UT = 1.0e7 * rng.NextDouble();
                var normal =
                    Math.Cos(lat) * (Math.Cos(lon) * e1 + Math.Sin(lon) * e2)
                    + Math.Sin(lat) * axis;

                cases.Add(
                    new Case
                    {
                        Body = body,
                        Normal = normal,
                        CosLatitude = Math.Cos(lat),
                        UT = UT,
                        Description =
                            $"{Info[body.Planet].Name} lat={lat / Deg:F2} lon={lon / Deg:F2} "
                            + $"UT={UT:R}",
                    }
                );
            }
        }

        var results = ParallelCases.Run(cases.Count, i => RunCase(bodies, cases[i]));

        var failures = new List<string>();
        foreach (var result in results)
        {
            if (result is not null)
                failures.Add(result);
        }

        if (failures.Count == 0)
            return;

        var shown = string.Join("\n  ", failures.GetRange(0, Math.Min(failures.Count, 10)));
        throw new Exception(
            $"{failures.Count} failures across {cases.Count} cases. The first few were:\n  {shown}"
        );
    }

    /// <summary>
    /// Check one case, returning a failure message or null. This runs on a
    /// worker thread, so it must not log.
    /// </summary>
    static string RunCase(SystemBody[] bodies, Case c)
    {
        var body = c.Body;
        var term = LandedShadow.ComputeLandedTerminator(
            bodies,
            body.Planet,
            Kerbol,
            body.Reference,
            c.Normal,
            c.CosLatitude,
            c.UT
        );

        if (term.Failed)
            return $"{c.Description}: the solver failed";
        if (!(term.UT > c.UT))
            return $"{c.Description}: terminator at {term.UT:R} is not in the future";

        var horizon = term.HasNext ? term.NextUT : term.UT;
        var end = Math.Min(horizon + 2.0 * body.Step, c.UT + body.Span);
        var transitions = FindTransitions(bodies, c, end, 2);

        // The state right at the start is too close to call.
        if (transitions.Count > 0 && transitions[0] < c.UT + 1.0)
            return null;

        var state = InShadow(bodies, c, c.UT);
        if (state != term.InShadow)
            return $"{c.Description}: solver says InShadow={term.InShadow} but it is {state}";

        var first = transitions.Count > 0 ? transitions[0] : double.PositiveInfinity;
        var second = transitions.Count > 1 ? transitions[1] : double.PositiveInfinity;

        // Past the end of the reference search (deep in a polar night, say)
        // all that can be checked is that nothing was missed before then.
        if (term.UT >= end - body.Step)
        {
            if (first < end - body.Step)
                return $"{c.Description}: missed the transition at {first:R} before {term.UT:R}";
            return null;
        }

        if (!term.HasNext)
        {
            // The terminator is only a time to compute the state again, so
            // the state must not change before then.
            if (first < term.UT - Tolerance)
                return $"{c.Description}: missed the transition at {first:R} before {term.UT:R}";
            return null;
        }

        // The reference can step over a window shorter than its step.
        if (double.IsInfinity(first) && term.NextUT - term.UT < body.Step)
            return null;

        if (!(Math.Abs(first - term.UT) <= Tolerance))
            return $"{c.Description}: terminator at {term.UT:R} but the reference has {first:R} "
                + $"(off by {term.UT - first:F3}s)";

        if (term.NextInShadow == term.InShadow)
            return $"{c.Description}: the state does not change at the terminator";

        if (second < term.NextUT - Tolerance)
            return $"{c.Description}: next terminator at {term.NextUT:R} but the reference has "
                + $"{second:R} (off by {term.NextUT - second:F3}s)";

        return null;
    }

    /// <summary>
    /// Find the first <paramref name="count"/> transitions after the case's
    /// UT and before <paramref name="end"/>.
    /// </summary>
    static List<double> FindTransitions(SystemBody[] bodies, Case c, double end, int count)
    {
        var result = new List<double>();
        var step = c.Body.Step;
        var t = c.UT;
        var state = InShadow(bodies, c, t);

        while (t < end && result.Count < count)
        {
            var next = Math.Min(t + step, end);
            var nextState = InShadow(bodies, c, next);
            if (nextState != state)
            {
                double lo = t,
                    hi = next;
                for (int i = 0; i < 64 && hi - lo > 1e-5; ++i)
                {
                    var mid = 0.5 * (lo + hi);
                    if (InShadow(bodies, c, mid) == state)
                        lo = mid;
                    else
                        hi = mid;
                }

                result.Add(hi);
                state = nextState;
            }

            t = next;
        }

        return result;
    }

    /// <summary>
    /// Whether the vessel is in shadow at <paramref name="UT"/>, evaluated
    /// directly from the body positions.
    /// </summary>
    static bool InShadow(SystemBody[] bodies, Case c, double UT)
    {
        ref var planet = ref bodies[c.Body.Planet];
        var theta = planet.Rotates ? planet.AngularVelocity * (UT - c.UT) : 0.0;
        var normal = QuaternionD.FromAngleAxis(theta, planet.RotationAxis).Rotate(c.Normal);

        var star = GetPosition(bodies, Kerbol, UT);
        var center = GetPosition(bodies, c.Body.Planet, UT);
        if (Vector3d.Dot(normal, center - star) > 0.0)
            return true;

        var point = center + planet.Radius * normal;
        foreach (var occluder in c.Body.Occluders)
        {
            var pos = GetPosition(bodies, occluder, UT);
            if (Margin(point - pos, pos - star, bodies[occluder].Radius) <= 0.0)
                return true;
        }

        return false;
    }

    /// <summary>
    /// The distance from <paramref name="v"/> (relative to the body) to the
    /// edge of the shadow of a body of radius <paramref name="radius"/>,
    /// which is at <paramref name="sunV"/> relative to the star.
    /// </summary>
    static double Margin(Vector3d v, Vector3d sunV, double radius)
    {
        var D = sunV.magnitude;
        var sun = sunV / D;

        var x = Vector3d.Dot(sun, v);
        if (x <= 0.0)
            return v.magnitude - radius;

        return (v - x * sun).magnitude - radius * (1.0 + x / D);
    }
}
