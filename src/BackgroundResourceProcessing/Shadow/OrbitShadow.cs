using System;
using BackgroundResourceProcessing.BurstSolver;
using BackgroundResourceProcessing.Collections.Burst;
using BackgroundResourceProcessing.Mathematics;
using BackgroundResourceProcessing.Maths;
using BackgroundResourceProcessing.Utils;
using Unity.Burst;
using Unity.Burst.CompilerServices;

namespace BackgroundResourceProcessing.Shadow;

using Orbit = BackgroundResourceProcessing.Mathematics.Orbit;

internal readonly struct OrbitShadow
{
    internal struct Terminator
    {
        public double UT;
        public bool InShadow;

        /// <summary>
        /// Set when the solver gave up and fell back to reporting the vessel
        /// as lit forever. This only happens on degenerate input, and the
        /// caller should log it so that it can be turned into a test case.
        /// </summary>
        public bool Failed;

        public static Terminator Sun(double UT) => new() { UT = UT, InShadow = false };

        public static Terminator Shadow(double UT) => new() { UT = UT, InShadow = true };

        /// <summary>
        /// The result when the solver fails. Reporting the vessel as lit
        /// forever is deliberate: it is much better for players than a vessel
        /// that keeps rescheduling changepoints at high time warp.
        /// </summary>
        public static Terminator Failure() =>
            new()
            {
                UT = double.PositiveInfinity,
                InShadow = false,
                Failed = true,
            };
    }

    const int MaxIter = 32;

    /// <summary>
    /// The maximum number of iterations for <see cref="SolveBracketed"/>. At
    /// least every other iteration halves the bracket, so this is enough to
    /// get from a whole orbit down to <see cref="RootTolerance"/>.
    /// </summary>
    const int MaxBracketIter = 128;

    /// <summary>
    /// Root finding stops once the bracket is narrower than this, in seconds.
    /// </summary>
    const double RootTolerance = 1e-3;

    /// <summary>
    /// The number of steps per half orbit when scanning for the next region
    /// boundary.
    /// </summary>
    const int BoundaryScanSteps = 8;

    /// <summary>
    /// The largest radius, in multiples of the planet radius, that is
    /// considered on a hyperbolic orbit. This is well outside the SOI of any
    /// stock body, so it only exists to keep the true anomaly away from the
    /// asymptote.
    /// </summary>
    const double MaxHyperbolicRadius = 1000.0;

    readonly SolarSystem system;
    readonly int starIndex;
    readonly SystemBody star;
    readonly Orbit vessel;
    readonly SystemBody planet;

    OrbitShadow(SolarSystem system, in Orbit vessel, int starIndex)
    {
        this.system = system;
        this.vessel = vessel;
        this.starIndex = starIndex;
        this.star = system[starIndex];
        this.planet = system[vessel.ParentBodyIndex ?? -1];
    }

    unsafe delegate void ComputeOrbitTerminatorDelegate(
        SolarSystem* system,
        Orbit* vessel,
        int starIndex,
        double UT,
        out Terminator terminator
    );

    static ComputeOrbitTerminatorDelegate ComputeOrbitTerminatorFp = null;

    internal static unsafe Terminator ComputeOrbitTerminator(
        SystemBody[] bodies,
        Orbit vessel,
        int starIndex,
        double UT
    )
    {
        // This method does nothing but ensures that the static constructor for
        // the crash handler is called.
        BurstCrashHandler.Init();

        fixed (SystemBody* ptr = bodies)
        {
            var system = new SolarSystem(new MemorySpan<SystemBody>(ptr, bodies.Length));

            Terminator term;
            if (!BurstUtil.EnableBurst)
            {
                term = ComputeOrbitTerminator(system, in vessel, starIndex, UT);
            }
            else
            {
                ComputeOrbitTerminatorFp ??= BurstCompiler
                    .CompileFunctionPointer<ComputeOrbitTerminatorDelegate>(
                        OrbitShadowBurst.ComputeOrbitTerminatorBurst
                    )
                    .Invoke;

                ComputeOrbitTerminatorFp(&system, &vessel, starIndex, UT, out term);
            }

            if (term.UT == double.MaxValue)
                term.UT = double.PositiveInfinity;

            return term;
        }
    }

    internal static unsafe Terminator ComputeOrbitTerminator(
        SolarSystem system,
        in Orbit vessel,
        int starIndex,
        double UT
    )
    {
        if (vessel.ParentBodyIndex == starIndex)
            return new() { UT = double.MaxValue, InShadow = false };

        var shadow = new OrbitShadow(system, in vessel, starIndex);
        if (vessel.Eccentricity < 1.0)
            return shadow.ComputeOrbitTerminatorNormal(UT);
        else
            return shadow.ComputeOrbitTerminatorHyperbolic(UT);
    }

    Terminator ComputeOrbitTerminatorHyperbolic(double UT)
    {
        // A hyperbolic orbit doesn't wrap around, so the search is done over
        // the anti-sunward parts of the remaining trajectory instead of one
        // arc at a time. The sun direction is taken as fixed at UT. That is
        // fine because a flyby is short compared to a planet's year, and the
        // edges of the anti-sunward region are far from the shadow anyway.
        //
        // If there is no shadow ahead then the vessel really is lit until it
        // leaves the SOI, at which point the shadow state is recomputed.
        var e = vessel.Eccentricity;
        var p = vessel.SemiLatusRectum;
        var maxR = MaxHyperbolicRadius * planet.Radius;
        var maxTA = Math.Acos(MathUtil.Clamp((p / maxR - 1.0) / e, -1.0, 1.0));
        var currentTA = vessel.GetTrueAnomalyAtUT(UT);
        if (!MathUtil.IsFinite(currentTA))
            return Terminator.Failure();
        if (currentTA >= maxTA)
            return Terminator.Sun(double.PositiveInfinity);

        // ŝ·v = r * A * cos(ν - φ), so the anti-sunward side is the true
        // anomaly range (φ - π/2, φ + π/2).
        var sun = GetSunVectorAtUT(UT);
        var phi = Math.Atan2(
            Vector3d.Dot(sun, vessel.OrbitFrame.Y),
            Vector3d.Dot(sun, vessel.OrbitFrame.X)
        );

        bool inShadow = !IsSunwardAtUT(UT) && IsInShadowAtUT(UT);

        // The anti-sunward range has a width of π and the trajectory covers
        // less than 2π, so at most two windows of it overlap the trajectory.
        for (int k = -1; k <= 1; ++k)
        {
            var loTA = Math.Max(currentTA, phi - 0.5 * Math.PI + 2.0 * Math.PI * k);
            var hiTA = Math.Min(maxTA, phi + 0.5 * Math.PI + 2.0 * Math.PI * k);
            if (!(hiTA > loTA))
                continue;

            var loUT = loTA == currentTA ? UT : vessel.GetUTAtTrueAnomaly(loTA, UT);
            var hiUT = vessel.GetUTAtTrueAnomaly(hiTA, UT);
            if (!(hiUT > loUT))
                continue;

            if (inShadow)
            {
                // The vessel can only be in the window that contains UT.
                if (loUT != UT)
                    break;

                // The window ends either on the boundary with the sunward side,
                // which is lit, or far away where the vessel may still be in
                // shadow if it is heading straight down the shadow axis.
                if (IsInShadowAtUT(hiUT))
                    return Terminator.Shadow(double.PositiveInfinity);

                return Terminator.Shadow(SolveTerminator(UT, hiUT, returnShadowSide: false));
            }

            var minimum = ComputeShadowPoint(loUT, hiUT, ComputeUTOfVesselMidpointTA(loUT, hiUT));
            if (minimum.Shadow)
                return Terminator.Sun(SolveTerminator(minimum.UT, loUT, returnShadowSide: true));
        }

        if (inShadow)
            return Terminator.Failure();

        return Terminator.Sun(double.PositiveInfinity);
    }

    Terminator ComputeOrbitTerminatorNormal(double UT)
    {
        double loUT;
        double hiUT;
        ShadowMinimum minimum;
        if (!IsSunwardAtUT(UT))
        {
            if (IsInShadowAtUT(UT))
                return Terminator.Shadow(ComputeTerminatorExit(UT));

            loUT = UT;
            hiUT = ComputeNextRegionBoundary(UT);

            // There is no boundary within the next orbit, so the whole orbit
            // is on the anti-sunward side.
            if (double.IsNaN(hiUT))
                return ComputeOrbitTerminatorOneSided(UT);
            if (!(hiUT > loUT))
                return Terminator.Failure();

            // The most reliable point to find a shadow region is exactly the
            // midpoint (by true anomaly) between the two boundary times.
            //
            // We put in extra effort here to actually compute this because
            // there are otherwise decent chances that we won't start the search
            // at a point that actually converges to a minimum.
            var prevUT = StepPrevRegionBoundary(hiUT);
            var startUT = MathUtil.IsFinite(prevUT)
                ? ComputeUTOfVesselMidpointTA(prevUT, hiUT)
                : 0.5 * (loUT + hiUT);

            minimum = ComputeShadowPoint(loUT, hiUT, Math.Max(startUT, loUT));
            if (minimum.Shadow)
                return Terminator.Sun(SolveTerminator(minimum.UT, UT, returnShadowSide: true));

            // There is no umbra crossing between now and the next boundary
            // so advance around to the next orbit.
            var sunUT = hiUT;
            loUT = StepNextRegionBoundary(sunUT);

            // If the boundaries can't be found then the sun direction is moving
            // quickly compared to the orbit. The vessel is lit until at least
            // the last boundary, so just check again from there.
            if (double.IsNaN(loUT))
                return Terminator.Sun(sunUT);
        }
        else
        {
            loUT = ComputeNextRegionBoundary(UT);

            // There is no boundary within the next orbit, so the whole orbit
            // is on the sunward side and the vessel is lit for all of it.
            if (double.IsNaN(loUT))
                return Terminator.Sun(UT + vessel.Period);
        }

        hiUT = StepNextRegionBoundary(loUT);
        if (double.IsNaN(hiUT))
            return Terminator.Sun(loUT);

        if (!(loUT >= UT && hiUT > loUT))
            return Terminator.Failure();

        minimum = ComputeShadowPoint(loUT, hiUT, ComputeUTOfVesselMidpointTA(loUT, hiUT));
        if (minimum.Shadow)
            return Terminator.Sun(SolveTerminator(minimum.UT, loUT, returnShadowSide: true));

        // The vessel is lit until at least the end of the arc that was just
        // searched.
        return Terminator.Sun(ComputeNextPossibleEclipse(hiUT));
    }

    /// <summary>
    /// Compute the terminator when the vessel stays on the anti-sunward side
    /// for at least the whole next orbit. This can happen on high orbits around
    /// planets close to their star, where the sun direction moves a long way
    /// during a single orbit.
    /// </summary>
    Terminator ComputeOrbitTerminatorOneSided(double UT)
    {
        var hiUT = UT + vessel.Period;
        var minimum = ComputeShadowPoint(UT, hiUT, UT + 0.5 * vessel.Period);
        if (minimum.Shadow)
            return Terminator.Sun(SolveTerminator(minimum.UT, UT, returnShadowSide: true));

        return Terminator.Sun(ComputeNextPossibleEclipse(hiUT));
    }

    double ComputeTerminatorExit(double UT)
    {
        var hiUT = ComputeNextRegionBoundary(UT);
        bool oneSided = double.IsNaN(hiUT);
        if (oneSided)
            hiUT = UT + vessel.Period;
        if (!(hiUT > UT))
            return double.MaxValue;

        if (oneSided || vessel.GetRadiusAtUT(hiUT) <= planet.Radius)
        {
            // Either we're on a suborbital trajectory or there is no boundary
            // within the next orbit. We need to check if our orbit is entirely
            // within the planet's shadow or if we can find a crossing point.

            var maximum = ComputeLitPoint(UT, hiUT, 0.5 * (UT + hiUT));
            if (maximum.Shadow)
                return oneSided ? hiUT : double.MaxValue;

            hiUT = maximum.UT;
        }

        return SolveTerminator(UT, hiUT, returnShadowSide: false);
    }

    /// <summary>
    /// Find the earliest time, no earlier than <paramref name="litUntilUT"/>,
    /// at which the vessel could possibly be in shadow. The vessel must be
    /// known to be lit until <paramref name="litUntilUT"/>.
    /// </summary>
    ///
    /// <remarks>
    /// <para>
    /// Orbits in KSP don't precess, so the normal n̂ of the vessel's orbit is
    /// fixed. A point at radius r in the orbit plane is at least
    /// <c>r·|sin β|</c> from the shadow axis, where β is the angle between the
    /// sun direction ŝ and the orbit plane, so <c>sin β = ŝ·n̂</c>. The shadow
    /// is at most <c>R(1 + r/D)</c> wide, so the vessel can only be in shadow
    /// while <c>|ŝ·n̂| &lt; R/r_pe + R/D</c>.
    /// </para>
    ///
    /// <para>
    /// If the planet orbits the star directly then <c>ŝ = cos ν P̂ + sin ν Q̂</c>
    /// for the planet's true anomaly ν and perifocal axes P̂ and Q̂, so
    /// <c>ŝ·n̂ = K cos(ν - ψ)</c>. That is small in two windows per planet year,
    /// centred on <c>ψ ± π/2</c>, and the start of the next one can be found
    /// exactly. For a moon, ŝ is the direction to the ancestor that orbits
    /// the star, moved by at most <c>2·offset/D</c> where offset is the sum of
    /// the apoapses of the moons in between. That is added to the margin.
    /// </para>
    ///
    /// <para>
    /// If the star isn't an ancestor of the planet then there is no simple
    /// bound, so the vessel is just checked again at
    /// <paramref name="litUntilUT"/>.
    /// </para>
    /// </remarks>
    double ComputeNextPossibleEclipse(double litUntilUT)
    {
        if (!MathUtil.IsFinite(litUntilUT))
            return litUntilUT;

        // Find the ancestor of the planet that orbits the star, and how far the
        // planet can be from it.
        int index = vessel.ParentBodyIndex ?? -1;
        double offset = 0.0;
        for (int depth = 0; ; ++depth)
        {
            if (index < 0 || depth >= system.Count)
                return litUntilUT;

            ref var body = ref system[index];
            if (!body.HasOrbit)
                return litUntilUT;

            var parent = body.Orbit.ParentBodyIndex ?? -1;
            if (parent == starIndex)
                break;

            offset += body.Orbit.ApR;
            index = parent;
        }

        ref var ancestor = ref system[index].Orbit;
        if (!(ancestor.Eccentricity < 1.0))
            return litUntilUT;

        var distance = ancestor.PeR - offset;
        if (!(distance > 0.0) || !(vessel.PeR > 0.0))
            return litUntilUT;

        var normal = vessel.OrbitFrame.Z;
        var A = Vector3d.Dot(ancestor.OrbitFrame.X, normal);
        var B = Vector3d.Dot(ancestor.OrbitFrame.Y, normal);
        var K = Math.Sqrt(A * A + B * B);

        var limit = planet.Radius / vessel.PeR + planet.Radius / distance + 2.0 * offset / distance;
        // Leave a little room for rounding error.
        limit = limit * (1.0 + 1e-6) + 1e-9;

        // An eclipse is possible at every point of the planet's orbit.
        if (!(limit < K))
            return litUntilUT;

        var halfWidth = Math.Asin(limit / K);
        var psi = Math.Atan2(B, A);

        // The windows are centred on ψ + π/2 + kπ. Find how far past the start
        // of the most recent one the planet is.
        var nu = ancestor.GetTrueAnomalyAtUT(litUntilUT);
        var windowStart = psi + 0.5 * Math.PI - halfWidth;
        var phase = (nu - windowStart) % Math.PI;
        if (phase < 0.0)
            phase += Math.PI;

        if (!MathUtil.IsFinite(phase) || phase < 2.0 * halfWidth)
            return litUntilUT;

        var nextUT = ancestor.GetUTAtTrueAnomaly(nu + (Math.PI - phase), litUntilUT);
        if (!MathUtil.IsFinite(nextUT))
            return litUntilUT;

        return Math.Max(litUntilUT, nextUT);
    }

    /// <summary>
    /// Compute the next boundary between the sunward side and the shadowed side
    /// of the orbit. This does not assume that UT is near a boundary crossing.
    /// </summary>
    ///
    /// <remarks>
    /// For a fixed sun direction the two boundaries are exactly π apart in
    /// true anomaly, so stepping forward by π/8 until the side changes always
    /// finds a bracket around the next one. The returned time is on the far
    /// side of the boundary. Returns NaN if no boundary was found, which can
    /// only happen if the sun direction is exactly perpendicular to the orbit.
    /// </remarks>
    double ComputeNextRegionBoundary(double UT)
    {
        var currentTA = vessel.GetTrueAnomalyAtUT(UT);
        var startSun = IsSunwardAtUT(UT);

        var loUT = UT;
        for (int i = 1; i <= 2 * BoundaryScanSteps; ++i)
        {
            var hiUT = vessel.GetUTAtTrueAnomaly(currentTA + i * (Math.PI / BoundaryScanSteps), UT);
            if (!(hiUT > loUT))
                break;

            if (IsSunwardAtUT(hiUT) != startSun)
                return SolveRegionBoundary(loUT, hiUT);

            loUT = hiUT;
        }

        return double.NaN;
    }

    /// <summary>
    /// Compute the next region boundary after the current one.
    ///
    /// This assumes that <paramref name="UT"/> is the UT of a region boundary.
    /// Returns NaN if no boundary was found.
    /// </summary>
    double StepNextRegionBoundary(double UT)
    {
        if (!MathUtil.IsFinite(UT))
            return double.NaN;

        return StepRegionBoundary(UT, UT);
    }

    /// <summary>
    /// Compute the previous region boundary before the current one.
    ///
    /// This assumes that <paramref name="UT"/> is the UT of a region boundary.
    /// Returns NaN if no boundary was found.
    /// </summary>
    double StepPrevRegionBoundary(double UT)
    {
        if (!MathUtil.IsFinite(UT))
            return double.NaN;

        return StepRegionBoundary(UT, UT - vessel.Period);
    }

    /// <summary>
    /// Find the boundary that is about π of true anomaly after the boundary at
    /// <paramref name="UT"/>, searching forward from <paramref name="fromUT"/>.
    /// </summary>
    double StepRegionBoundary(double UT, double fromUT)
    {
        // The next boundary is close to π of true anomaly away, because the
        // sun direction only moves a little in half an orbit. Widen the
        // bracket around that point until it contains a sign change. Staying
        // below π keeps the bracket from reaching the boundaries on either
        // side.
        var targetTA = vessel.GetTrueAnomalyAtUT(UT) + Math.PI;

        for (double width = 0.125; width < Math.PI; width *= 2.0)
        {
            var loUT = vessel.GetUTAtTrueAnomaly(targetTA - width, fromUT);
            var hiUT = vessel.GetUTAtTrueAnomaly(targetTA + width, loUT);
            if (!(hiUT > loUT))
                break;

            if (IsSunwardAtUT(loUT) != IsSunwardAtUT(hiUT))
                return SolveRegionBoundary(loUT, hiUT);
        }

        return double.NaN;
    }

    /// <summary>
    /// Find the region boundary between <paramref name="loUT"/> and
    /// <paramref name="hiUT"/>, which must be on opposite sides of it. The
    /// returned time is on the same side as <paramref name="hiUT"/>.
    /// </summary>
    double SolveRegionBoundary(double loUT, double hiUT)
    {
        double sunUT;
        double shadowUT;
        if (IsSunwardAtUT(loUT))
            (sunUT, shadowUT) = (loUT, hiUT);
        else
            (sunUT, shadowUT) = (hiUT, loUT);

        SolveBracketed(RootFunction.SunDot, ref sunUT, ref shadowUT);
        return Math.Max(sunUT, shadowUT);
    }

    /// <summary>
    /// Find a UT such that the vessel is in the planet's shadow. If the orbit
    /// does not intersect the planet's shadow then it returns the UT with the
    /// minumum distance.
    /// </summary>
    /// <returns></returns>
    unsafe ShadowMinimum ComputeShadowPoint(double loUT, double hiUT, double startUT)
    {
        var minimum = ComputeShadowPointSimple(loUT, hiUT, startUT);
        if (minimum is not null)
            return (ShadowMinimum)minimum;

        return ComputeShadowPointFallback(loUT, hiUT);
    }

    ShadowMinimum? ComputeShadowPointSimple(double loUT, double hiUT, double startUT)
    {
        double UT = startUT;
        Dual2 f = default;
        for (int i = 0; i < MaxIter; ++i)
        {
            f = GetUmbraDistance(Dual2.Variable(UT));
            if (f.x <= 0.0)
                return new() { UT = UT, Dist = f.x };

            // We are almost exactly at an inflection point, newton's method
            // won't give us anything useful here.
            if (MathUtil.ApproxEqual(f.ddx, 0.0))
                return null;

            // If ddx <= 0 then we are heading towards a local maximum.
            // This is not what we want, so break out.
            if (f.ddx <= 0.0)
                return null;

            var delta = f.dx / f.ddx;
            UT -= delta;

            if (MathUtil.ApproxEqual(delta, 0.0))
                break;

            // Newton's method is converging to something outside the range,
            // there may not actually be a minimum in the requested range.
            if (UT <= loUT || UT >= hiUT)
                return null;
        }

        return new() { UT = UT, Dist = f.x };
    }

    [SkipLocalsInit]
    unsafe ShadowMinimum ComputeShadowPointFallback(double loUT, double hiUT)
    {
        double loTA = vessel.GetTrueAnomalyAtUT(loUT);
        double hiTA = vessel.GetTrueAnomalyAtUT(hiUT);
        if (hiTA <= loTA)
            hiTA += 2 * Math.PI;

        double* UTs = stackalloc double[64];
        double* fs = stackalloc double[64];

        double step = (hiTA - loTA) * (1.0 / 63.0);
        for (int i = 0; i < 64; ++i)
        {
            double tA = loTA + i * step;
            double UT = vessel.GetUTAtTrueAnomaly(tA, loUT);
            double f = GetUmbraDistance(UT);

            if (f < 0.0)
                return new() { UT = UT, Dist = f };

            UTs[i] = UT;
            fs[i] = f;
        }

        int minI = 0;
        double minUT = loUT;
        double min = double.MaxValue;

        for (int i = 0; i < 64; ++i)
        {
            if (fs[i] >= min)
                continue;

            minI = i;
            minUT = UTs[i];
            min = fs[i];
        }

        loUT = UTs[MathUtil.Clamp(minI - 1, 0, 63)];
        hiUT = UTs[MathUtil.Clamp(minI + 1, 0, 63)];

        var minimum = ComputeShadowPointSimple(loUT, hiUT, minUT);
        return minimum ?? new() { UT = minUT, Dist = min };
    }

    /// <summary>
    /// Find a UT such that the vessel is not in the planet's shadow. If the
    /// orbit does not intersect the planet's shadow then it returns the UT with
    /// the maximum distance.
    /// </summary>
    /// <returns></returns>
    unsafe ShadowMinimum ComputeLitPoint(double loUT, double hiUT, double startUT)
    {
        var maximum = ComputeLitPointSimple(loUT, hiUT, startUT);
        if (maximum is not null)
            return (ShadowMinimum)maximum;

        return ComputeLitPointFallback(loUT, hiUT);
    }

    ShadowMinimum? ComputeLitPointSimple(double loUT, double hiUT, double startUT)
    {
        double UT = startUT;
        Dual2 f = default;
        for (int i = 0; i < MaxIter; ++i)
        {
            f = GetUmbraDistance(Dual2.Variable(UT));
            if (f.x <= 0.0)
                return new() { UT = UT, Dist = f.x };

            // We are almost exactly at an inflection point, newton's method
            // won't give us anything useful here.
            if (MathUtil.ApproxEqual(f.ddx, 0.0))
                return null;

            // If ddx >= 0 then we are heading towards a local minimum.
            // This is not what we want, so break out.
            if (f.ddx >= 0.0)
                return null;

            var delta = f.dx / f.ddx;
            UT -= delta;

            if (MathUtil.ApproxEqual(delta, 0.0))
                break;

            // Newton's method is converging to something outside the range,
            // there may not actually be a maximum in the requested range.
            if (UT <= loUT || UT >= hiUT)
                return null;
        }

        return new() { UT = UT, Dist = f.x };
    }

    [SkipLocalsInit]
    unsafe ShadowMinimum ComputeLitPointFallback(double loUT, double hiUT)
    {
        double loTA = vessel.GetTrueAnomalyAtUT(loUT);
        double hiTA = vessel.GetTrueAnomalyAtUT(hiUT);
        if (hiTA <= loTA)
            hiTA += 2 * Math.PI;

        double* UTs = stackalloc double[64];
        double* fs = stackalloc double[64];

        double step = (hiTA - loTA) * (1.0 / 63.0);
        for (int i = 0; i < 64; ++i)
        {
            double tA = loTA + i * step;
            double UT = vessel.GetUTAtTrueAnomaly(tA, loUT);
            double f = GetUmbraDistance(UT);

            if (f > 0.0)
                return new() { UT = UT, Dist = f };

            UTs[i] = UT;
            fs[i] = f;
        }

        int maxI = 0;
        double maxUT = loUT;
        double max = -double.MaxValue;

        for (int i = 0; i < 64; ++i)
        {
            if (fs[i] <= max)
                continue;

            maxI = i;
            maxUT = UTs[i];
            max = fs[i];
        }

        loUT = UTs[MathUtil.Clamp(maxI - 1, 0, 63)];
        hiUT = UTs[MathUtil.Clamp(maxI + 1, 0, 63)];

        var maximum = ComputeLitPointSimple(loUT, hiUT, maxUT);
        return maximum ?? new() { UT = maxUT, Dist = max };
    }

    struct ShadowMinimum
    {
        public double UT;
        public double Dist;
        public readonly bool Shadow => Dist <= 0.0;
    }

    /// <summary>
    /// Find the time at which the vessel crosses the shadow edge between
    /// <paramref name="shadowUT"/> (in shadow) and <paramref name="litUT"/>
    /// (lit).
    /// </summary>
    ///
    /// <param name="shadowUT">A time at which the vessel is in shadow.</param>
    /// <param name="litUT">A time at which the vessel is lit.</param>
    /// <param name="returnShadowSide">
    /// Whether to return the end of the final bracket that is in shadow, or
    /// the end that is lit. Callers pick the side that the vessel is moving
    /// into, so that evaluating the state at the returned time always gives
    /// the new state.
    /// </param>
    double SolveTerminator(double shadowUT, double litUT, bool returnShadowSide)
    {
        SolveBracketed(RootFunction.Umbra, ref shadowUT, ref litUT);
        return returnShadowSide ? shadowUT : litUT;
    }

    enum RootFunction
    {
        /// <summary>
        /// <see cref="GetUmbraDistance(Dual)"/>, which is <c>&lt;= 0</c> in
        /// shadow.
        /// </summary>
        Umbra,

        /// <summary>
        /// <see cref="ComputeSunDot(Dual)"/>, which is <c>&lt; 0</c> on the
        /// sunward side.
        /// </summary>
        SunDot,
    }

    Dual EvaluateRoot(RootFunction fn, double UT) =>
        fn == RootFunction.Umbra
            ? GetUmbraDistance(Dual.Variable(UT))
            : ComputeSunDot(Dual.Variable(UT));

    static bool IsNegative(RootFunction fn, double value) =>
        fn == RootFunction.Umbra ? value <= 0.0 : value < 0.0;

    /// <summary>
    /// Shrink the bracket between <paramref name="negUT"/> and
    /// <paramref name="posUT"/> (in either order) around a sign change of
    /// <paramref name="fn"/> until it is narrower than
    /// <see cref="RootTolerance"/>.
    /// </summary>
    ///
    /// <remarks>
    /// This is Newton's method with a bisection fallback. A Newton step that
    /// leaves the bracket, isn't finite, or doesn't converge quickly enough is
    /// replaced by a bisection step, so the bracket always contains the root.
    /// Once Newton has converged, the next step goes slightly past the root so
    /// that both ends of the bracket close in on it.
    /// </remarks>
    void SolveBracketed(RootFunction fn, ref double negUT, ref double posUT)
    {
        var UT = 0.5 * (negUT + posUT);
        var dxOld = Math.Abs(posUT - negUT);
        var dx = dxOld;

        for (int i = 0; i < MaxBracketIter; ++i)
        {
            if (!(Math.Abs(posUT - negUT) > RootTolerance))
                break;

            var f = EvaluateRoot(fn, UT);
            if (IsNegative(fn, f.x))
                negUT = UT;
            else
                posUT = UT;

            var lo = Math.Min(negUT, posUT);
            var hi = Math.Max(negUT, posUT);
            if (!(hi - lo > RootTolerance))
                break;

            var step = f.x / f.dx;
            var next = UT - step;

            if (!(next > lo && next < hi) || Math.Abs(step) > 0.5 * dxOld)
            {
                dxOld = dx;
                dx = 0.5 * (hi - lo);
                UT = lo + dx;
                continue;
            }

            dxOld = dx;
            dx = Math.Abs(step);

            if (dx < 0.5 * RootTolerance)
            {
                // Newton has converged. Step just past the root so that the
                // next evaluation lands on the other side of it.
                var nudge = 0.5 * RootTolerance;
                next = step > 0.0 ? UT - nudge : UT + nudge;
                if (!(next > lo && next < hi))
                    next = 0.5 * (lo + hi);
            }

            UT = next;
        }
    }

    bool IsSunwardAtUT(double UT) => ComputeSunDot(UT) < 0.0;

    bool IsInShadowAtUT(double UT) => GetUmbraDistance(UT) <= 0.0;

    double ComputeSunDot(double UT)
    {
        var starp = system.GetPositionAtUT(in star, UT);
        var planetp = system.GetPositionAtUT(in planet, UT);
        var vesselp = vessel.GetRelativePositionAtUT(UT);

        var sun = planetp - starp;
        return Vector3d.Dot(sun, vesselp);
    }

    Dual ComputeSunDot(Dual UT)
    {
        var starp = system.GetPositionAtUT(in star, UT);
        var planetp = system.GetPositionAtUT(in planet, UT);
        var vesselp = vessel.GetRelativePositionAtUT(UT);

        var sun = planetp - starp;
        return DualVector3.Dot(sun, vesselp);
    }

    double ComputeUTOfVesselMidpointTA(double loUT, double hiUT)
    {
        var loTA = vessel.GetTrueAnomalyAtUT(loUT);
        var hiTA = vessel.GetTrueAnomalyAtUT(hiUT);

        if (hiTA < loTA)
            hiTA += 2 * Math.PI;

        var midTA = 0.5 * (loTA + hiTA);
        return vessel.GetUTAtTrueAnomaly(midTA, loUT);
    }

    Vector3d GetSunVectorAtUT(double UT)
    {
        var starp = system.GetPositionAtUT(in star, UT);
        var planetp = system.GetPositionAtUT(in planet, UT);

        return planetp - starp;
    }

    double GetUmbraDistance(double UT)
    {
        var starp = system.GetPositionAtUT(in star, UT);
        var planetp = system.GetPositionAtUT(in planet, UT);
        var vesselp = vessel.GetRelativePositionAtUT(UT);

        var sunV = planetp - starp;
        var invSunD = 1.0 / sunV.magnitude;
        var sun = sunV * invSunD;

        var x = Vector3d.Dot(sun, vesselp);
        var xi = planet.Radius * (1.0 + x * invSunD);
        var delta = (vesselp - x * sun).magnitude;

        return delta - xi;
    }

    Dual GetUmbraDistance(Dual UT)
    {
        var starp = system.GetPositionAtUT(in star, UT);
        var planetp = system.GetPositionAtUT(in planet, UT);
        var vesselp = vessel.GetRelativePositionAtUT(UT);

        var sunV = planetp - starp;
        var sunD = sunV.Magnitude();
        var sun = sunV / sunD;

        var x = DualVector3.Dot(sun, vesselp);
        var xi = planet.Radius * (1.0 + x / sunD);
        var delta = (vesselp - x * sun).Magnitude();

        return delta - xi;
    }

    Dual2 GetUmbraDistance(Dual2 UT)
    {
        var starp = system.GetPositionAtUT(in star, UT);
        var planetp = system.GetPositionAtUT(in planet, UT);
        var vesselp = vessel.GetRelativePositionAtUT(UT);

        var sunV = planetp - starp;
        var sunD = sunV.Magnitude();
        var sun = sunV / sunD;

        var x = Dual2Vector3.Dot(sun, vesselp);
        var xi = planet.Radius * (1.0 + x / sunD);
        var delta = (vesselp - x * sun).Magnitude();

        return delta - xi;
    }

    static double ComputeOffsetUT(in Orbit orbit, double UT, double tA)
    {
        double currentTA = orbit.GetTrueAnomalyAtUT(UT);
        return orbit.GetUTAtTrueAnomaly(currentTA + tA, UT);
    }
}

[BurstCompile]
internal static class OrbitShadowBurst
{
    // This needs to be in a class or else we get a segfault somewhere within
    // mono when calling the burst function pointer.
    //
    // I have no idea why this happens, but putting it in a separate class
    // seems to fix the issue.
    [BurstCompile]
    internal static unsafe void ComputeOrbitTerminatorBurst(
        SolarSystem* system,
        Orbit* vessel,
        int starIndex,
        double UT,
        out OrbitShadow.Terminator terminator
    )
    {
        terminator = OrbitShadow.ComputeOrbitTerminator(*system, in *vessel, starIndex, UT);
    }
}
