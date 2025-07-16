using System;
using System.Collections.Generic;
using System.IO;
using static BackgroundResourceProcessing.Utils.MathUtil;

namespace BackgroundResourceProcessing.Maths;

internal ref struct LandedShadow
{
    static int Iterations = 128;

    // The star we are doing computations for.
    CelestialBody star;

    // The planet we are landed on.
    CelestialBody planet;

    // The body we are using to calculate the solar direction vector.
    //
    // This should be one of our ancestors but may not necessarily be orbiting
    // the star if kopernicus is installed.
    CelestialBody reference;

    Vessel vessel;

    Vector3d normal;

    // The nearest common ancestor of the reference body and the star.
    readonly CelestialBody Ancestor => reference.referenceBody;
    readonly bool PlanetIsReference => ReferenceEquals(planet, reference);
    readonly bool StarIsAncestor => ReferenceEquals(star, Ancestor);
    readonly bool StarIsPlanet => ReferenceEquals(star, planet);

    public void SetReferenceBodies(Vessel vessel, CelestialBody star)
    {
        this.vessel = vessel;
        this.star = star;
        this.planet = vessel.orbit.referenceBody;

        HashSet<CelestialBody> ancestors = [];
        CelestialBody ancestor = star;

        do
        {
            if (!ancestors.Add(ancestor))
                break;
            ancestor = ancestor.orbit?.referenceBody;
        } while (ancestor != null);

        CelestialBody reference = planet;

        while (!ancestors.Contains(reference.referenceBody))
            reference = reference.referenceBody;

        this.reference = reference;
    }

    public double ComputeTerminatorUT(out bool inshadow)
    {
        // We are somehow landed on the star? Anyways, we're not in shadow in
        // this case.
        if (StarIsPlanet)
        {
            inshadow = false;
            return double.PositiveInfinity;
        }

        var UT = Planetarium.GetUniversalTime();
        var startUT = UT;
        normal = planet.GetSurfaceNVector(vessel.latitude, vessel.longitude);
        var sun = GetSunVectorAtUT(UT).normalized;

        inshadow = Vector3d.Dot(normal, sun) > 0;

        // Planet is tidally locked, and is orbiting the star, there will be no
        // future changes here.
        if (
            PlanetIsReference
            && StarIsAncestor
            && (planet.tidallyLocked || planet.rotationPeriod == 0.0)
        )
            return double.PositiveInfinity;

        var cosaxis = Math.Abs(Vector3d.Dot(sun, planet.RotationAxis));
        var coslat = Math.Cos(Math.Abs(vessel.latitude) * DEG2RAD);

        if (coslat <= cosaxis)
        {
            // We are currently in the polar circle and there won't be a
            // terminator until the planet proceeds around it's orbit.

            UT = FindInclinationExit(UT);
            if (!IsFinite(UT))
                return UT;
        }

        var dot = ComputeDotAt(Dual2.Variable(UT));

        int count = 0;
        double advance = 0.25 * Math.Min(planet.rotationPeriod, planet.orbit.period);

        if (Math.Sign(dot.x) * Math.Sign(dot.dx) > 0)
        {
            // Newton's method wants to find a zero in the past. That is not
            // useful to us so we find the next extreme and flip to the other side.

            // If we are near sunrise/sunset then finding the maximum becomes difficult.
            // We can hopefully move out of the problematic region by advancing
            // UT by a bit.
            if (Math.Abs(dot.dx / dot.ddx) >= planet.solarDayLength / 4)
                UT += advance;

            double extremeUT = FindMiddayOrMidnight(UT);

            // This is something that should not be happening, we can try again in the
            // future.
            if (extremeUT < startUT)
                return UT + planet.solarDayLength / 4;

            UT += 2 * (extremeUT - UT);

            dot = ComputeDotAt(Dual2.Variable(UT));
        }

        // Cases we want to avoid:
        // 1. dx == 0. This breaks newton's method.
        //
        // We address both cases by advancing a quarter-period and trying again.
        while (Math.Abs(dot.x / dot.dx) >= planet.solarDayLength / 4)
        {
            // If we've gone 8 advances without seeing any change then we might
            // be nearly tidally locked and we can try again later.
            if (count > 8)
                return UT;
            count += 1;

            // Advance by a quarter orbit period, or a quarter rotation, whichever
            // is shorter, and try again.

            UT += advance;
            dot = ComputeDotAt(Dual2.Variable(UT));
        }

        return FindTerminator(UT);
    }

    private readonly double FindTerminator(double startUT)
    {
        double probe = startUT;

        for (int i = 0; i < Iterations; ++i)
        {
            var dot = ComputeDotAt(Dual.Variable(probe));
            var delta = dot.x / dot.dx;
            probe -= delta;

            if (ApproxEqual(delta, 0.0))
                break;
        }

        return probe;
    }

    private readonly double FindMiddayOrMidnight(double startUT)
    {
        double probe = startUT;

        for (int i = 0; i < Iterations; ++i)
        {
            var dot = ComputeDotAt(Dual2.Variable(probe));
            var delta = dot.dx / dot.ddx;
            probe -= delta;

            if (ApproxEqual(delta, 0.0))
                break;
        }

        return probe;
    }

    private readonly double FindInclinationExit(double startUT)
    {
        var diff = GetRelativeInclinationAngle(Dual.Variable(startUT));

        if (ApproxEqual(diff.dx, 0.0, 1e-9))
        {
            // We are almost at a maximum. Add a eighth-orbit and try then.
            startUT += reference.orbit.period * 0.125;
        }
        else if (Math.Sign(diff.x) * Math.Sign(diff.dx) > 0)
        {
            // Newton's method is trying to find a crossing in the past.
            // We can't really use that so instead we find the maximum
            // and jump to the other side.

            double maxUT = FindInclinationMaximum(startUT);

            // If this happens our assumptions are broken. Just return
            // infinity in this case.
            if (maxUT < startUT)
                return double.PositiveInfinity;

            startUT += (maxUT - startUT) * 2;
        }

        return FindInclinationTerminator(startUT);
    }

    private readonly double FindInclinationMaximum(double startUT)
    {
        double probe = startUT;
        var coslat = Math.Abs(Math.Cos(vessel.latitude * DEG2RAD));

        for (int i = 0; i < Iterations; ++i)
        {
            var UT = Dual2.Variable(probe);
            var sun = GetSunVectorAtUT(UT);
            var cos = Dual2.Abs(Dual2Vector3.Dot(sun, new(planet.RotationAxis)));
            var diff = cos - coslat;

            if (!IsFinite(cos.dx))
                break;

            double delta = diff.dx / diff.ddx;
            probe -= delta;
            if (Math.Abs(delta) < 1.0)
                break;
        }

        return probe;
    }

    private readonly double FindInclinationTerminator(double startUT)
    {
        double probe = startUT;
        var coslat = Math.Abs(Math.Cos(vessel.latitude * DEG2RAD));

        for (int i = 0; i < Iterations; ++i)
        {
            var UT = Dual.Variable(probe);
            var sun = GetSunVectorAtUT(UT);
            var cos = Dual.Abs(DualVector3.Dot(sun, new(planet.RotationAxis)));
            var diff = cos - coslat;

            double delta = diff.x / diff.dx;
            probe -= delta;
            if (Math.Abs(delta) < 1.0)
                break;
        }

        return probe;
    }

    private readonly Dual GetRelativeInclinationAngle(Dual UT)
    {
        var coslat = Math.Abs(Math.Cos(vessel.latitude * DEG2RAD));
        var sun = GetSunVectorAtUT(UT).Normalized();
        var cos = Dual.Abs(DualVector3.Dot(sun, new(planet.RotationAxis)));
        return cos - coslat;
    }

    private readonly Dual ComputeDotAt(Dual UT)
    {
        var pUT = UT - Planetarium.GetUniversalTime();
        var theta = planet.angularV * pUT;
        if (!planet.rotates)
            theta = new(0);
        var q = DualQuaternion.FromAngleAxis(theta, new(planet.RotationAxis));

        var sun = GetSunVectorAtUT(UT);
        var normal = q.Rotate(new(this.normal));

        return DualVector3.Dot(sun, normal);
    }

    private readonly Dual2 ComputeDotAt(Dual2 UT)
    {
        var pUT = UT - Planetarium.GetUniversalTime();
        var theta = planet.angularV * pUT;
        if (!planet.rotates)
            theta = new(0);
        var q = Dual2Quaternion.FromAngleAxis(theta, new(planet.RotationAxis));

        var sun = GetSunVectorAtUT(UT);
        var normal = q.Rotate(new(this.normal));

        return Dual2Vector3.Dot(sun, normal);
    }

    private readonly Vector3d GetSunVectorAtUT(double UT)
    {
        var ppos = planet.GetTruePositionAtUT(UT);
        var spos = star.GetTruePositionAtUT(UT);

        return ppos - spos;
    }

    private readonly DualVector3 GetSunVectorAtUT(Dual UT)
    {
        var ppos = planet.GetTruePositionAtUT(UT);
        var spos = star.GetTruePositionAtUT(UT);

        return ppos - spos;
    }

    private readonly Dual2Vector3 GetSunVectorAtUT(Dual2 UT)
    {
        var ppos = planet.GetTruePositionAtUT(UT);
        var spos = star.GetTruePositionAtUT(UT);

        return ppos - spos;
    }

    private readonly bool DumpDotCSV(double startUT)
    {
        return DumpDotCSV(startUT, planet.rotationPeriod);
    }

    private readonly bool DumpDotCSV(double startUT, double period)
    {
        return DumpDotCSV(startUT, period, 60);
    }

    private readonly bool DumpDotCSV(double startUT, double period, double step)
    {
        double end = startUT + period;

        using var file = File.Open("D:\\dump.csv", FileMode.Create);
        using var stream = new StreamWriter(file);

        for (double vUT = startUT; vUT < end; vUT += 60)
        {
            var UT = Dual2.Variable(vUT);
            var pUT = UT - startUT;
            var theta = planet.angularV * pUT;
            if (!planet.rotates)
                theta = new(0);
            if (!planet.inverseRotation)
                theta = -theta;
            var q = Dual2Quaternion.FromAngleAxis(theta, new(planet.RotationAxis));

            var sun = GetSunVectorAtUT(UT);
            var normal = q.Rotate(new(this.normal));

            var dot = Dual2Vector3.Dot(sun, normal);

            stream.Write($"{UT.x}, {dot.x}, {dot.dx}, {dot.ddx}, ");
            stream.Write($"{sun.x.x}, {sun.y.x}, {sun.z.x}, ");
            stream.Write($"{sun.x.dx}, {sun.y.dx}, {sun.z.dx}, ");
            stream.Write($"{sun.x.ddx}, {sun.y.ddx}, {sun.z.ddx}, ");
            stream.Write($"{normal.x.x}, {normal.y.x}, {normal.z.x}, ");
            stream.Write($"{normal.x.dx}, {normal.y.dx}, {normal.z.dx}, ");

            stream.WriteLine();
        }

        return true;
    }
}
