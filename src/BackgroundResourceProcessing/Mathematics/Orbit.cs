using System;
using BackgroundResourceProcessing.Maths;
using BackgroundResourceProcessing.Utils;
using static System.Math;
using KSPOrbit = Orbit;

namespace BackgroundResourceProcessing.Mathematics;

/// <summary>
/// Represents an orbital trajectory using Keplerian orbital elements.
/// </summary>
internal readonly struct Orbit
{
    /// <summary>
    /// The coordinate frame for the orbit's orientation in space.
    /// </summary>
    public readonly Planetarium.CelestialFrame OrbitFrame;

    /// <summary>
    /// Eccentricity of the orbit (0 = circular, 0-1 = elliptical, 1 = parabolic, >1 = hyperbolic).
    /// </summary>
    public readonly double Eccentricity;
    
    /// <summary>
    /// Inclination of the orbital plane relative to the reference plane (in radians).
    /// </summary>
    public readonly double Inclination;
    
    /// <summary>
    /// Semi-major axis - half the longest diameter of the ellipse.
    /// </summary>
    public readonly double SemiMajorAxis;

    /// <summary>
    /// Argument of periapsis - angle from ascending node to periapsis (in radians).
    /// </summary>
    public readonly double ArgumentOfPeriapsis;
    
    /// <summary>
    /// Longitude of Ascending Node - angle from reference direction to ascending node (in radians).
    /// </summary>
    public readonly double LAN;

    /// <summary>
    /// Epoch time when orbital elements are valid.
    /// </summary>
    public readonly double Epoch;
    
    /// <summary>
    /// Orbital time at epoch.
    /// </summary>
    public readonly double ObTAtEpoch;

    /// <summary>
    /// Mean motion - average angular velocity of the orbiting body (radians per second).
    /// </summary>
    public readonly double MeanMotion;
    
    /// <summary>
    /// Mean anomaly at epoch - average angular position at epoch time.
    /// </summary>
    public readonly double MeanAnomalyAtEpoch;
    
    /// <summary>
    /// Gravitational parameter (GM) of the central body.
    /// </summary>
    public readonly double GravParameter;

    /// <summary>
    /// Orbital period for elliptical orbits.
    /// </summary>
    public readonly double Period;

    /// <summary>
    /// The index of the celestial body that this orbit is relative to.
    ///
    /// Note that it is only relative to the position of the parent body.
    /// It is not transformed by the OrbitFrame othe parent body.
    /// </summary>
    public readonly int? ParentBodyIndex;

    /// <summary>
    /// Semi-latus rectum - parameter defining the size of the orbit perpendicular to the major axis.
    /// </summary>
    public readonly double SemiLatusRectum;
    
    /// <summary>
    /// Semi-minor axis - half the shortest diameter of the ellipse.
    /// </summary>
    public readonly double SemiMinorAxis
    {
        get
        {
            var num = Eccentricity * Eccentricity - 1.0;
            if (Eccentricity < 1.0)
                num = -num;

            return SemiMajorAxis * Sqrt(num);
        }
    }

    /// <summary>
    /// Apoapsis radius - maximum distance from the central body.
    /// </summary>
    public readonly double ApR => (1.0 + Eccentricity) * SemiMajorAxis;
    
    /// <summary>
    /// Periapsis radius - minimum distance from the central body.
    /// </summary>
    public readonly double PeR => (1.0 - Eccentricity) * SemiMajorAxis;

    /// <summary>
    /// Initializes a new orbit from KSP's Orbit class.
    /// </summary>
    /// <param name="orbit">Source KSP orbit object</param>
    /// <param name="parentBodyIndex">Index of the parent celestial body</param>
    public Orbit(KSPOrbit orbit, int parentBodyIndex)
    {
        Eccentricity = orbit.eccentricity;
        Inclination = orbit.inclination;
        SemiMajorAxis = orbit.semiMajorAxis;
        ArgumentOfPeriapsis = orbit.argumentOfPeriapsis;
        LAN = orbit.LAN;
        Epoch = orbit.epoch;
        ObTAtEpoch = orbit.ObTAtEpoch;
        MeanMotion = orbit.meanMotion;
        MeanAnomalyAtEpoch = orbit.meanAnomalyAtEpoch;
        GravParameter = orbit.referenceBody.gravParameter;
        ParentBodyIndex = parentBodyIndex;

        Period = 2.0 * PI / MeanMotion;
        SemiLatusRectum = SemiMajorAxis * (1.0 - Eccentricity * Eccentricity);
    }

    #region ObT
    /// <summary>
    /// Gets orbital time at a given mean anomaly.
    /// </summary>
    /// <param name="M">Mean anomaly in radians</param>
    /// <returns>Orbital time</returns>
    public readonly double GetObTAtMeanAnomaly(double M) => M / MeanMotion;

    /// <summary>
    /// Gets orbital time at a given true anomaly.
    /// </summary>
    /// <param name="tA">True anomaly in radians</param>
    /// <returns>Orbital time</returns>
    public readonly double GetObTAtTrueAnomaly(double tA) =>
        GetObTAtMeanAnomaly(GetMeanAnomaly(GetEccentricAnomaly(tA)));

    /// <summary>
    /// Gets orbital time at a given universal time.
    /// </summary>
    /// <param name="UT">Universal time</param>
    /// <returns>Orbital time</returns>
    public readonly double GetObTAtUT(double UT)
    {
        double ObT;

        if (Eccentricity < 1.0)
        {
            if (double.IsInfinity(UT))
                return double.NaN;

            ObT = (UT - Epoch + ObTAtEpoch) % Period;
            if (ObT > Period * 0.5)
                ObT -= Period;
        }
        else
        {
            if (double.IsInfinity(UT))
                return UT;

            ObT = ObTAtEpoch + (UT - Epoch);
        }

        return ObT;
    }
    #endregion

    #region Mean Anomaly
    /// <summary>
    /// Calculates mean anomaly from eccentric anomaly.
    /// </summary>
    /// <param name="E">Eccentric anomaly in radians</param>
    /// <returns>Mean anomaly in radians</returns>
    public readonly double GetMeanAnomaly(double E)
    {
        if (Eccentricity < 1.0)
            return UtilMath.ClampRadiansTwoPI(E - Eccentricity * Sin(E));
        if (double.IsInfinity(E))
            return E;
        return Eccentricity * Sinh(E) - E;
    }

    /// <summary>
    /// Gets mean anomaly at a given orbital time.
    /// </summary>
    /// <param name="T">Orbital time</param>
    /// <returns>Mean anomaly in radians</returns>
    public readonly double GetMeanAnomalyAtT(double T) => T * MeanMotion;

    /// <summary>
    /// Gets mean anomaly at a given universal time.
    /// </summary>
    /// <param name="UT">Universal time</param>
    /// <returns>Mean anomaly in radians</returns>
    public readonly double GetMeanAnomalyAtUT(double UT)
    {
        double M = MeanAnomalyAtEpoch + MeanMotion * (UT - Epoch);
        if (Eccentricity < 1.0)
            M = UtilMath.ClampRadiansTwoPI(M);
        return M;
    }
    #endregion

    #region True Anomaly
    /// <summary>
    /// Gets true anomaly at a given universal time.
    /// </summary>
    /// <param name="UT">Universal time</param>
    /// <returns>True anomaly in radians</returns>
    public double GetTrueAnomalyAtUT(double UT) => GetTrueAnomalyAtT(GetObTAtUT(UT));

    /// <summary>
    /// Gets true anomaly at a given orbital time.
    /// </summary>
    /// <param name="T">Orbital time</param>
    /// <returns>True anomaly in radians</returns>
    public double GetTrueAnomalyAtT(double T)
    {
        double M = GetMeanAnomalyAtT(T);
        double E = SolveEccentricAnomaly(M);
        if (double.IsNaN(E))
            return E;
        return GetTrueAnomaly(E);
    }

    /// <summary>
    /// Calculates true anomaly from eccentric anomaly.
    /// </summary>
    /// <param name="E">Eccentric anomaly in radians</param>
    /// <returns>True anomaly in radians</returns>
    public double GetTrueAnomaly(double E)
    {
        if (Eccentricity < 1.0)
        {
            var (y, x) = MathUtil.SinCos(E * 0.5);
            return 2.0 * Atan2(Sqrt(1.0 + Eccentricity) * y, Sqrt(1.0 - Eccentricity) * x);
        }

        if (double.IsPositiveInfinity(E))
            return Acos(-1.0 / Eccentricity);
        if (double.IsNegativeInfinity(E))
            return -Acos(-1.0 / Eccentricity);

        {
            double y = Sinh(E * 0.5);
            double x = Cosh(E * 0.5);
            return 2.0 * Atan2(Sqrt(Eccentricity + 1.0) * y, Sqrt(Eccentricity - 1.0) * x);
        }
    }
    #endregion

    #region Eccentric Anomaly
    /// <summary>
    /// Calculates eccentric anomaly from true anomaly.
    /// </summary>
    /// <param name="tA">True anomaly in radians</param>
    /// <returns>Eccentric anomaly in radians</returns>
    public readonly double GetEccentricAnomaly(double tA)
    {
        var (sinTA, cosTA) = MathUtil.SinCos(tA);

        if (Eccentricity < 1.0)
        {
            double y = Math.Sqrt(1.0 - Eccentricity) * sinTA;
            double x = Math.Sqrt(1.0 + Eccentricity) * cosTA;
            return 2.0 * Math.Atan2(y, x);
        }

        double num = Math.Sqrt((Eccentricity - 1.0) / (Eccentricity + 1.0)) * sinTA / cosTA;
        if (num >= 1.0)
            return double.PositiveInfinity;
        if (num <= -1.0)
            return double.NegativeInfinity;

        return Math.Log((1.0 + num) / (1.0 - num));
    }

    /// <summary>
    /// Gets eccentric anomaly at a given orbital time.
    /// </summary>
    /// <param name="T">Orbital time</param>
    /// <returns>Eccentric anomaly in radians</returns>
    public readonly double GetEccentricAnomalyAtObT(double T) =>
        SolveEccentricAnomaly(T * MeanMotion);

    /// <summary>
    /// Gets eccentric anomaly at a given universal time.
    /// </summary>
    /// <param name="UT">Universal time</param>
    /// <returns>Eccentric anomaly in radians</returns>
    public readonly double GetEccentricAnomalyAtUT(double UT) =>
        GetEccentricAnomalyAtObT(GetObTAtUT(UT));

    /// <summary>
    /// Solves Kepler's equation to find eccentric anomaly from mean anomaly.
    /// </summary>
    /// <param name="M">Mean anomaly in radians</param>
    /// <returns>Eccentric anomaly in radians</returns>
    private readonly double SolveEccentricAnomaly(double M)
    {
        if (Eccentricity >= 1.0)
            return SolveEccentricAnomalyHyp(M);
        if (Eccentricity >= 0.8)
            return SolveEccentricAnomalyExtreme(M);
        return SolveEccentricAnomalyStd(M);
    }

    /// <summary>
    /// Solves eccentric anomaly for hyperbolic orbits using Newton's method.
    /// </summary>
    /// <param name="M">Mean anomaly in radians</param>
    /// <returns>Eccentric anomaly in radians</returns>
    private readonly double SolveEccentricAnomalyHyp(double M)
    {
        const double MaxError = 1e-7;

        if (double.IsInfinity(M))
            return M;

        double delta;
        double num = 2.0 * M / Eccentricity;
        double E = Math.Log(Math.Sqrt(1.0 + num * num) + num);

        do
        {
            delta = (Eccentricity * Math.Sinh(E) - E - M) / (Eccentricity * Math.Cosh(E) - 1.0);
            E -= delta;
        } while (Math.Abs(delta) > MaxError);

        return E;
    }

    /// <summary>
    /// Solves eccentric anomaly for high eccentricity orbits using Laguerre's method.
    /// </summary>
    /// <param name="M">Mean anomaly in radians</param>
    /// <returns>Eccentric anomaly in radians</returns>
    private readonly double SolveEccentricAnomalyExtreme(double M)
    {
        const int MaxIterations = 8;

        double E = M + 0.85 * Eccentricity * Math.Sin(Math.Sin(M));
        for (int i = 0; i < MaxIterations; ++i)
        {
            double num2 = Eccentricity * Math.Sin(E);
            double num3 = Eccentricity * Math.Cos(E);
            double num4 = E - num2 - M;
            double num5 = 1.0 - num3;
            double num6 = num2;
            E +=
                -5.0
                * num4
                / (
                    num5
                    + Math.Sign(num5) * Math.Sqrt(Math.Abs(16.0 * num5 * num5 - 20.0 * num4 * num6))
                );
        }

        return E;
    }

    /// <summary>
    /// Solves eccentric anomaly for standard eccentricity orbits using Newton-Raphson method.
    /// </summary>
    /// <param name="M">Mean anomaly in radians</param>
    /// <returns>Eccentric anomaly in radians</returns>
    private readonly double SolveEccentricAnomalyStd(double M)
    {
        const double MaxError = 1e-7;

        double delta;
        double E = M + Eccentricity * (Math.Sin(M) + 0.5 * Eccentricity * Math.Sin(2.0 * M));
        do
        {
            double newM = E - Eccentricity * Math.Sin(E);
            delta = (M - newM) / (1.0 - Eccentricity * Math.Cos(E));
            E += delta;
        } while (Math.Abs(delta) > MaxError);

        return E;
    }
    #endregion

    #region UT
    /// <summary>
    /// Gets universal time at a given mean anomaly relative to a reference time.
    /// </summary>
    /// <param name="M">Mean anomaly in radians</param>
    /// <param name="UT">Reference universal time</param>
    /// <returns>Universal time</returns>
    public readonly double GetUTAtMeanAnomaly(double M, double UT)
    {
        M -= GetMeanAnomalyAtUT(UT);
        if (Eccentricity < 1.0)
            M = UtilMath.ClampRadiansTwoPI(M);
        return UT + M / MeanMotion;
    }
    #endregion

    #region Radius
    /// <summary>
    /// Gets orbital radius at a given true anomaly.
    /// </summary>
    /// <param name="tA">True anomaly in radians</param>
    /// <returns>Distance from central body</returns>
    public readonly double GetRadiusAtTrueAnomaly(double tA) =>
        SemiLatusRectum * (1.0 / (1.0 + Eccentricity * Cos(tA)));

    #endregion

    #region Position
    /// <summary>
    /// Gets 3D position vector at a given universal time.
    /// </summary>
    /// <param name="UT">Universal time</param>
    /// <returns>Position vector relative to central body</returns>
    public Vector3d GetRelativePositionAtUT(double UT) =>
        GetRelativePositionAtEccentricAnomaly(GetEccentricAnomalyAtUT(UT));

    /// <summary>
    /// Gets 3D position vector at a given eccentric anomaly.
    /// </summary>
    /// <param name="E">Eccentric anomaly in radians</param>
    /// <returns>Position vector relative to central body</returns>
    public Vector3d GetRelativePositionAtEccentricAnomaly(double E)
    {
        double x = 0.0;
        double y = 0.0;

        if (Eccentricity < 1.0)
        {
            x = SemiMajorAxis * (Cos(E) - Eccentricity);
            y = SemiMajorAxis * Sqrt(1.0 - Eccentricity * Eccentricity) * Sin(E);
        }
        else if (Eccentricity > 1.0)
        {
            x = -SemiMajorAxis * (Eccentricity - Cosh(E));
            y = -SemiMajorAxis * Sqrt(Eccentricity * Eccentricity - 1.0) * Sinh(E);
        }

        return OrbitFrame.X * x + OrbitFrame.Y * y;
    }

    /// <summary>
    /// Gets 3D position vector at a given true anomaly.
    /// </summary>
    /// <param name="tA">True anomaly in radians</param>
    /// <returns>Position vector relative to central body</returns>
    public Vector3d GetRelativePositionAtTrueAnomaly(double tA)
    {
        var (sinTA, cosTA) = MathUtil.SinCos(tA);
        double radius = SemiLatusRectum / (1.0 + Eccentricity * cosTA);
        return radius * (OrbitFrame.X * cosTA + OrbitFrame.Y * sinTA);
    }
    #endregion

    #region Velocity
    /// <summary>
    /// Gets orbital velocity vector at a given true anomaly.
    /// </summary>
    /// <param name="tA">True anomaly in radians</param>
    /// <returns>Velocity vector in orbital frame</returns>
    public Vector3d GetOrbitalVelocityAtTrueAnomaly(double tA)
    {
        var (sinTA, cosTA) = MathUtil.SinCos(tA);
        double r = Sqrt(GravParameter / SemiLatusRectum);
        double x = -sinTA * r;
        double y = (cosTA + Eccentricity) * r;

        return OrbitFrame.X * x + OrbitFrame.Y * y;
    }

    /// <summary>
    /// Gets orbital velocity vector at a given orbital time.
    /// </summary>
    /// <param name="ObT">Orbital time</param>
    /// <returns>Velocity vector in orbital frame</returns>
    public Vector3d GetOrbitalVelocityAtObT(double ObT) =>
        GetOrbitalVelocityAtTrueAnomaly(GetTrueAnomalyAtT(ObT));

    /// <summary>
    /// Gets orbital velocity vector at a given universal time.
    /// </summary>
    /// <param name="UT">Universal time</param>
    /// <returns>Velocity vector in orbital frame</returns>
    public Vector3d GetOrbitalVelocityAtUT(double UT) => GetOrbitalVelocityAtObT(GetObTAtUT(UT));
    #endregion

    #region Acceleration
    /// <summary>
    /// Gets gravitational acceleration vector at a given universal time.
    /// </summary>
    /// <param name="UT">Universal time</param>
    /// <returns>Acceleration vector due to gravity</returns>
    public Vector3d GetAccelerationAtUT(double UT) =>
        GetAccelerationAtTrueAnomaly(GetTrueAnomalyAtUT(UT));

    /// <summary>
    /// Gets gravitational acceleration vector at a given true anomaly.
    /// </summary>
    /// <param name="tA">True anomaly in radians</param>
    /// <returns>Acceleration vector due to gravity</returns>
    public Vector3d GetAccelerationAtTrueAnomaly(double tA) =>
        GetAccelerationAtPosition(GetRelativePositionAtTrueAnomaly(tA));

    /// <summary>
    /// Gets gravitational acceleration vector at a given position.
    /// </summary>
    /// <param name="pos">Position vector</param>
    /// <returns>Acceleration vector due to gravity</returns>
    public Vector3d GetAccelerationAtPosition(Vector3d pos)
    {
        var R = pos.magnitude;
        var invR = 1.0 / R;
        return -pos * invR * invR * invR * GravParameter;
    }
    #endregion

    #region Duals
    /// <summary>
    /// Gets position and velocity using dual numbers for automatic differentiation.
    /// </summary>
    /// <param name="UT">Universal time as dual number</param>
    /// <returns>Position and velocity as dual vector</returns>
    public DualVector3 GetRelativePositionAtUT(Dual UT)
    {
        var tA = GetTrueAnomalyAtUT(UT.x);
        var pos = GetRelativePositionAtTrueAnomaly(tA);
        var vel = GetOrbitalVelocityAtTrueAnomaly(tA);

        return new DualVector3(pos, vel * UT.dx);
    }

    /// <summary>
    /// Gets position, velocity, and acceleration using second-order dual numbers.
    /// </summary>
    /// <param name="UT">Universal time as second-order dual number</param>
    /// <returns>Position, velocity, and acceleration as second-order dual vector</returns>
    public Dual2Vector3 GetRelativePositionAtUT(Dual2 UT)
    {
        var tA = GetTrueAnomalyAtUT(UT.x);
        var pos = GetRelativePositionAtTrueAnomaly(tA);
        var vel = GetOrbitalVelocityAtTrueAnomaly(tA);
        var acc = GetAccelerationAtTrueAnomaly(tA);

        return new(pos, vel * UT.dx, acc * UT.dx * UT.dx + vel * UT.ddx);
    }
    #endregion
}
