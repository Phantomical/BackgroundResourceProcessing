using System;
using BackgroundResourceProcessing.Utils;
using static BackgroundResourceProcessing.Utils.MathUtil;

namespace BackgroundResourceProcessing.Maths;

internal ref struct OrbitShadow
{
    const double PI = Math.PI;

    internal double semiMajorAxis;
    internal double eccentricity;
    internal double lonAn;
    internal double argPe;
    internal double inclination;

    double a => semiMajorAxis;
    double e => eccentricity;
    double i => inclination;

    double cosi;
    double sini;
    double cosomega;
    double sinomega;
    double tanomega;

    internal double solarDistance;
    internal double planetRadius;
    double slope;

    Vessel vessel;

    CelestialBody star;
    CelestialBody planet;
    CelestialBody parent;

    public void SetReferenceBodies(Vessel vessel, CelestialBody star, CelestialBody planet)
    {
        this.vessel = vessel ?? throw new ArgumentNullException("vessel");
        this.star = star ?? throw new ArgumentNullException("star");
        this.planet = planet;
        this.parent = vessel.orbit.referenceBody;

        var orbit = vessel.orbit;

        semiMajorAxis = orbit.semiMajorAxis;
        eccentricity = orbit.eccentricity;

        if (planet == null)
            solarDistance = vessel.orbit.radius;
        else
            solarDistance = (planet.position - star.position).magnitude;
        planetRadius = parent?.Radius ?? 0;

        ComputeOrbitParameters();
        ComputeDerivedParameters();
    }

    public double ComputeTerminatorUT(out bool inshadow)
    {
        if (ReferenceEquals(star, parent))
        {
            // We are orbiting the star directly. We will never be in shadow
            // unless we switch SOIs.
            inshadow = false;
            return double.PositiveInfinity;
        }

        if (e >= 1.0)
        {
            // We don't support hyperbolic orbits yet.
            inshadow = false;
            return double.PositiveInfinity;
        }
        else
        {
            var tA = ComputeOrbitTerminatorTrueAnomaly(out inshadow);
            if (tA == null)
                return double.PositiveInfinity;
            var trueAnomaly = FMod((double)tA, 2 * PI);

            return vessel.orbit.GetUTforTrueAnomaly(trueAnomaly, 0.0);
        }
    }

    double? ComputeOrbitTerminatorTrueAnomaly(out bool inshadow)
    {
        var tA = vessel.orbit.trueAnomaly;

        // The orbit is edge-on and will not have a terminator
        if (ApproxEqual(cosomega, 0) && ApproxEqual(cosi, 0))
        {
            inshadow = false;
            return null;
        }

        // Get the boundaries of the shadow-ward side of the orbit.
        var (theta1, theta2) = ComputeRegionBoundary();

        if (tA < theta1 && tA + 2 * PI < theta2)
            tA += 2 * PI;
        else if (tA > theta2)
            tA -= 2 * PI;

        bool vshadow = InShadow(tA);
        bool t1shadow = BoundaryInPlanet(theta1);
        bool t2shadow = BoundaryInPlanet(theta2);
        inshadow = vshadow;

        var midpoint = (theta1 + theta2) * 0.5;

        double start = theta1;
        if (tA <= theta1 || tA >= theta2)
        {
            // We will re-enter the planet's shadow but it'll only happen after
            // we have crashed into the planet, so we don't really need to worry
            // about it.
            if (t1shadow)
                return null;
        }
        else
        {
            start = tA;
        }

        if (vshadow)
        {
            // We're just in the planet's shadow. Bisect to find the exit.
            if (!t2shadow)
                return BisectTerminator(tA, theta2);

            // We're on a sub-orbital trajectory and we need to determine
            // whether the orbit goes outside of the planet's shadow before
            // we run into the planet.
            //
            // It is possible for there to be up to 3 different regions
            // of shadow/not shadow ahead of us, though this is difficult
            // to actually do in an orbit as it would require an extremely
            // eccentric orbit. I've elected to ignore that here.
            //
            // Instead, we just maximize the distance to the terminator,
            // starting at the midpoint of anomaly and theta2.

            if (midpoint < tA)
                midpoint = (midpoint + theta2) * 0.5;
            else
                midpoint = (tA + theta2) * 0.5;

            var extreme = FindExtremum(midpoint);

            // There is no maximum/minimum on the range between v and theta2
            // so we know that we do not exit the planet's shadow before we
            // crash.
            if (extreme.location <= tA || extreme.location >= theta2)
                return null;

            // We didn't find a maximum, or the maximum was still in shadow.
            if (extreme.minimum || extreme.value < 0.0)
                return null;

            return BisectTerminator(tA, extreme.location);
        }
        else
        {
            // We're on a sub-orbital trajectory but we are currently outside
            // of the planet's shadow and we will re-enter it before we crash.
            if (t2shadow)
            {
                // NOTE:
                // It is technically possible for a highly elliptical orbit
                // enter and exit the planet's shadow multiple times. I have
                // chosen to ignore that here as it requires a very eccentric
                // orbit and generally bisection will give the right answer
                // in that case anyways.

                return BisectTerminator(theta2, tA);
            }

            // We have a point within the planet's shadow and we need to bisect
            // in order to find the terminator point.
            if (InShadow(midpoint))
                return BisectTerminator(midpoint, start);

            // At this point we need to either find a point in the planet's
            // shadow, or we need to determine that there is no such point.
            //
            // Generally, this means that there is no such point but it may
            // happen on certain orbits.

            var extreme = FindExtremum(midpoint);

            // We could not find a point within the planet's shadow
            if (extreme.value > 0.0)
                return null;

            // Otherwise, bisect to determine the terminator
            return BisectTerminator(extreme.location, tA);
        }
    }

    internal void ComputeDerivedParameters()
    {
        slope = planetRadius / solarDistance;

        PrecomputeTrigFunctions();
    }

    // Compute orbit parameters in the reference frame defined by:
    // - the direction away from the star, and,
    // - the planet's velocity
    void ComputeOrbitParameters()
    {
        // The first thing we do here is to convert the orbit parameters into
        // one that is more convenient for us to work with.
        //
        // We define that frame as follows:
        // - The X vector is the unit vector outwards from the sun through
        //   the center of the planet we are orbiting.
        // - The Y vector is gotten by crossing the sun vector with the
        //   planet velocity.
        // - The Z vector is then gotten by crossing X and Y.
        //
        // The really nice bit about this reference frame is that changes
        // in the sun vector are equivalent to changes in the longitude of
        // the ascending node, and nothing else. This makes some of the
        // calculations we need to do tractable, where they otherwise would
        // not be.
        //
        // The fact that the sun vector is the X axis also simplifies the
        // calculations quite a bit.

        var sun = (parent.position - star.position).normalized;

        // If the planet is directly orbiting the star then we can use its
        // orbit velocity. If not then we use the relative velocity and
        // accept some level of inaccuracy for predictions that are further
        // away.
        Vector3d pvel;
        if (ReferenceEquals(planet.referenceBody, star))
            pvel = planet.GetObtVelocity().normalized;
        else
            pvel = (planet.orbit.GetVel() - star.orbit.GetVel()).normalized;

        // KSP's orbit frames have Z as up, but velocity and world frames have
        // Y as up. The math we use below assumes Z is up (a 0 inclination orbit
        // is on the XY plane) so we convert coordinate spaces here.
        sun = sun.xzy;
        pvel = pvel.xzy;

        var fZ = Vector3d.Cross(sun, pvel);
        var fY = Vector3d.Cross(fZ, sun);

        // We use some quaternion math to easily do the orbital frame
        // conversion. See [0] for a description of how to do this
        //
        // [0]: https://ntrs.nasa.gov/api/citations/19950023025/downloads/19950023025.pdf
        var frame = vessel.orbit.OrbitFrame;
        var frameQ = QuaternionD.FromUnity(frame.Rotation);
        var zupQ = QuaternionD.FromUnity(Planetarium.Zup.Rotation);
        var sunQ = QuaternionD.FromBasis(sun, fY, fZ);

        var rotation = frameQ * zupQ.Inverse() * sunQ.Inverse();

        // This gets us angles in the frame where X is the vector away from the
        // sun, and where inclination is relative to the orbital plane of planet.
        //
        // This is convenient because it means the only change in the orbit due
        // to the planets orbital motion is to the longitude of the ascending
        // node, and nothing else.
        var original = new OrbitAngles()
        {
            lonAn = vessel.orbit.LAN * DEG2RAD,
            inclination = vessel.orbit.inclination * DEG2RAD,
            argPe = vessel.orbit.argumentOfPeriapsis * DEG2RAD,
        };
        var angles = OrbitAngles.FromQuaternion(rotation);

        inclination = angles.inclination;
        lonAn = angles.lonAn;
        argPe = angles.argPe;
    }

    void PrecomputeTrigFunctions()
    {
        cosi = Math.Cos(inclination);
        sini = Math.Sin(inclination);
        cosomega = Math.Cos(lonAn);
        sinomega = Math.Sin(lonAn);
        tanomega = Math.Tan(lonAn);
    }

    // Compute the boundaries of the parts of the orbit that face away from the sun.
    readonly (double, double) ComputeRegionBoundary()
    {
        // This is the solution to x == 0 for the orbit.
        double theta1 = Math.Atan(1.0 / (tanomega * cosi));
        double theta2 = theta1 + PI;

        // Are we moving into or out of the sunwards side of the orbit?
        double dx = -cosomega * Math.Sin(theta1) - cosi * sinomega * Math.Cos(theta1);

        if (dx < 0)
            (theta1, theta2) = (theta2, theta1 + 2 * PI);

        // Convert anomaly to true anomaly
        theta1 -= argPe;
        theta2 -= argPe;

        // Normalize the angles so they are in the region [0, 4*PI] and the
        // start angle is in the region [0, 2*PI]
        if (theta1 >= 2 * PI)
        {
            theta1 -= 2 * PI;
            theta2 -= 2 * PI;
        }
        else if (theta1 < 0)
        {
            theta1 += 2 * PI;
            theta2 += 2 * PI;
        }

        return (theta1, theta2);
    }

    private bool InShadow(double tA)
    {
        double distance = GetDistance(tA, out var x);

        // If we're on the sunward side of the planet then we are not in shadow.
        if (x < 0)
            return false;

        return distance <= 0;
    }

    private bool BoundaryInPlanet(double tA)
    {
        return GetRadius(tA) < planetRadius;
    }

    internal double BisectTerminator(double shadow, double sun)
    {
        double probe;
        for (int i = 0; i < 4; ++i)
        {
            probe = 0.5 * (shadow + sun);
            var distance = GetDistance(probe);

            if (ApproxEqual(distance, 0.0))
                return probe;
            else if (distance < 0)
                shadow = probe;
            else
                sun = probe;
        }

        // Now do some iterations of newton's method to improve our
        // estimate here.
        probe = 0.5 * (shadow + sun);

        for (int i = 0; i < 16; ++i)
        {
            var f = GetDistanceDerivatives(probe);
            var delta = f.x / f.dx;
            probe -= delta;

            if (ApproxEqual(delta, 0.0))
                break;
        }

        return probe;
    }

    internal double GetRadius(double tA)
    {
        return a * (1.0 - e * e) / (1.0 + e * Math.Cos(tA));
    }

    internal double GetDistance(double tA, out double frameX)
    {
        var (sinv, cosv) = MathUtil.SinCos(argPe + tA);
        var cosvw = Math.Cos(tA);

        var r = a * (1.0 - e * e) / (1.0 + e * cosvw);

        var x = r * (cosomega * cosv - cosi * sinomega * sinv);
        var y = r * (-cosi * cosomega * sinv - sinomega * cosv);
        var z = r * (sini * sinv);

        frameX = x;

        var xi = planetRadius + x * slope;
        var delta = Math.Sqrt(y * y + z * z);

        return delta - xi;
    }

    internal double GetDistance(double tA)
    {
        return GetDistance(tA, out var _);
    }

    internal Dual GetDistanceDerivatives(double tA)
    {
        var v = Dual.Variable(tA);

        var (sinv, cosv) = Dual.SinCos(argPe + v);
        var cosvw = Dual.Cos(v);

        var r = a * (1.0 - e * e) / (1.0 + e * cosvw);

        var x = r * (cosomega * cosv - cosi * sinomega * sinv);
        var y = r * (-cosi * cosomega * sinv - sinomega * cosv);
        var z = r * (sini * sinv);

        var xi = planetRadius + x * slope;
        var delta = Dual.Sqrt(y * y + z * z);

        return delta - xi;
    }

    // Compute both the first and second derivatives of distance at the given
    // anomaly value.
    private Dual2 GetDistanceDerivatives2(double tA)
    {
        var v = Dual2.Variable(tA);

        var (sinv, cosv) = Dual2.SinCos(argPe + v);
        var cosvw = Dual2.Cos(v);

        var r = a * (1.0 - e * e) / (1.0 + e * cosvw);

        var x = r * (cosomega * cosv - cosi * sinomega * sinv);
        var y = r * (-cosi * cosomega * sinv - sinomega * cosv);
        var z = r * (sini * sinv);

        var xi = planetRadius + x * slope;
        var delta = Dual2.Sqrt(y * y + z * z);

        return delta - xi;
    }

    internal Extremum FindExtremum(double tA)
    {
        Dual2 f = default;

        for (int i = 0; i < 16; ++i)
        {
            f = GetDistanceDerivatives2(tA);

            var delta = f.dx / f.ddx;
            tA -= delta;

            if (ApproxEqual(delta, 0.0))
                break;
        }

        return new Extremum()
        {
            location = tA,
            value = f.x,
            minimum = f.ddx > 0,
        };
    }

    /// <summary>
    /// Transform the angles representing an orbit into a new coordinate space.
    /// Note that the basis vectors must be orthagonal unit vectors representing
    /// a rotation.
    /// </summary>
    /// <param name="orbit"></param>
    /// <param name="X"></param>
    /// <param name="Y"></param>
    /// <param name="Z"></param>
    /// <returns></returns>
    internal static OrbitAngles TransformOrbitToBasis(
        OrbitAngles orbit,
        Vector3d X,
        Vector3d Y,
        Vector3d Z
    )
    {
        var basis = QuaternionD.ToBasis(X, Y, Z);
        return TransformOrbit(orbit, basis);
    }

    /// <summary>
    /// Transform the orbit elements into the new reference frame described
    /// by <paramref name="rotation"/>.
    /// </summary>
    internal static OrbitAngles TransformOrbit(OrbitAngles orbit, QuaternionD rotation)
    {
        var lonAn = QuaternionD.EulerAngleY(orbit.lonAn);
        var inc = QuaternionD.EulerAngleX(orbit.inclination);
        var argPe = QuaternionD.EulerAngleY(orbit.argPe);

        // Apply the angles, and then apply the inverse basis rotation.
        var q = rotation * lonAn * inc * argPe;
        var (nLonAn, nInc, nArgPe) = q.ToEulerAngles212(orbit.argPe);

        return new()
        {
            lonAn = nLonAn,
            inclination = nInc,
            argPe = nArgPe,
        };
    }

    public struct Terminator
    {
        public bool entering;
        public double time;
    }

    public struct TerminatorAnomaly
    {
        public bool entering;
        public double anomaly;
    }

    internal struct OrbitAngles
    {
        public double lonAn;
        public double argPe;
        public double inclination;

        public static OrbitAngles FromQuaternion(QuaternionD q)
        {
            var (lonAn, inclination, argPe) = q.ToEulerAngles313();
            return new()
            {
                lonAn = lonAn,
                argPe = argPe,
                inclination = inclination,
            };
        }
    }

    internal struct Extremum
    {
        public double location;
        public double value;
        public bool minimum;
    }
}
