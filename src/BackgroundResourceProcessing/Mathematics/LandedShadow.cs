using System;
using BackgroundResourceProcessing.BurstSolver;
using BackgroundResourceProcessing.Collections.Burst;
using BackgroundResourceProcessing.Maths;
using BackgroundResourceProcessing.Utils;
using Unity.Burst;
using Unity.Burst.CompilerServices;
using static BackgroundResourceProcessing.Utils.MathUtil;

namespace BackgroundResourceProcessing.Mathematics;

internal readonly struct LandedShadow
{
    const int MaxIter = 128;

    readonly SolarSystem system;
    readonly SystemBody planet;
    readonly SystemBody star;
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
        this.star = system[starIndex];
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

            return term;
        }
    }
    #endregion

    #region Core Computation
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

        return shadow.ComputeTerminator();
    }

    OrbitShadow.Terminator ComputeTerminator()
    {
        // Landed on the star itself -- not in shadow.
        if (StarIsPlanet)
            return OrbitShadow.Terminator.Sun(double.MaxValue);

        var UT = currentUT;
        var startUT = UT;
        var sun = GetSunVectorAtUT(UT).Normalized();

        bool inshadow = Vector3d.Dot(normal, sun) > 0;

        // Planet is tidally locked and is orbiting the star -- no future
        // changes.
        if (
            PlanetIsReference
            && StarIsAncestor
            && (planet.TidallyLocked || planet.RotationPeriod == 0.0)
        )
        {
            return inshadow
                ? OrbitShadow.Terminator.Shadow(double.MaxValue)
                : OrbitShadow.Terminator.Sun(double.MaxValue);
        }

        var cosaxis = Math.Abs(Vector3d.Dot(sun, planet.RotationAxis));

        if (cosLatitude <= cosaxis)
        {
            // We are currently in the polar circle and there won't be a
            // terminator until the planet proceeds around its orbit.
            UT = FindInclinationExit(UT);
            if (!IsFinite(UT))
            {
                return inshadow
                    ? OrbitShadow.Terminator.Shadow(double.MaxValue)
                    : OrbitShadow.Terminator.Sun(double.MaxValue);
            }
        }

        var dot = ComputeDotAt(Dual2.Variable(UT));

        int count = 0;
        double advance = 0.25 * Math.Min(planet.RotationPeriod, planet.Orbit.Period);

        if (Math.Sign(dot.x) * Math.Sign(dot.dx) > 0)
        {
            // Newton's method wants to find a zero in the past. Find the next
            // extreme and flip to the other side.
            if (Math.Abs(dot.dx / dot.ddx) >= planet.SolarDayLength / 4)
                UT += advance;

            double extremeUT = FindMiddayOrMidnight(UT);

            if (extremeUT < startUT)
            {
                return inshadow
                    ? OrbitShadow.Terminator.Shadow(UT + planet.SolarDayLength / 4)
                    : OrbitShadow.Terminator.Sun(UT + planet.SolarDayLength / 4);
            }

            UT += 2 * (extremeUT - UT);
            dot = ComputeDotAt(Dual2.Variable(UT));
        }

        while (Math.Abs(dot.x / dot.dx) >= planet.SolarDayLength / 4)
        {
            if (count > 8)
            {
                return inshadow
                    ? OrbitShadow.Terminator.Shadow(UT)
                    : OrbitShadow.Terminator.Sun(UT);
            }
            count += 1;

            UT += advance;
            dot = ComputeDotAt(Dual2.Variable(UT));
        }

        var terminatorUT = FindTerminator(UT);
        return inshadow
            ? OrbitShadow.Terminator.Shadow(terminatorUT)
            : OrbitShadow.Terminator.Sun(terminatorUT);
    }
    #endregion

    #region Newton Solvers
    double FindTerminator(double startUT)
    {
        double probe = startUT;

        for (int i = 0; i < MaxIter; ++i)
        {
            var dot = ComputeDotAt(Dual.Variable(probe));
            var delta = dot.x / dot.dx;
            probe -= delta;

            if (ApproxEqual(delta, 0.0))
                break;
        }

        return probe;
    }

    double FindMiddayOrMidnight(double startUT)
    {
        double probe = startUT;

        for (int i = 0; i < MaxIter; ++i)
        {
            var dot = ComputeDotAt(Dual2.Variable(probe));
            var delta = dot.dx / dot.ddx;
            probe -= delta;

            if (ApproxEqual(delta, 0.0))
                break;
        }

        return probe;
    }

    double FindInclinationExit(double startUT)
    {
        var diff = GetRelativeInclinationAngle(Dual.Variable(startUT));

        if (ApproxEqual(diff.dx, 0.0, 1e-9))
        {
            // We are almost at a maximum. Add an eighth-orbit and try then.
            startUT += system[referenceIndex].Orbit.Period * 0.125;
        }
        else if (Math.Sign(diff.x) * Math.Sign(diff.dx) > 0)
        {
            // Newton's method is trying to find a crossing in the past.
            // Find the maximum and jump to the other side.
            double maxUT = FindInclinationMaximum(startUT);

            if (maxUT < startUT)
                return double.MaxValue;

            startUT += (maxUT - startUT) * 2;
        }

        return FindInclinationTerminator(startUT);
    }

    double FindInclinationMaximum(double startUT)
    {
        double probe = startUT;

        for (int i = 0; i < MaxIter; ++i)
        {
            var UT = Dual2.Variable(probe);
            var sun = GetSunVectorAtUT(UT);
            var cos = Dual2.Abs(Dual2Vector3.Dot(sun, new(planet.RotationAxis)));
            var diff = cos - cosLatitude;

            if (!IsFinite(cos.dx))
                break;

            double delta = diff.dx / diff.ddx;
            probe -= delta;
            if (Math.Abs(delta) < 1.0)
                break;
        }

        return probe;
    }

    double FindInclinationTerminator(double startUT)
    {
        double probe = startUT;

        for (int i = 0; i < MaxIter; ++i)
        {
            var UT = Dual.Variable(probe);
            var sun = GetSunVectorAtUT(UT);
            var cos = Dual.Abs(DualVector3.Dot(sun, new(planet.RotationAxis)));
            var diff = cos - cosLatitude;

            double delta = diff.x / diff.dx;
            probe -= delta;
            if (Math.Abs(delta) < 1.0)
                break;
        }

        return probe;
    }

    Dual GetRelativeInclinationAngle(Dual UT)
    {
        var sun = GetSunVectorAtUT(UT).Normalized();
        var cos = Dual.Abs(DualVector3.Dot(sun, new(planet.RotationAxis)));
        return cos - cosLatitude;
    }
    #endregion

    #region Dot Product & Sun Vector
    Dual ComputeDotAt(Dual UT)
    {
        var pUT = UT - currentUT;
        var theta = planet.AngularVelocity * pUT;
        if (!planet.Rotates)
            theta = new(0);
        var q = DualQuaternion.FromAngleAxis(theta, new(planet.RotationAxis));

        var sun = GetSunVectorAtUT(UT);
        var normal = q.Rotate(new(this.normal));

        return DualVector3.Dot(sun, normal);
    }

    Dual2 ComputeDotAt(Dual2 UT)
    {
        var pUT = UT - currentUT;
        var theta = planet.AngularVelocity * pUT;
        if (!planet.Rotates)
            theta = new(0);
        var q = Dual2Quaternion.FromAngleAxis(theta, new(planet.RotationAxis));

        var sun = GetSunVectorAtUT(UT);
        var normal = q.Rotate(new(this.normal));

        return Dual2Vector3.Dot(sun, normal);
    }

    Vector3d GetSunVectorAtUT(double UT)
    {
        var ppos = system.GetPositionAtUT(in planet, UT);
        var spos = system.GetPositionAtUT(in star, UT);
        return ppos - spos;
    }

    DualVector3 GetSunVectorAtUT(Dual UT)
    {
        var ppos = system.GetPositionAtUT(in planet, UT);
        var spos = system.GetPositionAtUT(in star, UT);
        return ppos - spos;
    }

    Dual2Vector3 GetSunVectorAtUT(Dual2 UT)
    {
        var ppos = system.GetPositionAtUT(in planet, UT);
        var spos = system.GetPositionAtUT(in star, UT);
        return ppos - spos;
    }
    #endregion
}

[BurstCompile]
internal static class LandedShadowBurst
{
    // This needs to be in a class or else we get a segfault somewhere within
    // mono when calling the burst function pointer.
    [BurstCompile(FloatMode = FloatMode.Fast)]
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
