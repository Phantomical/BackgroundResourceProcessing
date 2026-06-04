using System;
using BackgroundResourceProcessing.Mathematics;
using BackgroundResourceProcessing.Maths;
using KSP.Testing;
using BRPOrbit = BackgroundResourceProcessing.Mathematics.Orbit;

namespace BackgroundResourceProcessing.Test.Mathematics;

/// <summary>
/// Regression test for the polar-region (inclination-exit) branch of the landed
/// shadow solver.
/// </summary>
///
/// <remarks>
/// <para>
/// <see cref="LandedShadow.FindInclinationTerminator"/> and
/// <see cref="LandedShadow.FindInclinationMaximum"/> must normalize the sun
/// vector before dotting it against the rotation axis. Without that they solve
/// <c>|sun| * cos(angle) - cosLatitude == 0</c> (with <c>|sun|</c> the
/// planet-star distance, ~1e10 m) instead of <c>cos(angle) - cosLatitude == 0</c>,
/// placing the polar-circle exit where the sun is nearly perpendicular to the
/// spin axis rather than at the true latitude boundary. That throws the predicted
/// terminator off by a large fraction of an orbit.
/// </para>
///
/// <para>
/// The scenario is entirely synthetic — a single tilted, rotating planet orbiting
/// a fixed star — so it does not require a loaded game scene. Ground truth is
/// produced independently of the solver by rotating the surface normal forward
/// with the planet's spin (using the same <see cref="QuaternionD.FromAngleAxis"/>
/// convention as <c>ComputeDotAt</c>) and dotting against the sun vector.
/// </para>
/// </remarks>
public sealed class LandedShadowPolarTests : BRPTestBase
{
    const double Period = 1.0e7; // planet orbital period (s)
    const double Radius = 1.0e10; // planet-star distance (m)
    const double RotationPeriod = 1.0e5; // planet sidereal day (s)
    const double CurrentUT = 1000.0;

    // Tilt of the spin axis away from the orbit normal, and the landed latitude.
    // With these values the landed point starts deep inside the polar circle
    // (cosLatitude <= |dot(sunUnit, axis)| = sin(tilt)) and the latitude exits the
    // polar circle roughly a fifth of an orbit later.
    const double TiltDeg = 55.0;
    const double LatDeg = 75.0;

    static Vector3d Axis()
    {
        double b = TiltDeg * Math.PI / 180.0;
        return new Vector3d(Math.Sin(b), 0.0, Math.Cos(b));
    }

    static Vector3d Normal()
    {
        // Surface normal at geographic latitude LatDeg, longitude 0, expressed in
        // the orbit frame. Built as sin(lat)*axis + cos(lat)*e1 with e1 a unit
        // vector perpendicular to the axis; for this axis it simplifies to
        // (cos(lat - tilt), 0, sin(lat - tilt)).
        double d = (LatDeg - TiltDeg) * Math.PI / 180.0;
        return new Vector3d(Math.Cos(d), 0.0, Math.Sin(d));
    }

    static SystemBody[] BuildSystem()
    {
        double n = 2.0 * Math.PI / Period;
        var orbit = new BRPOrbit
        {
            OrbitFrame = new()
            {
                X = new(1, 0, 0),
                Y = new(0, 1, 0),
                Z = new(0, 0, 1),
            },
            Eccentricity = 0.0,
            Inclination = 0.0,
            SemiMajorAxis = Radius,
            ArgumentOfPeriapsis = 0.0,
            LAN = 0.0,
            Epoch = CurrentUT,
            ObTAtEpoch = 0.0,
            MeanMotion = n,
            MeanAnomalyAtEpoch = 0.0,
            // GM consistent with a circular orbit (n = sqrt(GM / a^3)) so the
            // autodiff velocity/acceleration the solver differentiates are correct.
            GravParameter = n * n * Radius * Radius * Radius,
            Period = Period,
            ParentBodyIndex = 0,
            SemiLatusRectum = Radius,
        };

        var star = new SystemBody
        {
            HasOrbit = false,
            Position = new Vector3d(0, 0, 0),
            Index = 0,
            Radius = 7.0e8,
            Rotates = false,
        };

        var planet = new SystemBody
        {
            HasOrbit = true,
            Orbit = orbit,
            Index = 1,
            Radius = 6.0e5,
            RotationAxis = Axis(),
            AngularVelocity = 2.0 * Math.PI / RotationPeriod,
            Rotates = true,
            TidallyLocked = false,
            RotationPeriod = RotationPeriod,
            SolarDayLength = RotationPeriod * Period / (Period - RotationPeriod),
        };

        return [star, planet];
    }

