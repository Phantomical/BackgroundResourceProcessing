using System;
using KSP.Testing;

namespace BackgroundResourceProcessing.Test.Mathematics;

public sealed class LandedShadowTests : BRPTestBase
{
    // Points within this much of the terminator (|lit-dot|) are skipped so that
    // floating point noise right at sunrise/sunset cannot make the test flaky.
    const double TerminatorMargin = 0.1;

    /// <summary>
    /// A vessel landed on the sunlit side of a body must be reported as lit, and
    /// one on the night side must be reported as in shadow.
    /// </summary>
    ///
    /// <remarks>
    /// Regression test for issue #26. The landed shadow code dotted a surface
    /// normal (Unity world space, from <see cref="CelestialBody.GetSurfaceNVector"/>)
    /// against orbital sun positions reconstructed in KSP's internal orbit frame.
    /// The frame mismatch inverted the lit/shadow result, so unloaded solar
    /// vessels would not begin charging after sunrise.
    ///
    /// Ground truth is computed entirely in Unity world space (where
    /// <c>GetSurfaceNVector</c> and <c>CelestialBody.position</c> are both valid),
    /// independently of any BRP math.
    /// </remarks>
    [TestInfo("LandedShadowTests_TestLitStateMatchesGroundTruth")]
    public void TestLitStateMatchesGroundTruth()
    {
        var sun = Planetarium.fetch?.Sun;
        var bodies = FlightGlobals.Bodies;
        if (sun == null || bodies == null || bodies.Count == 0)
        {
            // Celestial bodies aren't loaded (e.g. the test is being run outside
            // of a game scene). There's nothing we can check.
            return;
        }

        double UT = Planetarium.GetUniversalTime();
        int chec_ed = 0;

        foreach (var planet in bodies)
        {
            if (planet.isStar || planet.referenceBody == null)
                continue;

            for (double lat = -45.0; lat <= 45.0; lat += 45.0)
            {
                for (double lon = 0.0; lon < 360.0; lon += 45.0)
                {
                    Vector3d normal = planet.GetSurfaceNVector(lat, lon);

                    // Vector from the star to the planet, i.e. pointing away from
                    // the sun. The surface is lit when the outward normal points
                    // the other way (dot < 0), matching the InShadow = dot > 0
                    // convention used by the landed shadow code.
                    Vector3d antiSun = planet.position - sun.position;
                    double dot = Vector3d.Dot(normal.normalized, antiSun.normalized);

                    if (Math.Abs(dot) < TerminatorMargin)
                        continue;

                    bool expectedInShadow = dot > 0.0;
                    var state = ShadowState.ComputeLandedShadowState(planet, sun, lat, lon, UT);

                    Assert.AreEqual(
                        expectedInShadow,
                        state.InShadow,
                        $"{planet.bodyName} @ lat={lat} lon={lon}: "
                            + $"BRP reported InShadow={state.InShadow} but the surface is actually "
                            + $"{(expectedInShadow ? "in shadow" : "lit")} (lit-dot={dot:F3})"
                    );

                    chec_ed += 1;
                }
            }
        }

        Assert.IsTrue(
            chec_ed > 0,
            "No landed shadow points were checked; celestial bodies may not be loaded."
        );
    }

