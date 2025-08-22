using System;
using BackgroundResourceProcessing.Maths;
using BackgroundResourceProcessing.Utils;
using static System.Math;
using KSPOrbit = Orbit;

namespace BackgroundResourceProcessing.Mathematics;

internal readonly struct Orbit
{
    public readonly Planetarium.CelestialFrame OrbitFrame;

    public readonly double Eccentricity;
    public readonly double Inclination;
    public readonly double SemiMajorAxis;

    public readonly double ArgumentOfPeriapsis;
    public readonly double LAN;

    public readonly double Epoch;
    public readonly double ObTAtEpoch;

    public readonly double MeanMotion;
    public readonly double MeanAnomalyAtEpoch;
    public readonly double GravParameter;

    public readonly double Period;

    /// <summary>
    /// The index of the celestial body that this orbit is relative to.
    ///
    /// Note that it is only relative to the position of the parent body.
    /// It is not transformed by the OrbitFrame othe parent body.
    /// </summary>
    public readonly int? ParentBodyIndex;

    public readonly double SemiLatusRectum;
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

    public readonly double ApR => (1.0 + Eccentricity) * SemiMajorAxis;
    public readonly double PeR => (1.0 - Eccentricity) * SemiMajorAxis;

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
    public readonly double GetObTAtMeanAnomaly(double M) => M / MeanMotion;

    public readonly double GetObTAtTrueAnomaly(double tA) =>
        GetObTAtMeanAnomaly(GetMeanAnomaly(GetEccentricAnomaly(tA)));

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
    public readonly double GetMeanAnomaly(double E)
    {
        if (Eccentricity < 1.0)
            return UtilMath.ClampRadiansTwoPI(E - Eccentricity * Sin(E));
        if (double.IsInfinity(E))
            return E;
        return Eccentricity * Sinh(E) - E;
    }

    public readonly double GetMeanAnomalyAtT(double T) => T * MeanMotion;

    public readonly double GetMeanAnomalyAtUT(double UT)
    {
        double M = MeanAnomalyAtEpoch + MeanMotion * (UT - Epoch);
        if (Eccentricity < 1.0)
            M = UtilMath.ClampRadiansTwoPI(M);
        return M;
    }
    #endregion

    #region True Anomaly
    public double GetTrueAnomalyAtUT(double UT) => GetTrueAnomalyAtT(GetObTAtUT(UT));

    public double GetTrueAnomalyAtT(double T)
    {
        double M = GetMeanAnomalyAtT(T);
        double E = SolveEccentricAnomaly(M);
        if (double.IsNaN(E))
            return E;
        return GetTrueAnomaly(E);
    }

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

    public readonly double GetEccentricAnomalyAtObT(double T) =>
        SolveEccentricAnomaly(T * MeanMotion);

    public readonly double GetEccentricAnomalyAtUT(double UT) =>
        GetEccentricAnomalyAtObT(GetObTAtUT(UT));

    private readonly double SolveEccentricAnomaly(double M)
    {
        if (Eccentricity >= 1.0)
            return SolveEccentricAnomalyHyp(M);
        if (Eccentricity >= 0.8)
            return SolveEccentricAnomalyExtreme(M);
        return SolveEccentricAnomalyStd(M);
    }

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
    public readonly double GetUTAtMeanAnomaly(double M, double UT)
    {
        M -= GetMeanAnomalyAtUT(UT);
        if (Eccentricity < 1.0)
            M = UtilMath.ClampRadiansTwoPI(M);
        return UT + M / MeanMotion;
    }
    #endregion

    #region Radius
    public readonly double GetRadiusAtTrueAnomaly(double tA) =>
        SemiLatusRectum * (1.0 / (1.0 + Eccentricity * Cos(tA)));

    #endregion

    #region Position
    public Vector3d GetRelativePositionAtUT(double UT) =>
        GetRelativePositionAtEccentricAnomaly(GetEccentricAnomalyAtUT(UT));

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

    public Vector3d GetRelativePositionAtTrueAnomaly(double tA)
    {
        var (sinTA, cosTA) = MathUtil.SinCos(tA);
        double radius = SemiLatusRectum / (1.0 + Eccentricity * cosTA);
        return radius * (OrbitFrame.X * cosTA + OrbitFrame.Y * sinTA);
    }
    #endregion

    #region Velocity
    public Vector3d GetOrbitalVelocityAtTrueAnomaly(double tA)
    {
        var (sinTA, cosTA) = MathUtil.SinCos(tA);
        double r = Sqrt(GravParameter / SemiLatusRectum);
        double x = -sinTA * r;
        double y = (cosTA + Eccentricity) * r;

        return OrbitFrame.X * x + OrbitFrame.Y * y;
    }

    public Vector3d GetOrbitalVelocityAtObT(double ObT) =>
        GetOrbitalVelocityAtTrueAnomaly(GetTrueAnomalyAtT(ObT));

    public Vector3d GetOrbitalVelocityAtUT(double UT) => GetOrbitalVelocityAtObT(GetObTAtUT(UT));
    #endregion

    #region Acceleration
    public Vector3d GetAccelerationAtUT(double UT) =>
        GetAccelerationAtTrueAnomaly(GetTrueAnomalyAtUT(UT));

    public Vector3d GetAccelerationAtTrueAnomaly(double tA) =>
        GetAccelerationAtPosition(GetRelativePositionAtTrueAnomaly(tA));

    public Vector3d GetAccelerationAtPosition(Vector3d pos)
    {
        var R = pos.magnitude;
        var invR = 1.0 / R;
        return -pos * invR * invR * invR * GravParameter;
    }
    #endregion

    #region Duals
    public DualVector3 GetRelativePositionAtUT(Dual UT)
    {
        var tA = GetTrueAnomalyAtUT(UT.x);
        var pos = GetRelativePositionAtTrueAnomaly(tA);
        var vel = GetOrbitalVelocityAtTrueAnomaly(tA);

        return new DualVector3(pos, vel * UT.dx);
    }

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