    /// <summary>
    /// The true lit-dot at an arbitrary UT, computed independently of the solver.
    /// Rotates the surface normal forward with the planet's spin (matching
    /// <c>ComputeDotAt</c> via the identical <see cref="QuaternionD.FromAngleAxis"/>
    /// convention) and dots it against the unit sun vector. Positive => in shadow,
    /// negative => lit.
    /// </summary>
    static double LitDot(SystemBody[] bodies, Vector3d normal, double UT)
    {
        // The star sits at the origin, so the planet's relative position is also
        // the sun vector (planet - star).
        Vector3d sun = bodies[1].GetRelativePositionAtUT(UT);
        double theta = bodies[1].AngularVelocity * (UT - CurrentUT);
        Vector3d rotated = QuaternionD.FromAngleAxis(theta, bodies[1].RotationAxis).Rotate(normal);
        return Vector3d.Dot(sun.normalized, rotated);
    }

    [TestInfo("LandedShadowPolarTests_TestPolarTerminatorIsCorrect")]
    public void TestPolarTerminatorIsCorrect()
    {
        var savedBurst = DebugSettings.Instance.EnableBurst;
        // Exercise the managed math path directly (no Burst compilation needed).
        DebugSettings.Instance.EnableBurst = false;
        try
        {
            var bodies = BuildSystem();
            var normal = Normal();
            double cosLatitude = Math.Cos(LatDeg * Math.PI / 180.0);

            // Sanity check: the landed point really does start inside the polar
            // circle and in night, so the inclination-exit branch is taken.
            Assert.IsTrue(
                LitDot(bodies, normal, CurrentUT) > 0.0,
                "test setup error: landed point should start in polar night"
            );

            var term = LandedShadow.ComputeLandedTerminator(
                bodies,
                planetIndex: 1,
                starIndex: 0,
                referenceIndex: 1,
                normal,
                cosLatitude,
                CurrentUT
            );

            Assert.IsTrue(
                !double.IsNaN(term.UT) && !double.IsInfinity(term.UT),
                $"terminator UT must be finite, was {term.UT}"
            );

            double dt = term.UT - CurrentUT;

            // The polar-circle exit occurs ~0.199 orbits in and the first daily
            // terminator follows within one rotation, so the answer lands at
            // roughly 0.2 orbits. The bug (unnormalized sun vector) pushes the exit
            // out toward 0.25 orbits — where the sun is perpendicular to the spin
            // axis — or produces garbage; both fall outside this window.
            Assert.IsTrue(
                dt > 0.15 * Period && dt < 0.23 * Period,
                $"terminator should land ~0.2 orbits out (within [{0.15 * Period}, "
                    + $"{0.23 * Period}]); was +{dt}s. An unnormalized inclination solver "
                    + $"lands near {0.25 * Period}s or returns garbage."
            );

            // ...and the predicted time must be an actual lit/shadow crossing.
            Assert.IsTrue(
                Math.Abs(LitDot(bodies, normal, term.UT)) < 1e-2,
                $"predicted terminator at UT {term.UT} is not an actual lit/shadow "
                    + $"crossing (lit-dot={LitDot(bodies, normal, term.UT):E3})"
            );
        }
        finally
        {
            DebugSettings.Instance.EnableBurst = savedBurst;
        }
    }
}
