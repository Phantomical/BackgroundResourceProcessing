using System;
using BackgroundResourceProcessing.Mathematics;
using BackgroundResourceProcessing.Maths;
using KSP.Testing;
using BRPOrbit = BackgroundResourceProcessing.Mathematics.Orbit;

namespace BackgroundResourceProcessing.Test.Mathematics;

/// <summary>
/// Regression tests for the retrograde/slow-rotation handling of the landed
/// shadow solver (finding D4 of the 2026-07-17 broad codebase review).
/// </summary>
///
/// <remarks>
/// <para>
/// KSP encodes a body's spin direction purely in the sign of
/// <c>rotationPeriod</c> (<c>CelestialBody.RotationAxis</c> is always
/// <c>Vector3d.up</c> and <c>angularV</c> is an unsigned magnitude), and
/// retrograde or slow rotators also produce a negative
/// <c>solarDayLength</c>. <see cref="SystemBody"/> must record the signed
/// angular velocity, and <see cref="LandedShadow"/> must not use the raw
/// (possibly negative) rotation period / solar day length for its step sizes
/// and convergence guards: <c>advance = 0.25 * min(RotationPeriod, Orbit.Period)</c>
/// walks Newton backwards in time, and a negative <c>SolarDayLength / 4</c>
/// bound makes every guard trivially true, so the solver bails out with a
/// terminator in the past instead of the actual crossing.
/// </para>
///
/// <para>
/// The scenarios are entirely synthetic — a single equatorial planet on a
/// circular orbit around a fixed star — so they do not require a loaded game
/// scene. <see cref="SystemBody"/> fields are populated with the signed values a
/// retrograde <c>CelestialBody</c> produces. Ground truth is computed
/// independently of the solver by rotating the surface normal with the same
/// signed spin and bisecting for the lit/shadow crossing. (Real retrograde
/// bodies — e.g. OPM's Urlum system — are additionally covered in-game by
/// <see cref="LandedShadowTests"/>' ground-truth sweeps.)
/// </para>
/// </remarks>
public sealed class LandedShadowRetrogradeTests : BRPTestBase
{
    const double CurrentUT = 1000.0;
    const double Radius = 1.0e10; // planet-star distance (m)

    // Phase of the landed point ahead of the anti-star direction at CurrentUT.
    // Non-zero so the test does not start exactly at midnight (an extremum of
    // the lit-dot, where the solver's branch choice is ill-conditioned).
    const double PhaseRad = 0.7;

    static SystemBody[] BuildSystem(double orbitPeriod, double rotationPeriod)
    {
        double n = 2.0 * Math.PI / orbitPeriod;
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
            Period = orbitPeriod,
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

        // Mirror what SystemBody(CelestialBody) records for a body with a signed
        // rotationPeriod: the spin axis is the orbit normal, the angular velocity
        // carries the spin direction, and solarDayLength is KSP's
        // orbit.period * rotationPeriod / (orbit.period - rotationPeriod) — which
        // is negative for both scenarios below.
        var planet = new SystemBody
        {
            HasOrbit = true,
            Orbit = orbit,
            Index = 1,
            Radius = 6.0e5,
            RotationAxis = new Vector3d(0, 0, 1),
            AngularVelocity = 2.0 * Math.PI / rotationPeriod,
            Rotates = true,
            TidallyLocked = false,
            RotationPeriod = rotationPeriod,
            SolarDayLength = orbitPeriod * rotationPeriod / (orbitPeriod - rotationPeriod),
        };

        return [star, planet];
    }

    /// <summary>
    /// The surface normal of the landed point at CurrentUT: an equatorial point
    /// rotated <see cref="PhaseRad"/> ahead of the anti-star direction, so the
    /// point starts on the night side partway between midnight and a terminator.
    /// </summary>
    static Vector3d Normal()
    {
        return new Vector3d(Math.Cos(PhaseRad), Math.Sin(PhaseRad), 0.0);
    }

    /// <summary>
    /// The true lit-dot at an arbitrary UT, computed independently of the solver.
    /// Rotates the surface normal forward with the planet's signed spin (matching
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

