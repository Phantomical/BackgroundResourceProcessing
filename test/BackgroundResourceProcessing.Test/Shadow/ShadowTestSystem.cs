using System;
using BackgroundResourceProcessing.Mathematics;
using BackgroundResourceProcessing.Shadow;

namespace BackgroundResourceProcessing.Test.Shadow;

using Orbit = BackgroundResourceProcessing.Mathematics.Orbit;

/// <summary>
/// A synthetic copy of part of the stock Kerbol system, built without any
/// <see cref="CelestialBody"/> so that the shadow math can be tested in any
/// scene.
/// </summary>
internal static class ShadowTestSystem
{
    public const int Kerbol = 0;
    public const int Kerbin = 1;
    public const int Mun = 2;
    public const int Moho = 3;
    public const int Jool = 4;
    public const int Laythe = 5;

    const double Deg = Math.PI / 180.0;

    public const double KerbolMu = 1.1723328e18;

    public readonly struct BodyInfo(string name, double radius, double mu, double soi)
    {
        public readonly string Name = name;
        public readonly double Radius = radius;
        public readonly double Mu = mu;
        public readonly double SOI = soi;
    }

    public static readonly BodyInfo[] Info =
    [
        new("Kerbol", 261_600_000, KerbolMu, double.PositiveInfinity),
        new("Kerbin", 600_000, 3.5316e12, 84_159_286),
        new("Mun", 200_000, 6.5138398e10, 2_429_559),
        new("Moho", 250_000, 1.6860938e11, 9_646_663),
        new("Jool", 6_000_000, 2.82528e14, 2_455_985_200),
        new("Laythe", 500_000, 1.962e12, 3_723_645),
    ];

    /// <summary>
    /// Build the body array. A new array is returned each time so tests can't
    /// interfere with each other.
    /// </summary>
    public static SystemBody[] Create()
    {
        var bodies = new SystemBody[Info.Length];

        bodies[Kerbol] = new SystemBody
        {
            HasOrbit = false,
            Position = Vector3d.zero,
            Index = Kerbol,
            Radius = Info[Kerbol].Radius,
        };
        bodies[Kerbin] = Body(
            Kerbin,
            MakeOrbit(Kerbol, KerbolMu, 13_599_840_256, 0, 0, 0, 0, 3.14)
        );
        bodies[Mun] = Body(Mun, MakeOrbit(Kerbin, Info[Kerbin].Mu, 12_000_000, 0, 0, 0, 0, 1.7));
        bodies[Moho] = Body(
            Moho,
            MakeOrbit(Kerbol, KerbolMu, 5_263_138_304, 0.2, 7 * Deg, 70 * Deg, 15 * Deg, 3.14)
        );
        bodies[Jool] = Body(
            Jool,
            MakeOrbit(Kerbol, KerbolMu, 68_773_560_320, 0.05, 1.304 * Deg, 52 * Deg, 0, 0.1)
        );
        bodies[Laythe] = Body(Laythe, MakeOrbit(Jool, Info[Jool].Mu, 27_184_000, 0, 0, 0, 0, 3.14));

        return bodies;
    }

    static SystemBody Body(int index, Orbit orbit) =>
        new()
        {
            HasOrbit = true,
            Orbit = orbit,
            Index = index,
            Radius = Info[index].Radius,
        };

    /// <summary>
    /// Build an orbit from classical elements. Angles are in radians and the
    /// frame is KSP's internal Z-up frame.
    /// </summary>
    public static Orbit MakeOrbit(
        int parent,
        double mu,
        double sma,
        double ecc,
        double inc,
        double lan,
        double argPe,
        double meanAnomalyAtEpoch,
        double epoch = 0.0
    )
    {
        var (sO, cO) = (Math.Sin(lan), Math.Cos(lan));
        var (sw, cw) = (Math.Sin(argPe), Math.Cos(argPe));
        var (si, ci) = (Math.Sin(inc), Math.Cos(inc));

        var frame = new Planetarium.CelestialFrame
        {
            X = new Vector3d(cO * cw - sO * sw * ci, sO * cw + cO * sw * ci, sw * si),
            Y = new Vector3d(-cO * sw - sO * cw * ci, -sO * sw + cO * cw * ci, cw * si),
            Z = new Vector3d(sO * si, -cO * si, ci),
        };

        var n = Math.Sqrt(mu / Math.Abs(sma * sma * sma));
        return new Orbit
        {
            OrbitFrame = frame,
            Eccentricity = ecc,
            Inclination = inc,
            SemiMajorAxis = sma,
            ArgumentOfPeriapsis = argPe,
            LAN = lan,
            Epoch = epoch,
            MeanAnomalyAtEpoch = meanAnomalyAtEpoch,
            ObTAtEpoch = meanAnomalyAtEpoch / n,
            MeanMotion = n,
            GravParameter = mu,
            Period = 2.0 * Math.PI / n,
            ParentBodyIndex = parent,
            SemiLatusRectum = sma * (1.0 - ecc * ecc),
        };
    }

    /// <summary>
    /// Build an orbit around <paramref name="parent"/> from its periapsis
    /// radius instead of its semi-major axis. This also works for hyperbolic
    /// orbits.
    /// </summary>
    public static Orbit MakeOrbitPe(
        SystemBody[] bodies,
        int parent,
        double pe,
        double ecc,
        double inc,
        double lan,
        double argPe,
        double meanAnomalyAtEpoch,
        double epoch = 0.0
    ) =>
        MakeOrbit(
            parent,
            Info[parent].Mu,
            pe / (1.0 - ecc),
            ecc,
            inc,
            lan,
            argPe,
            meanAnomalyAtEpoch,
            epoch
        );

