using System;
using BackgroundResourceProcessing.BurstSolver;
using BackgroundResourceProcessing.Collections.Burst;
using BackgroundResourceProcessing.Mathematics;
using BackgroundResourceProcessing.Maths;
using BackgroundResourceProcessing.Utils;
using Unity.Burst;
using static BackgroundResourceProcessing.Utils.MathUtil;

namespace BackgroundResourceProcessing.Shadow;

/// <summary>
/// The shadow of a body on a vessel landed on its surface.
/// </summary>
///
/// <remarks>
/// <para>
/// The vessel is in shadow when <c>f(t) = n(t) · (p_planet(t) - p_star(t))</c>
/// is positive, where <c>n(t)</c> is the surface normal rotated forward with
/// the planet's spin. The terminators are the sign changes of <c>f</c>.
/// </para>
///
/// <para>
/// They are found by stepping forward in steps short enough that <c>f</c>
/// has at most one extremum per step (see <see cref="GetDotStep"/>). A sign
/// change between the ends of a step brackets a terminator. If both ends have
/// the same sign but <c>|f|</c> falls at the start of the step and rises at
/// the end, there is a minimum of <c>|f|</c> in between that may cross zero,
/// which catches short days and nights near the polar circles. The bracket is
/// then narrowed with Newton's method, falling back to bisection whenever a
/// Newton step leaves it.
/// </para>
///
/// <para>
/// Inside a polar circle there are no terminators until the sun moves far
/// enough around the planet's orbit, which can take a large fraction of an
/// orbit. The time at which the vessel leaves the polar circle is found in
/// the same way first, and the search for terminators starts from there.
/// </para>
///
/// <para>
/// If no terminator is found within <see cref="MaxScanSteps"/> steps, the
/// state is still known up to the end of the search, so that is returned as
/// the time at which to compute the state again.
/// </para>
/// </remarks>
internal readonly struct LandedShadow
{
    /// <summary>
    /// The most steps to take when searching for one sign change.
    /// </summary>
    const int MaxScanSteps = 64;

    /// <summary>
    /// The most iterations to take when narrowing a bracket. Each iteration
    /// at least halves it, so this gets from any step down to
    /// <see cref="RootTolerance"/>.
    /// </summary>
    const int MaxRefineIter = 64;

    /// <summary>
    /// Terminators are found to within this many seconds.
    /// </summary>
    const double RootTolerance = 1e-3;

    /// <summary>
    /// The fraction of a half-turn of the sun across the sky to cover with
    /// each step. Extrema of <c>f</c> are at least a half-turn apart, so this
    /// leaves at most one per step.
    /// </summary>
    const double StepFraction = 0.25;

    readonly SolarSystem system;
    readonly SystemBody planet;
    readonly int referenceIndex;
    readonly int planetIndex;
    readonly int starIndex;

    readonly Vector3d normal;
    readonly double cosLatitude;
    readonly double currentUT;

    bool PlanetIsReference => planetIndex == referenceIndex;
    bool StarIsAncestor => system[referenceIndex].Orbit.ParentBodyIndex == starIndex;
    bool StarIsPlanet => starIndex == planetIndex;

    LandedShadow(
        SolarSystem system,
        int planetIndex,
        int starIndex,
        int referenceIndex,
        Vector3d normal,
        double cosLatitude,
        double currentUT
    )
    {
        this.system = system;
        this.planetIndex = planetIndex;
        this.starIndex = starIndex;
        this.referenceIndex = referenceIndex;
        this.planet = system[planetIndex];
        this.normal = normal;
        this.cosLatitude = cosLatitude;
        this.currentUT = currentUT;
    }

    #region Burst Entry Point
    unsafe delegate void ComputeLandedTerminatorDelegate(
        SolarSystem* system,
        int planetIndex,
        int starIndex,
        int referenceIndex,
        Vector3d* normal,
        double cosLatitude,
        double currentUT,
        out OrbitShadow.Terminator terminator
    );

    static ComputeLandedTerminatorDelegate ComputeLandedTerminatorFp = null;

    internal static unsafe OrbitShadow.Terminator ComputeLandedTerminator(
        SystemBody[] bodies,
        int planetIndex,
        int starIndex,
        int referenceIndex,
        Vector3d normal,
        double cosLatitude,
        double currentUT
    )
    {
        BurstCrashHandler.Init();

        fixed (SystemBody* ptr = bodies)
        {
            var system = new SolarSystem(new MemorySpan<SystemBody>(ptr, bodies.Length));

            OrbitShadow.Terminator term;
            if (!BurstUtil.EnableBurst)
            {
                term = ComputeLandedTerminator(
                    system,
                    planetIndex,
                    starIndex,
                    referenceIndex,
                    normal,
                    cosLatitude,
                    currentUT
                );
            }
            else
            {
                ComputeLandedTerminatorFp ??= BurstCompiler
                    .CompileFunctionPointer<ComputeLandedTerminatorDelegate>(
                        LandedShadowBurst.ComputeLandedTerminatorBurst
                    )
                    .Invoke;

                ComputeLandedTerminatorFp(
                    &system,
                    planetIndex,
                    starIndex,
                    referenceIndex,
                    &normal,
                    cosLatitude,
                    currentUT,
                    out term
                );
            }

            if (term.UT == double.MaxValue)
                term.UT = double.PositiveInfinity;
            if (term.NextUT == double.MaxValue)
                term.NextUT = double.PositiveInfinity;

            return term;
        }
    }
    #endregion

    #region Core Computation
    /// <summary>
    /// Compute the next terminator after <paramref name="currentUT"/>, along
    /// with the one after that, for the shadows of the planet itself and of
    /// the bodies that it orbits (see <see cref="AncestorShadow"/>).
    /// </summary>
    internal static OrbitShadow.Terminator ComputeLandedTerminator(
        SolarSystem system,
        int planetIndex,
        int starIndex,
        int referenceIndex,
        Vector3d normal,
        double cosLatitude,
        double currentUT
    )
    {
        var shadow = new LandedShadow(
            system,
            planetIndex,
            starIndex,
            referenceIndex,
            normal,
            cosLatitude,
            currentUT
        );

        var term = shadow.ComputeTerminator();
        if (term.Failed)
            return term;

        var vessel = AncestorShadow.Source.Landed(system, planetIndex, normal, currentUT);
        return AncestorShadow.Combine(system, in vessel, starIndex, currentUT, term);
    }

    OrbitShadow.Terminator ComputeTerminator()
    {
        // Landed on the star itself -- not in shadow.
        if (StarIsPlanet)
            return OrbitShadow.Terminator.Sun(double.MaxValue);

        var dot = Evaluate(Function.Dot, currentUT);
        if (!IsFinite(dot.x) || !IsFinite(dot.dx))
            return OrbitShadow.Terminator.Failure();

        bool inshadow = dot.x > 0;

        // Planet is tidally locked and is orbiting the star -- no future
        // changes. A planet that doesn't rotate at all still sees the sun
        // move across the sky once per orbit, so it isn't included here.
        if (PlanetIsReference && StarIsAncestor && planet.TidallyLocked)
            return State(inshadow, double.MaxValue);

        var sunRate = GetSunAngularRate();
        var start = currentUT;

        var polar = Evaluate(Function.Polar, currentUT);
        if (polar.x > 0)
        {
            // We are currently in the polar circle and there won't be a
            // terminator until the planet proceeds around its orbit.
            if (!(sunRate > 0))
                return State(inshadow, double.MaxValue);

            var polarStep = StepFraction * Math.PI / sunRate;
            if (!FindCrossing(Function.Polar, currentUT, polarStep, true, out start))
            {
                if (!(start > currentUT))
                    return OrbitShadow.Terminator.Failure();

                // The state stays the same while in the polar circle.
                return State(inshadow, start);
            }

            // The state can't change inside the polar circle. If it seems to
            // have, the exit is too close to call, so compute it again then.
            if ((Evaluate(Function.Dot, start).x > 0) != inshadow)
                return State(inshadow, start);
        }

        var step = GetDotStep(sunRate);
        if (!(step < double.PositiveInfinity))
            return State(inshadow, double.MaxValue);

        if (!FindCrossing(Function.Dot, start, step, inshadow, out var terminatorUT))
        {
            if (!(terminatorUT > currentUT))
                return OrbitShadow.Terminator.Failure();

            return State(inshadow, terminatorUT);
        }

        var term = State(inshadow, terminatorUT);

        // The search continues from the far side of the terminator, so the
        // second one is the end of the day or night that the first one starts.
        // If it isn't found, the state is still known up to where the search
        // got to.
        FindCrossing(Function.Dot, terminatorUT, step, !inshadow, out var nextUT);
        if (nextUT > terminatorUT)
        {
            term.HasNext = true;
            term.NextInShadow = !inshadow;
            term.NextUT = nextUT;
        }

        return term;
    }

    static OrbitShadow.Terminator State(bool inshadow, double UT) =>
        inshadow ? OrbitShadow.Terminator.Shadow(UT) : OrbitShadow.Terminator.Sun(UT);

    /// <summary>
    /// The step to take when searching for terminators, so that there is at
    /// most one extremum of <c>f</c> per step.
    /// </summary>
    ///
    /// <remarks>
    /// The surface normal turns at the planet's spin rate and the sun
    /// direction turns at <paramref name="sunRate"/>, so the angle between
    /// them changes no faster than the sum of the two. That covers both
    /// prograde and retrograde spins, whatever the length of the solar day.
    /// </remarks>
    double GetDotStep(double sunRate)
    {
        var spin = planet.Rotates ? Math.Abs(planet.AngularVelocity) : 0.0;
        var rate = spin + sunRate;
        if (!(rate > 0) || !IsFinite(rate))
            return double.PositiveInfinity;

        return StepFraction * Math.PI / rate;
    }

    /// <summary>
    /// An upper bound on how fast the direction to the star turns, in radians
    /// per second.
    /// </summary>
    ///
    /// <remarks>
    /// This is mostly due to the reference body's orbit around the star. The
    /// fastest that it turns is at periapsis. The current rate is included
    /// too, with some margin, for systems where the star itself is moving.
    /// </remarks>
    double GetSunAngularRate()
    {
        var sun = GetSunVectorAtUT(Dual.Variable(currentUT));
        var sqr = sun.v.sqrMagnitude;
        var rate = 2.0 * Vector3d.Cross(sun.v, sun.dv).magnitude / sqr;

        ref var reference = ref system[referenceIndex];
        if (reference.HasOrbit && reference.Orbit.Eccentricity < 1.0)
        {
            ref var orbit = ref reference.Orbit;
            var peR = orbit.PeR;
            var speed = Math.Sqrt(orbit.KinematicGravParameter * (1.0 + orbit.Eccentricity) / peR);
            var periapsisRate = speed / peR;
            if (IsFinite(periapsisRate))
                rate = Math.Max(rate, periapsisRate);
        }

        return IsFinite(rate) ? rate : 0.0;
    }
    #endregion

    #region Root Finding
    enum Function
    {
        /// <summary>
        /// <see cref="ComputeDotAt"/>, which is <c>&gt; 0</c> in shadow.
        /// </summary>
        Dot,

        /// <summary>
        /// <see cref="GetRelativeInclinationAngle"/>, which is <c>&gt; 0</c>
        /// in the polar circle.
        /// </summary>
        Polar,
    }

    Dual Evaluate(Function function, double UT)
    {
        var x = Dual.Variable(UT);
        return function == Function.Dot ? ComputeDotAt(x) : GetRelativeInclinationAngle(x);
    }

    /// <summary>
    /// Find the first time after <paramref name="lo"/> at which
    /// <paramref name="function"/> stops being positive (if
    /// <paramref name="positive"/>) or becomes positive (otherwise).
    /// </summary>
    ///
    /// <param name="step">
    /// The step to take. There must be at most one extremum of the function
    /// in any interval this long.
    /// </param>
    /// <param name="positive">Whether the function is positive at
    /// <paramref name="lo"/>.</param>
    /// <param name="UT">
    /// The time of the sign change, on the side of it that has the new sign.
    /// If there is none within <see cref="MaxScanSteps"/> steps, this is
    /// instead the time up to which the sign is known not to change.
    /// </param>
    ///
    /// <returns>Whether a sign change was found.</returns>
    bool FindCrossing(Function function, double lo, double step, bool positive, out double UT)
    {
        UT = lo;

        var sign = positive ? 1.0 : -1.0;
        var a = lo;
        var fa = Evaluate(function, a);
        if (!IsFinite(fa.x) || !IsFinite(fa.dx))
            return false;

        for (int i = 0; i < MaxScanSteps; ++i)
        {
            var b = a + step;
            var fb = Evaluate(function, b);
            if (!IsFinite(fb.x) || !IsFinite(fb.dx))
                return false;

            if ((fb.x > 0) != positive)
            {
                UT = Refine(function, a, fa, b, positive);
                return true;
            }

            // Both ends have the same sign, but if |f| falls at the start and
            // rises at the end then it has a minimum in between, which may
            // cross zero.
            if (
                sign * fa.dx < 0
                && sign * fb.dx > 0
                && FindDip(function, a, b, positive, out var c)
            )
            {
                UT = Refine(function, a, fa, c, positive);
                return true;
            }

            a = b;
            fa = fb;
            UT = a;
        }

        return false;
    }

    /// <summary>
    /// Look for a point between <paramref name="lo"/> and
    /// <paramref name="hi"/> where the function has the opposite sign to the
    /// ends, given that <c>|f|</c> has a single minimum in between.
    /// </summary>
    bool FindDip(Function function, double lo, double hi, bool positive, out double UT)
    {
        var sign = positive ? 1.0 : -1.0;

        for (int i = 0; i < MaxRefineIter && hi - lo > RootTolerance; ++i)
        {
            var mid = 0.5 * (lo + hi);
            var f = Evaluate(function, mid);
            if (!IsFinite(f.x) || !IsFinite(f.dx))
                break;

            if ((f.x > 0) != positive)
            {
                UT = mid;
                return true;
            }

            // Move towards the minimum of |f|.
            if (sign * f.dx < 0)
                lo = mid;
            else
                hi = mid;
        }

        UT = double.NaN;
        return false;
    }

    /// <summary>
    /// Narrow the bracket <c>[lo, hi]</c> around a sign change, where the
    /// function has the sign <paramref name="positive"/> at
    /// <paramref name="lo"/> and the opposite one at <paramref name="hi"/>.
    /// </summary>
    ///
    /// <returns>
    /// The end of the final bracket that has the new sign, so that evaluating
    /// the state at the returned time gives the new state.
    /// </returns>
    double Refine(Function function, double lo, Dual flo, double hi, bool positive)
    {
        // Newton's method from the start of the bracket, falling back to
        // bisection whenever a step lands outside of it. Points too close to
        // either end are pulled in, so that the bracket keeps shrinking once
        // Newton's method has converged on one side of the root.
        var x = lo - flo.x / flo.dx;
        for (int i = 0; i < MaxRefineIter && hi - lo > RootTolerance; ++i)
        {
            if (!(x > lo && x < hi))
                x = 0.5 * (lo + hi);
            x = Clamp(x, lo + 0.25 * RootTolerance, hi - 0.25 * RootTolerance);

            var f = Evaluate(function, x);
            if ((f.x > 0) == positive)
                lo = x;
            else
                hi = x;

            x -= f.x / f.dx;
        }

        // The state at the result is what the next computation starts from,
        // so make sure that it agrees, moving further into the new state if
        // necessary.
        for (int i = 0; i < 4 && (Evaluate(function, hi).x > 0) == positive; ++i)
            hi += RootTolerance;

        return hi;
    }
    #endregion

    #region Dot Product & Sun Vector
    /// <summary>
    /// The cosine of the angle between the sun and the rotation axis, minus
    /// the cosine of the latitude. This is positive when the latitude is
    /// inside the polar circle, where the sun never crosses the horizon.
    /// </summary>
    Dual GetRelativeInclinationAngle(Dual UT)
    {
        // Normalize the sun vector so we solve cos(angle) - cosLatitude == 0
        // rather than |sun| * cos(angle) - cosLatitude == 0. |sun| is the
        // planet-star distance (~1e10 m), which would otherwise dominate the
        // equation.
        var sun = GetSunVectorAtUT(UT).Normalized();
        var cos = Dual.Abs(DualVector3.Dot(sun, new(planet.RotationAxis)));
        return cos - cosLatitude;
    }

    Dual ComputeDotAt(Dual UT)
    {
        var pUT = UT - currentUT;
        // Rotate the surface normal forward in time with the planet's spin.
        var theta = planet.AngularVelocity * pUT;
        if (!planet.Rotates)
            theta = new(0);
        var q = DualQuaternion.FromAngleAxis(theta, new(planet.RotationAxis));

        var sun = GetSunVectorAtUT(UT);
        var normal = q.Rotate(new(this.normal));

        return DualVector3.Dot(sun, normal);
    }

    DualVector3 GetSunVectorAtUT(Dual UT)
    {
        var ppos = system.GetPositionAtUT(in planet, UT);
        var spos = system.GetPositionAtUT(in system[starIndex], UT);
        return ppos - spos;
    }
    #endregion
}

[BurstCompile]
internal static class LandedShadowBurst
{
    // This needs to be in a class or else we get a segfault somewhere within
    // mono when calling the burst function pointer.
    [BurstCompile]
    internal static unsafe void ComputeLandedTerminatorBurst(
        SolarSystem* system,
        int planetIndex,
        int starIndex,
        int referenceIndex,
        Vector3d* normal,
        double cosLatitude,
        double currentUT,
        out OrbitShadow.Terminator terminator
    )
    {
        terminator = LandedShadow.ComputeLandedTerminator(
            *system,
            planetIndex,
            starIndex,
            referenceIndex,
            *normal,
            cosLatitude,
            currentUT
        );
    }
}
