using System;
using BackgroundResourceProcessing.Utils;

namespace BackgroundResourceProcessing.Maths;

internal static class OrbitMath
{
    /// <summary>
    /// Get the true position of the body in this orbit in world space at UT.
    /// </summary>
    /// <param name="body"></param>
    /// <param name="UT"></param>
    /// <returns></returns>
    public static Vector3d GetTruePositionAtUT(this CelestialBody body, double UT)
    {
        if (body.orbit == null)
            return body.position;

        return body.orbit.getTruePositionAtUT(UT);
    }

    /// <summary>
    /// Get the true position of the body in this orbit in world space at UT.
    /// </summary>
    /// <param name="body"></param>
    /// <param name="UT"></param>
    /// <returns></returns>
    public static DualVector3 GetTruePositionAtUT(this CelestialBody body, Dual UT)
    {
        if (body.orbit == null)
            return new(body.position);

        var parent = GetTruePositionAtUT(body.orbit?.referenceBody, UT);

        var tA = body.orbit.TrueAnomalyAtUT(UT.x);
        var pos = body.orbit.getRelativePositionFromTrueAnomaly(tA).xzy;
        var vel = body.orbit.getOrbitalVelocityAtTrueAnomaly(tA).xzy;

        return parent + new DualVector3(pos, vel * UT.dx);
    }

    /// <summary>
    /// Get the true position of the body in this orbit in world space at UT.
    /// </summary>
    /// <param name="body"></param>
    /// <param name="UT"></param>
    /// <returns></returns>
    public static Dual2Vector3 GetTruePositionAtUT(this CelestialBody body, Dual2 UT)
    {
        if (body.orbit == null)
            return new(body.position);

        var parent = GetTruePositionAtUT(body.orbit?.referenceBody, UT);

        var tA = body.orbit.TrueAnomalyAtUT(UT.x);
        var pos = body.orbit.getRelativePositionFromTrueAnomaly(tA).xzy;
        var vel = body.orbit.getOrbitalVelocityAtTrueAnomaly(tA).xzy;
        var acc = body.orbit.GetAccelerationAtPosition(pos);

        return parent + new Dual2Vector3(pos, vel * UT.dx, acc * (UT.dx * UT.dx) + vel * UT.ddx);
    }

    private static Vector3d GetAccelerationAtPosition(this Orbit orbit, Vector3d pos)
    {
        var R = pos.magnitude;
        var invR = 1.0 / R;
        return -pos * invR * invR * invR * orbit.referenceBody.gravParameter;
    }
}