    /// <summary>
    /// Find the UT of the first lit/shadow crossing after <paramref name="now"/>
    /// by sampling and bisecting the true lit-dot. Returns a negative value if no
    /// crossing occurs within <paramref name="span"/>.
    /// </summary>
    static double FindTruthTerminator(SystemBody[] bodies, Vector3d normal, double now, double span)
    {
        const int Steps = 4000;
        double step = span / Steps;
        double prev = LitDot(bodies, normal, now);

        for (double t = now + step; t <= now + span; t += step)
        {
            double cur = LitDot(bodies, normal, t);
            if ((prev > 0.0) != (cur > 0.0))
            {
                double lo = t - step;
                double hi = t;
                for (int i = 0; i < 64; ++i)
                {
                    double mid = 0.5 * (lo + hi);
                    bool loShadow = LitDot(bodies, normal, lo) > 0.0;
                    bool midShadow = LitDot(bodies, normal, mid) > 0.0;
                    if (loShadow == midShadow)
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

    void CheckTerminator(double orbitPeriod, double rotationPeriod, string label)
    {
        var savedBurst = DebugSettings.Instance.EnableBurst;
        // Exercise the managed math path directly (no Burst compilation needed).
        DebugSettings.Instance.EnableBurst = false;
        try
        {
            var bodies = BuildSystem(orbitPeriod, rotationPeriod);
            var normal = Normal();
            double solarDay = Math.Abs(bodies[1].SolarDayLength);

            // Sanity check: the landed point starts in night, away from both the
            // terminator and midnight.
            double startDot = LitDot(bodies, normal, CurrentUT);
            Assert.IsTrue(
                startDot > 0.5 && startDot < 0.9,
                $"{label}: test setup error: expected the landed point to start "
                    + $"partway into the night side, lit-dot was {startDot:F3}"
            );

            double truthUT = FindTruthTerminator(bodies, normal, CurrentUT, 1.5 * solarDay);
            Assert.IsTrue(
                truthUT > CurrentUT,
                $"{label}: test setup error: no ground-truth terminator found "
                    + $"within 1.5 solar days"
            );

            var term = LandedShadow.ComputeLandedTerminator(
                bodies,
                planetIndex: 1,
                starIndex: 0,
                referenceIndex: 1,
                normal,
                cosLatitude: 1.0, // equatorial point
                CurrentUT
            );

            Assert.IsTrue(
                term.InShadow,
                $"{label}: the landed point starts in shadow (lit-dot={startDot:F3}) "
                    + $"but the solver reported it lit"
            );
            Assert.IsTrue(
                !double.IsNaN(term.UT) && !double.IsInfinity(term.UT),
                $"{label}: terminator UT must be finite, was {term.UT}"
            );
            Assert.IsTrue(
                term.UT > CurrentUT,
                $"{label}: predicted terminator at UT {term.UT:F1} is in the past "
                    + $"(current UT {CurrentUT}); the solver walked backwards"
            );
            Assert.IsTrue(
                Math.Abs(term.UT - truthUT) <= Math.Max(60.0, 1e-4 * (truthUT - CurrentUT)),
                $"{label}: predicted terminator at UT {term.UT:F1} "
                    + $"(+{term.UT - CurrentUT:F0}s) but the true crossing is at "
                    + $"UT {truthUT:F1} (+{truthUT - CurrentUT:F0}s), "
                    + $"a difference of {term.UT - truthUT:F0}s"
            );
        }
        finally
        {
            DebugSettings.Instance.EnableBurst = savedBurst;
        }
    }

    /// <summary>
    /// A retrograde rotator (rotationPeriod &lt; 0, |rotation| much shorter than
    /// the orbit): the spin carries the landed point the "wrong way" around, and
    /// the solar day length is negative.
    /// </summary>
    [TestInfo("LandedShadowRetrogradeTests_TestRetrogradeRotatorTerminator")]
    public void TestRetrogradeRotatorTerminator()
    {
        CheckTerminator(orbitPeriod: 1.0e7, rotationPeriod: -1.0e5, label: "retrograde");
    }

    /// <summary>
    /// A slow prograde rotator (rotationPeriod &gt; orbit period): the sun
    /// effectively moves backwards across the sky, so KSP's solarDayLength is
    /// negative even though the spin itself is prograde.
    /// </summary>
    [TestInfo("LandedShadowRetrogradeTests_TestSlowRotatorNegativeSolarDay")]
    public void TestSlowRotatorNegativeSolarDay()
    {
        CheckTerminator(orbitPeriod: 1.0e7, rotationPeriod: 2.0e7, label: "slow rotator");
    }
}