    public static Vector3d GetPosition(SystemBody[] bodies, in Orbit orbit, double UT)
    {
        var pos = orbit.GetRelativePositionAtUT(UT);
        var index = orbit.ParentBodyIndex;
        while (index is int i)
        {
            ref var body = ref bodies[i];
            if (!body.HasOrbit)
                return pos + body.Position;

            pos += body.Orbit.GetRelativePositionAtUT(UT);
            index = body.Orbit.ParentBodyIndex;
        }

        return pos;
    }

    public static Vector3d GetPosition(SystemBody[] bodies, int body, double UT)
    {
        ref var b = ref bodies[body];
        if (!b.HasOrbit)
            return b.Position;
        return GetPosition(bodies, in b.Orbit, UT);
    }

    /// <summary>
    /// The direction that sunlight travels from the star to the vessel's
    /// parent body at <paramref name="UT"/>, as a unit vector.
    /// </summary>
    public static Vector3d GetSunDirection(
        SystemBody[] bodies,
        in Orbit vessel,
        int star,
        double UT
    )
    {
        var parent = vessel.ParentBodyIndex ?? throw new ArgumentException("orbit has no parent");
        var sun = GetPosition(bodies, parent, UT) - GetPosition(bodies, star, UT);
        return sun / sun.magnitude;
    }

    /// <summary>
    /// Run the solver under test on either the managed or the Burst path.
    /// </summary>
    public static OrbitShadow.Terminator Compute(
        SystemBody[] bodies,
        in Orbit vessel,
        double UT,
        bool burst = false,
        int star = Kerbol
    )
    {
        var settings = DebugSettings.Instance;
        var saved = settings.EnableBurst;
        settings.EnableBurst = burst;
        try
        {
            return OrbitShadow.ComputeOrbitTerminator(bodies, vessel, star, UT);
        }
        finally
        {
            settings.EnableBurst = saved;
        }
    }
}

/// <summary>
/// A slow but simple brute-force reference for orbit shadow timing.
/// </summary>
///
/// <remarks>
/// <para>
/// It uses the same shadow model as <see cref="OrbitShadow"/>: a point-source
/// shadow cone with its apex at the star's centre. A vessel at <c>v</c>
/// relative to the planet, with the unit sun direction <c>ŝ</c> pointing from
/// the star to the planet, is in shadow when <c>x = ŝ·v &gt; 0</c> and its
/// distance from the shadow axis is less than <c>R(1 + x/D)</c>.
/// </para>
///
/// <para>
/// It finds transitions by stepping forward in time and bisecting every sign
/// change it sees. The only way for it to be wrong is to step over a shadow
/// window that is shorter than one step. Tests are written so that this can
/// only make them more lenient.
/// </para>
/// </remarks>
internal readonly struct ShadowReference
{
    readonly SystemBody[] bodies;
    readonly Orbit vessel;
    readonly int star;
    readonly double radius;

    /// <summary>
    /// The time step used when scanning for transitions.
    /// </summary>
    public readonly double Step;

    public ShadowReference(SystemBody[] bodies, in Orbit vessel, int star = ShadowTestSystem.Kerbol)
    {
        this.bodies = bodies;
        this.vessel = vessel;
        this.star = star;

        var parent = vessel.ParentBodyIndex ?? throw new ArgumentException("orbit has no parent");
        radius = bodies[parent].Radius;

        // The shortest possible shadow window is a pass through the shadow at
        // periapsis. Make sure that even a fairly glancing pass of that kind
        // is several steps long.
        var vpe = Math.Sqrt(
            vessel.GravParameter * (1.0 + vessel.Eccentricity) / Math.Abs(vessel.PeR)
        );
        Step = Math.Min(vessel.Period / 2000.0, 0.25 * radius / vpe);
        if (!(Step > 0.0) || double.IsInfinity(Step))
            Step = 1.0;
    }

    /// <summary>
    /// A signed distance to the shadow edge, in metres. Negative values are in
    /// shadow. On the sunward side this is just the distance from the shadow
    /// axis, which is always larger than the planet radius.
    /// </summary>
    public double Margin(double UT)
    {
        var parent = (int)vessel.ParentBodyIndex;
        var sunV =
            ShadowTestSystem.GetPosition(bodies, parent, UT)
            - ShadowTestSystem.GetPosition(bodies, star, UT);
        var D = sunV.magnitude;
        var sun = sunV / D;
        var v = vessel.GetRelativePositionAtUT(UT);

        var x = Vector3d.Dot(sun, v);
        var delta = (v - x * sun).magnitude;
        if (x <= 0.0)
            return v.magnitude - radius;

        return delta - radius * (1.0 + x / D);
    }

    public bool InShadow(double UT) => Margin(UT) < 0.0;

    /// <summary>
    /// Find the first time after <paramref name="UT"/> (and at most
    /// <paramref name="horizon"/> seconds after it) at which the shadow state
    /// changes. Returns <c>+∞</c> if there is no change before the horizon.
    /// </summary>
    public double NextTransition(double UT, double horizon)
    {
        var start = InShadow(UT);
        var end = UT + horizon;

        var lo = UT;
        while (lo < end)
        {
            var hi = Math.Min(lo + Step, end);
            if (InShadow(hi) != start)
                return Bisect(lo, hi, start);
            lo = hi;
        }

        return double.PositiveInfinity;
    }

    double Bisect(double lo, double hi, bool start)
    {
        for (int i = 0; i < 64 && hi - lo > 1e-4; ++i)
        {
            var mid = 0.5 * (lo + hi);
            if (InShadow(mid) == start)
                lo = mid;
            else
                hi = mid;
        }

        return hi;
    }
}
