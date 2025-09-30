using System;
using BackgroundResourceProcessing.Collections.Burst;
using BackgroundResourceProcessing.Maths;
using BackgroundResourceProcessing.Utils;
using Unity.Burst;

namespace BackgroundResourceProcessing.Mathematics;

[BurstCompile]
internal readonly struct OrbitShadow
{
    internal struct Terminator
    {
        public double UT;
        public bool InShadow;

        public static Terminator Sun(double UT) => new() { UT = UT, InShadow = false };

        public static Terminator Shadow(double UT) => new() { UT = UT, InShadow = true };
    }

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

    internal static Terminator ComputeOrbitTerminator(
        in SolarSystem system,
        in Orbit vessel,
        int starIndex,
        double UT,
        double planetRadius
    )
    {
        ComputeOrbitTerminatorBurst(system, vessel, starIndex, UT, planetRadius, out var term);

        if (term.UT == double.MaxValue)
            term.UT = double.PositiveInfinity;

        return term;
    }

    [BurstCompile(FloatMode = FloatMode.Fast)]
    static void ComputeOrbitTerminatorBurst(
        in SolarSystem system,
        in Orbit vessel,
        int starIndex,
        double UT,
        double planetRadius,
        out Terminator terminator
    )
    {
        if (vessel.ParentBodyIndex == starIndex)
        {
            terminator = new() { UT = double.MaxValue, InShadow = false };
        }
        else
        {
            var shadow = new OrbitShadow(system, vessel, starIndex, planetRadius);
            if (vessel.Eccentricity < 1.0)
                terminator = shadow.ComputeOrbitTerminatorNormal(UT);
            else
                terminator = shadow.ComputeOrbitTerminatorHyperbolic(UT);
        }
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
            hiUT = StepNextRegionBoundary(UT);
        }

        if (hiUT < loUT)
            return Terminator.Sun(double.PositiveInfinity);

        minimum = ComputeShadowPoint(loUT, hiUT, ComputeUTOfVesselMidpointTA(loUT, hiUT));
        if (minimum.Shadow)
            return Terminator.Sun(BisectTerminator(minimum.UT, loUT));

        // TODO: Compute the time at which the vessel's orbit will next
        //       intersect the planet's umbra.
        return Terminator.Sun(vessel.Period);
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

            for (int i = 0; i < 4; ++i)
            {
                UT = 0.5 * (sunUT + shadowUT);

                if (IsSunwardAtUT(UT))
                    sunUT = UT;
                else
                    shadowUT = UT;
            }
        }

        for (int i = 0; i < 128; ++i)
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

        for (int i = 0; i < 128; ++i)
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

        for (int i = 0; i < 128; ++i)
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
        for (int i = 0; i < 128; ++i)
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
        for (int i = 0; i < 128; ++i)
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
        for (int i = 0; i < 5; ++i)
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

        for (int i = 0; i < 128; ++i)
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