    /// <summary>
    /// The predicted time of the next terminator for a landed vessel must match
    /// the time the surface actually crosses into/out of sunlight.
    /// </summary>
    ///
    /// <remarks>
    /// Regression test for the landed shadow rotation-direction bug.
    /// <c>LandedShadow.ComputeDotAt</c> spun the surface normal about the body's
    /// rotation axis in the wrong direction (KSP's true angular velocity points
    /// along <c>-RotationAxis</c>), so the lit/shadow state at the current
    /// instant could be correct while the predicted terminator landed on the
    /// wrong side of the day, desynchronising the day/night cycle.
    ///
    /// Ground truth is produced by walking the body's true rotation forward in
    /// time using KSP's own rotation formulas (<see cref="Planetarium.CelestialFrame.PlanetaryFrame"/>
    /// plus <see cref="CelestialBody.getTruePositionAtUT"/>) and finding when the
    /// real surface normal crosses the terminator.
    /// </remarks>
    [TestInfo("LandedShadowTests_TestTerminatorMatchesGroundTruth")]
    public void TestTerminatorMatchesGroundTruth()
    {
        var sun = Planetarium.fetch?.Sun;
        var bodies = FlightGlobals.Bodies;
        if (sun == null || bodies == null || bodies.Count == 0)
            return;

        double now = Planetarium.GetUniversalTime();
        const double lat = 12.0;
        const double lon = 47.0;
        int chec_ed = 0;

        foreach (var planet in bodies)
        {
            if (planet.isStar || planet.referenceBody == null || !planet.rotates)
                continue;
            if (planet.rotationPeriod == 0.0)
                continue;

            // Skip points sitting right on the terminator; the predicted crossing
            // is ill-conditioned there and the assertion would be noisy.
            if (Math.Abs(TruthLitDot(planet, sun, lat, lon, now)) < TerminatorMargin)
                continue;

            // We only need to search a little over one rotation to be guaranteed
            // a terminator (away from the poles).
            double span = planet.rotationPeriod * 1.5;
            double truthUT = FindTruthTerminator(planet, sun, lat, lon, now, span);
            if (truthUT < 0.0)
                continue;

            var state = ShadowState.ComputeLandedShadowState(planet, sun, lat, lon, now);
            double brpUT = state.NextTerminatorEstimate;
            if (double.IsInfinity(brpUT) || double.IsNaN(brpUT))
                continue;

            // The terminator can legitimately be far in the future for slow
            // rotators, so allow a small relative slack on top of an absolute
            // floor. The bug shifts the terminator by a large fraction of a day,
            // far outside this tolerance.
            double tolerance = Math.Max(120.0, 0.01 * (truthUT - now));

            Assert.IsTrue(
                Math.Abs(brpUT - truthUT) <= tolerance,
                $"{planet.bodyName} @ lat={lat} lon={lon}: BRP predicts the next "
                    + $"terminator at UT {brpUT:F1} (+{brpUT - now:F0}s) but it actually "
                    + $"occurs at UT {truthUT:F1} (+{truthUT - now:F0}s), "
                    + $"a difference of {brpUT - truthUT:F0}s."
            );

            chec_ed += 1;
        }

        Assert.IsTrue(
            chec_ed > 0,
            "No landed terminators were checked; celestial bodies may not be loaded."
        );
    }

    /// <summary>
    /// Reconstruct the real lit-dot (outward surface normal vs direction to the
    /// sun) at an arbitrary UT using KSP's own rotation model. Positive means
    /// lit. This is computed entirely in Unity world space and does not depend on
    /// any BRP math.
    /// </summary>
    private static double TruthLitDot(
        CelestialBody body,
        CelestialBody sun,
        double lat,
        double lon,
        double UT
    )
    {
        double rotAngle = (body.initialRotation + 360.0 * body.rotPeriodRecip * UT) % 360.0;
        double directRot = (rotAngle - Planetarium.InverseRotAngle) % 360.0;

        Planetarium.CelestialFrame frame = default;
        Planetarium.CelestialFrame.PlanetaryFrame(0.0, 90.0, directRot, ref frame);

        Vector3d local = Planetarium.SphericalVector(lat * Math.PI / 180.0, lon * Math.PI / 180.0);
        Vector3d normal = frame.LocalToWorld(local).xzy;
        Vector3d toSun = sun.getTruePositionAtUT(UT) - body.getTruePositionAtUT(UT);

        return Vector3d.Dot(normal.normalized, toSun.normalized);
    }

    /// <summary>
    /// Find the UT of the next terminator crossing after <paramref name="now"/>,
    /// or a negative value if none is found within <paramref name="span"/>.
    /// </summary>
    private static double FindTruthTerminator(
        CelestialBody body,
        CelestialBody sun,
        double lat,
        double lon,
        double now,
        double span
    )
    {
        const int Steps = 2000;
        double step = span / Steps;
        double prev = TruthLitDot(body, sun, lat, lon, now);

        for (double t = now + step; t <= now + span; t += step)
        {
            double cur = TruthLitDot(body, sun, lat, lon, t);
            if ((prev > 0.0) != (cur > 0.0))
            {
                double lo = t - step;
                double hi = t;
                for (int i = 0; i < 48; ++i)
                {
                    double mid = 0.5 * (lo + hi);
                    bool loLit = TruthLitDot(body, sun, lat, lon, lo) > 0.0;
                    bool midLit = TruthLitDot(body, sun, lat, lon, mid) > 0.0;
                    if (loLit == midLit)
                        lo = mid;
                    else
                        hi = mid;
                }
                return 0.5 * (lo + hi);
            }
            prev = cur;
        }

        return -1.0;
    }
}
