using System;
using BackgroundResourceProcessing.BurstSolver;
using BackgroundResourceProcessing.Collections.Burst;
using BackgroundResourceProcessing.Maths;
using BackgroundResourceProcessing.Utils;
using Unity.Burst;

namespace BackgroundResourceProcessing.Mathematics;

internal readonly struct OrbitShadow
{
    internal struct Terminator
    {
        public double UT;
        public bool InShadow;

        public static Terminator Sun(double UT) => new() { UT = UT, InShadow = false };

        public static Terminator Shadow(double UT) => new() { UT = UT, InShadow = true };
    }

    const int MaxIter = 32;
    const int BisectIter = 4;

    readonly SolarSystem system;
    readonly SystemBody star;
    readonly Orbit vessel;
    readonly SystemBody planet;
    readonly double planetRadius;

    OrbitShadow(SolarSystem system, Orbit vessel, int starIndex, double planetRadius)
    {
        this.system = system;
        this.vessel = vessel;
        this.star = system[starIndex];
        this.planet = system[vessel.ParentBodyIndex ?? -1];
        this.planetRadius = planetRadius;
    }

    unsafe delegate void ComputeOrbitTerminatorDelegate(
        SolarSystem* system,
        Orbit* vessel,
        int starIndex,
        double UT,
        double planetRadius,
        out Terminator terminator
    );

    static ComputeOrbitTerminatorDelegate ComputeOrbitTerminatorFp = null;

    internal static unsafe Terminator ComputeOrbitTerminator(
        SystemBody[] bodies,
        Orbit vessel,
        int starIndex,
        double UT,
        double planetRadius
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
                term = ComputeOrbitTerminator(system, in vessel, starIndex, UT, planetRadius);
            }
            else
            {
                ComputeOrbitTerminatorFp ??= BurstCompiler
                    .CompileFunctionPointer<ComputeOrbitTerminatorDelegate>(
                        OrbitShadowBurst.ComputeOrbitTerminatorBurst
                    )
                    .Invoke;

                ComputeOrbitTerminatorFp(&system, &vessel, starIndex, UT, planetRadius, out term);
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
        double UT,
        double planetRadius
    )
    {
        if (vessel.ParentBodyIndex == starIndex)
            return new() { UT = double.MaxValue, InShadow = false };

        var shadow = new OrbitShadow(system, vessel, starIndex, planetRadius);
        if (vessel.Eccentricity < 1.0)
            return shadow.ComputeOrbitTerminatorNormal(UT);
        else
            return shadow.ComputeOrbitTerminatorHyperbolic(UT);
    }

    Terminator ComputeOrbitTerminatorHyperbolic(double UT)
    {
        // TODO: We can't assume that the orbit can wrap around for hyperbolic
        //       orbits. For now, just don't implement this.
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

            if (hiUT < loUT)
                return Terminator.Sun(double.PositiveInfinity);

            // The most reliable point to find a shadow region is exactly the
            // midpoint (by true anomaly) between the two boundary times.
            //
            // We put in extra effort here to actually compute this because
            // there are otherwise decent chances that we won't start the search
            // at a point that actually converges to a minimum.
            var prevUT = StepPrevRegionBoundary(hiUT);
            var startUT = ComputeUTOfVesselMidpointTA(prevUT, hiUT);

            minimum = ComputeShadowPoint(loUT, hiUT, Math.Max(startUT, loUT));
            if (minimum.Shadow)
                return Terminator.Sun(BisectTerminator(minimum.UT, UT));

            // There is no umbra crossing between now and the next boundary
            // so advance around to the next orbit.
            loUT = StepNextRegionBoundary(hiUT);
            hiUT = StepNextRegionBoundary(loUT);
        }
        else
        {
            loUT = ComputeNextRegionBoundary(UT);
            hiUT = StepNextRegionBoundary(loUT);
        }

        if (hiUT < loUT)
            return Terminator.Sun(double.PositiveInfinity);

        minimum = ComputeShadowPoint(loUT, hiUT, ComputeUTOfVesselMidpointTA(loUT, hiUT));
        if (minimum.Shadow)
            return Terminator.Sun(BisectTerminator(minimum.UT, loUT));

        return Terminator.Sun(EstimateNextPotentialIntersection(minimum.UT));
    }

    double ComputeTerminatorExit(double UT)
    {
        var hiUT = ComputeNextRegionBoundary(UT);
        if (vessel.GetRadiusAtUT(hiUT) <= planetRadius)
        {
            // We're on a suborbital trajectory. We need to check if our orbit
            // is entirely within the planet's shadow or if we can find a
            // crossing point.

            var maximum = ComputeLitPoint(UT, hiUT, 0.5 * (UT + hiUT));
            if (maximum.Shadow)
                return double.MaxValue;

            hiUT = maximum.UT;
        }

        return BisectTerminator(UT, hiUT);
    }

    double EstimateNextPotentialIntersection(double minUT)
    {
        // We can lower-bound the next time we might intersect the terminator
        // by getting the angular distance between the current orbit and the
        // umbra, and then figuring out the UT at which sun vector will have
        // moved by at least that angle.
        //
        // This will be a big underestimate in cases where the minimum angular
        // distance is increasing, but that's actually not a big issue since
        // time scales here are measured in terms of years, so a few extra
        // iterations are unlikely to hurt.
        //
        // If there are cases where the target star's variation in solar angle
        // will not exceed then this method will likely return nonsense. I do
        // not expect those results to show up in actual gameplay, even with
        // weird kopernicus systems there should either be a nearer star that
        // will take priority or else power produced by solar panels is so
        // minimal that it will not matter.

        var cosa = GetCosAngularUmbraDistance(minUT);
        var csun2 = GetSunVectorAtUT(Dual2.Variable(minUT)).Normalized();
        var csun = csun2.v;

        // We can approximate dot(sun, sun') as cos(A*t). We can't start newton's
        // method at minUT because dx will always be 0 at that point so we need
        // to somehow come up with a starting point estimate.
        //
        // The second derivative of the function cos(A*t) is -A*A*cos(A*t), so
        // we can estimate A as sqrt(abs(ddx)).
        var scale = Math.Sqrt(Math.Abs(Dual2Vector3.Dot(new(csun), csun2).ddx));
        // We can then plug this back in to solve cos(A*t) - cos α == 0 for t.
        var offset = Math.Acos(cosa) / scale;

        var UT = minUT + offset;
        for (int i = 0; i < MaxIter; ++i)
        {
            var sun = GetSunVectorAtUT(Dual.Variable(UT)).Normalized();
            var f = DualVector3.Dot(sun, new(csun)) - cosa;

            if (f.dx == 0.0)
                return minUT + vessel.Period;

            var delta = f.x / f.dx;
            UT -= delta;

            if (MathUtil.ApproxEqual(delta, 0.0))
                break;
        }

        return Math.Max(UT, minUT + vessel.Period);
    }

    /// <summary>
    /// Compute the next boundary between the sunward side and the shadowed side
    /// of the orbit. This does not assume that UT is near a boundary crossing.
    /// </summary>
    double ComputeNextRegionBoundary(double UT)
    {
        var loUT = UT;
        var hiUT = ComputeOffsetUT(in vessel, UT, Math.PI);

        var loSun = IsSunwardAtUT(loUT);
        var hiSun = IsSunwardAtUT(hiUT);

        if (loSun == hiSun)
        {
            // There's a couple of conditions under which going 0.5 orbits
            // into the future is still on the same side of the planet:
            // - it was borderline anyway
            // - both the vessel and the star are orbiting the same body
            //
            // The solution here behaves reasonably for the first case, and
            // the second case is likely to not matter all that much.
            UT = hiUT;
        }
        else
        {
            // Do a couple iterations of bisection in order to get a good enough
            // initial estimate for newton's method.
            double sunUT;
            double shadowUT;
            if (loSun)
                (sunUT, shadowUT) = (loUT, hiUT);
            else
                (sunUT, shadowUT) = (hiUT, loUT);

            for (int i = 0; i < BisectIter; ++i)
            {
                UT = 0.5 * (sunUT + shadowUT);

                if (IsSunwardAtUT(UT))
                    sunUT = UT;
                else
                    shadowUT = UT;
            }
        }

        for (int i = 0; i < MaxIter; ++i)
        {
            var f = ComputeSunDot(Dual.Variable(UT));
            var delta = f.x / f.dx;
            UT -= delta;

            if (MathUtil.ApproxEqual(delta, 0.0))
                break;
        }

        return UT;
    }

    /// <summary>
    /// Compute the next region boundary after the current one.
    ///
    /// This assumes that <paramref name="UT"/> is the UT of a region boundary.
    /// </summary>
    double StepNextRegionBoundary(double UT)
    {
        // A good estimate for the next boundary is just advancing by half an
        // orbit.
        UT = ComputeOffsetUT(in vessel, UT, Math.PI);

        for (int i = 0; i < MaxIter; ++i)
        {
            var f = ComputeSunDot(Dual.Variable(UT));
            var delta = f.x / f.dx;

            UT -= delta;
            if (MathUtil.ApproxEqual(delta, 0.0))
                break;
        }

        return UT;
    }

    /// <summary>
    /// Compute the previous region boundary before the current one.
    ///
    /// This assumes that <paramref name="UT"/> is the UT of a region boundary.
    /// </summary>
    double StepPrevRegionBoundary(double UT)
    {
        // A good estimate for the previous boundary is just going back by half
        // an orbit.
        UT = ComputeOffsetUT(in vessel, UT, Math.PI) - vessel.Period;

        for (int i = 0; i < MaxIter; ++i)
        {
            var f = ComputeSunDot(Dual.Variable(UT));
            var delta = f.x / f.dx;

            UT -= delta;
            if (MathUtil.ApproxEqual(delta, 0.0))
                break;
        }

        return UT;
    }

    /// <summary>
    /// Find a UT such that the vessel is in the planet's shadow. If the orbit
    /// does not intersect the planet's shadow then it returns the UT with the
    /// minumum distance.
    /// </summary>
    /// <returns></returns>
    unsafe ShadowMinimum ComputeShadowPoint(double loUT, double hiUT, double startUT)
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
                goto FALLBACK;

            // If ddx <= 0 then we are heading towards a local maximum.
            // This is not what we want, so break out.
            if (f.ddx <= 0.0)
                goto FALLBACK;

            var delta = f.dx / f.ddx;
            UT -= delta;

            if (MathUtil.ApproxEqual(delta, 0.0))
                break;

            // Newton's method is converging to something outside the range,
            // there may not actually be a minimum in the requested range.
            if (UT <= loUT || UT >= hiUT)
                goto FALLBACK;
        }

        return new() { UT = UT, Dist = f.x };

        FALLBACK:

        // As a fallback, just find the minimum distance across the starting
        // points.
        double* UTmem = stackalloc double[3] { startUT, loUT, hiUT };
        MemorySpan<double> UTs = new(UTmem, 3);

        double min = double.PositiveInfinity;
        UT = double.PositiveInfinity;
        foreach (var vUT in UTs)
        {
            double dist = GetUmbraDistance(vUT);
            if (dist >= min)
                continue;

            dist = min;
            UT = vUT;
        }

        return new() { UT = UT, Dist = min };
    }

    /// <summary>
    /// Find a UT such that the vessel is not in the planet's shadow. If the
    /// orbit does not intersect the planet's shadow then it returns the UT with
    /// the maximum distance.
    /// </summary>
    /// <returns></returns>
    unsafe ShadowMinimum ComputeLitPoint(double loUT, double hiUT, double startUT)
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
                goto FALLBACK;

            // If ddx >= 0 then we are heading towards a local minimum.
            // This is not what we want, so break out.
            if (f.ddx >= 0.0)
                goto FALLBACK;

            var delta = f.dx / f.ddx;
            UT -= delta;

            if (MathUtil.ApproxEqual(delta, 0.0))
                break;

            // Newton's method is converging to something outside the range,
            // there may not actually be a maximum in the requested range.
            if (UT <= loUT || UT >= hiUT)
                goto FALLBACK;
        }

        return new() { UT = UT, Dist = f.x };

        FALLBACK:

        // As a fallback, just find the maximum distance across the starting
        // points.
        double* UTmem = stackalloc double[3] { startUT, loUT, hiUT };
        MemorySpan<double> UTs = new(UTmem, 3);

        double max = double.PositiveInfinity;
        UT = double.PositiveInfinity;
        foreach (var vUT in UTs)
        {
            double dist = GetUmbraDistance(vUT);
            if (dist <= max)
                continue;

            dist = max;
            UT = vUT;
        }

        return new() { UT = UT, Dist = max };
    }

    struct ShadowMinimum
    {
        public double UT;
        public double Dist;
        public readonly bool Shadow => Dist <= 0.0;
    }

    double BisectTerminator(double shadowUT, double sunUT)
    {
        double UT = 0.0;
        for (int i = 0; i < BisectIter; ++i)
        {
            UT = 0.5 * (shadowUT + sunUT);
            var distance = GetUmbraDistance(UT);

            if (MathUtil.ApproxEqual(distance, 0.0))
                break;
            if (distance < 0.0)
                shadowUT = UT;
            else
                sunUT = UT;
        }

        for (int i = 0; i < MaxIter; ++i)
        {
            var f = GetUmbraDistance(Dual.Variable(UT));
            var delta = f.x / f.dx;
            UT -= delta;

            if (MathUtil.ApproxEqual(delta, 0.0))
                break;
        }

        return UT;
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

    DualVector3 GetSunVectorAtUT(Dual UT)
    {
        var starp = system.GetPositionAtUT(in star, UT);
        var planetp = system.GetPositionAtUT(in planet, UT);

        return planetp - starp;
    }

    Dual2Vector3 GetSunVectorAtUT(Dual2 UT)
    {
        var starp = system.GetPositionAtUT(in star, UT);
        var planetp = system.GetPositionAtUT(in planet, UT);

        return planetp - starp;
    }

    double GetCosAngularUmbraDistance(double UT)
    {
        var starp = system.GetPositionAtUT(in star, UT);
        var planetp = system.GetPositionAtUT(in planet, UT);
        var vesselp = vessel.GetRelativePositionAtUT(UT);

        var sunV = planetp - starp;
        var sunD = sunV.magnitude;
        var invSunD = 1.0 / sunD;
        var sun = sunV * invSunD;

        var r2 = planetRadius * planetRadius;
        var s2 = sunD * sunD;
        var v2 = vesselp.sqrMagnitude;

        var l = sunD * (-r2 + Math.Sqrt(s2 * v2 + r2 * (v2 - s2))) / (r2 + s2);
        var xi = planetRadius * (1.0 + l * invSunD);

        var vperp = (vesselp - Vector3d.Dot(sun, vesselp) * sun).Normalized();
        var vprime = l * sun + xi * vperp;

        return Vector3d.Dot(vprime.Normalized(), vesselp.Normalized());
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
        var xi = planetRadius * (1.0 + x * invSunD);
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
        var xi = planetRadius * (1.0 + x / sunD);
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
        var xi = planetRadius * (1.0 + x / sunD);
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
    [BurstCompile(FloatMode = FloatMode.Fast)]
    internal static unsafe void ComputeOrbitTerminatorBurst(
        SolarSystem* system,
        Orbit* vessel,
        int starIndex,
        double UT,
        double planetRadius,
        out OrbitShadow.Terminator terminator
    )
    {
        terminator = OrbitShadow.ComputeOrbitTerminator(
            *system,
            in *vessel,
            starIndex,
            UT,
            planetRadius
        );
    }
}
