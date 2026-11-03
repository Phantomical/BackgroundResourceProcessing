using System;
using BackgroundResourceProcessing.Collections.Burst;
using BackgroundResourceProcessing.Mathematics;
using BackgroundResourceProcessing.Maths;
using BackgroundResourceProcessing.Utils;
using Unity.Burst.CompilerServices;

namespace BackgroundResourceProcessing.Shadow;

/// <summary>
/// A lower bound on when a vessel's next eclipse season can start, for any
/// arrangement of the planet and the star in the body tree.
/// </summary>
///
/// <remarks>
/// <para>
/// The vessel's orbit doesn't precess, so its normal n̂ is fixed. An eclipse
/// is only possible while <c>|ŝ·n̂| &lt; L = R/r_pe + R/|d|</c>, where
/// <c>d</c> is the vector from the planet to the star and <c>ŝ = d/|d|</c>.
/// </para>
///
/// <para>
/// <c>d</c> is a signed sum of the position of each body relative to its
/// parent, going up from the planet and from the star to their common
/// ancestor. Each of those is a Kepler orbit, so how far it can move in a time
/// τ is bounded by its current velocity plus <c>½Aτ²</c> (where A is its
/// maximum acceleration), by its maximum speed times τ, and by the size of
/// its orbit. Summing those bounds bounds <c>|ŝ·n̂|</c> from below over the
/// whole step. The largest step for which that stays above the limit is
/// safe, so the bound steps forward by that until the steps get short. This
/// is conservative advancement, as used for continuous collision detection.
/// </para>
/// </remarks>
internal static unsafe class EclipseSeason
{
    /// <summary>
    /// The most bodies the chain between the planet and the star may have.
    /// </summary>
    public const int MaxTerms = 16;

    const int MaxSteps = 64;
    const int MaxGrowIter = 64;
    const int MaxBisectIter = 40;

    struct Term
    {
        public int Body;
        public double Sigma;

        /// <summary>The maximum speed relative to the parent.</summary>
        public double V;

        /// <summary>The maximum acceleration relative to the parent.</summary>
        public double A;

        /// <summary>The maximum distance from the parent.</summary>
        public double Ap;

        /// <summary>The maximum of <c>|v·n̂|</c>.</summary>
        public double Vn;

        /// <summary>The maximum of <c>|r·n̂|</c>.</summary>
        public double Np;

        // The rest depends on the current time: the position and velocity
        // relative to the parent, and the coefficients of the bounds on each
        // part of the movement of the body.
        public Vector3d R;
        public Vector3d Vel;
        public double LinN;
        public double CapN;
        public double LinP;
        public double CapP;
        public double LinF;
        public double CapF;
    }

    struct State
    {
        public Term* Terms;
        public int Count;
        public double L0;
        public double Radius;

        public double Dn;
        public double DPerp;
        public double D;
    }

    /// <summary>
    /// Find the earliest time, no earlier than <paramref name="UT"/>, at which
    /// a vessel orbiting <paramref name="planetIndex"/> could be eclipsed by
    /// it from <paramref name="starIndex"/>.
    /// </summary>
    ///
    /// <param name="normal">The normal of the vessel's orbit.</param>
    /// <param name="vesselPeR">The periapsis radius of the vessel's orbit.</param>
    /// <param name="minStep">
    /// Stop once the safe step is shorter than this. One vessel orbit is a
    /// good choice, since the caller searches one orbit at a time anyway.
    /// </param>
    ///
    /// <returns>
    /// A time no later than the start of the next eclipse season, or
    /// <see cref="double.PositiveInfinity"/> if an eclipse is never possible.
    /// Returns <paramref name="UT"/> if an eclipse is possible now, or if the
    /// bound can't be computed.
    /// </returns>
    public static double ComputeNextPossibleEclipse(
        SystemBody[] bodies,
        int planetIndex,
        int starIndex,
        Vector3d normal,
        double vesselPeR,
        double minStep,
        double UT
    )
    {
        fixed (SystemBody* ptr = bodies)
        {
            var system = new SolarSystem(new MemorySpan<SystemBody>(ptr, bodies.Length));
            return ComputeNextPossibleEclipse(
                system,
                planetIndex,
                starIndex,
                normal,
                vesselPeR,
                minStep,
                UT
            );
        }
    }

    /// <inheritdoc cref="ComputeNextPossibleEclipse(SystemBody[], int, int, Vector3d, double, double, double)"/>
    /// <param name="radius">
    /// The radius of the shadow, or NaN to use the radius of the planet.
    /// </param>
    [SkipLocalsInit]
    public static double ComputeNextPossibleEclipse(
        SolarSystem system,
        int planetIndex,
        int starIndex,
        Vector3d normal,
        double vesselPeR,
        double minStep,
        double UT,
        double radius = double.NaN
    )
    {
        if (!MathUtil.IsFinite(UT) || !(vesselPeR > 0.0))
            return UT;
        if (planetIndex < 0 || planetIndex >= system.Count)
            return UT;
        if (starIndex < 0 || starIndex >= system.Count || starIndex == planetIndex)
            return UT;

        int* planetChain = stackalloc int[MaxTerms + 1];
        int* starChain = stackalloc int[MaxTerms + 1];
        int planetCount = GetAncestors(system, planetIndex, planetChain);
        int starCount = GetAncestors(system, starIndex, starChain);
        if (planetCount < 0 || starCount < 0)
            return UT;

        // Find the common ancestor. Everything below it on the planet's side
        // counts negative, and everything below it on the star's side counts
        // positive.
        int planetTerms = -1;
        int starTerms = -1;
        for (int j = 0; j < starCount && planetTerms < 0; ++j)
        {
            for (int k = 0; k < planetCount; ++k)
            {
                if (planetChain[k] == starChain[j])
                {
                    planetTerms = k;
                    starTerms = j;
                    break;
                }
            }
        }

        if (planetTerms < 0 || planetTerms + starTerms > MaxTerms)
            return UT;

        Term* terms = stackalloc Term[MaxTerms];
        int count = 0;
        for (int k = 0; k < planetTerms; ++k)
        {
            if (!InitTerm(system, planetChain[k], -1.0, normal, out terms[count++]))
                return UT;
        }
        for (int j = 0; j < starTerms; ++j)
        {
            if (!InitTerm(system, starChain[j], 1.0, normal, out terms[count++]))
                return UT;
        }
        if (count == 0)
            return UT;

        if (double.IsNaN(radius))
            radius = system[planetIndex].Radius;

        var state = new State
        {
            Terms = terms,
            Count = count,
            L0 = radius / vesselPeR,
            Radius = radius,
        };

        if (!(minStep > 1.0))
            minStep = 1.0;

        var t = UT;
        for (int step = 0; step < MaxSteps; ++step)
        {
            if (!Evaluate(system, ref state, normal, t))
                return t;
            if (!IsSafe(in state, 0.0, infinite: false))
                return t;
            if (IsSafe(in state, 0.0, infinite: true))
                return double.PositiveInfinity;

            var tau = ComputeSafeStep(in state, minStep);
            if (!(tau >= minStep))
                return t;

            t += tau;
        }

        return t;
    }

    /// <summary>
    /// Fill <paramref name="chain"/> with <paramref name="index"/> and each of
    /// its ancestors, in order. Returns the number of bodies, or -1 if there
    /// are too many.
    /// </summary>
    static int GetAncestors(SolarSystem system, int index, int* chain)
    {
        int count = 0;
        while (index >= 0 && index < system.Count)
        {
            if (count > MaxTerms)
                return -1;

            chain[count++] = index;

            ref var body = ref system[index];
            if (!body.HasOrbit)
                break;
            index = body.Orbit.ParentBodyIndex ?? -1;
        }

        return count;
    }

    static bool InitTerm(
        SolarSystem system,
        int index,
        double sigma,
        Vector3d normal,
        out Term term
    )
    {
        term = default;

        ref var body = ref system[index];
        if (!body.HasOrbit)
            return false;

        ref var orbit = ref body.Orbit;
        var e = orbit.Eccentricity;
        var mu = orbit.KinematicGravParameter;
        var pe = orbit.PeR;
        var p = orbit.SemiLatusRectum;
        if (!(pe > 0.0) || !(mu > 0.0) || !(p > 0.0))
            return false;

        var alpha = Vector3d.Dot(orbit.OrbitFrame.X, normal);
        var beta = Vector3d.Dot(orbit.OrbitFrame.Y, normal);

        term.Body = index;
        term.Sigma = sigma;
        term.V = Math.Sqrt(mu * (1.0 + e) / pe);
        term.A = mu / (pe * pe);

        // v = √(μ/p)(-sin ν P̂ + (e + cos ν) Q̂)
        term.Vn = Math.Sqrt(mu / p) * (Hypot(alpha, beta) + e * Math.Abs(beta));

        if (e < 1.0)
        {
            // r = a(cos E - e) P̂ + b sin E Q̂
            var a = orbit.SemiMajorAxis;
            var b = orbit.SemiMinorAxis;
            term.Ap = orbit.ApR;
            term.Np = Hypot(a * alpha, b * beta) + a * e * Math.Abs(alpha);
        }
        else
        {
            term.Ap = double.PositiveInfinity;
            term.Np = double.PositiveInfinity;
        }

        return MathUtil.IsFinite(term.V) && MathUtil.IsFinite(term.A) && MathUtil.IsFinite(term.Vn);
    }

    static double Hypot(double x, double y) => Math.Sqrt(x * x + y * y);

    /// <summary>
    /// Evaluate the chain at <paramref name="UT"/> and set up the
    /// coefficients for <see cref="IsSafe"/>.
    /// </summary>
    static bool Evaluate(SolarSystem system, ref State state, Vector3d normal, double UT)
    {
        var d = Vector3d.zero;
        for (int i = 0; i < state.Count; ++i)
        {
            ref var term = ref state.Terms[i];
            var pos = system[term.Body].Orbit.GetRelativePositionAtUT(Dual.Variable(UT));
            term.R = pos.v;
            term.Vel = pos.dv;
            d += term.Sigma * pos.v;
        }

        state.Dn = Vector3d.Dot(d, normal);
        state.DPerp = (d - state.Dn * normal).magnitude;
        state.D = d.magnitude;
        if (!MathUtil.IsFinite(state.D) || !(state.D > 0.0))
            return false;

        var s = state.Dn >= 0.0 ? 1.0 : -1.0;
        for (int i = 0; i < state.Count; ++i)
        {
            ref var term = ref state.Terms[i];
            var r = term.R;
            var v = term.Vel;

            var rn = Vector3d.Dot(r, normal);
            var vn = Vector3d.Dot(v, normal);
            var sign = s * term.Sigma;

            // How much the body can reduce |d·n̂|.
            term.LinN = -sign * vn;
            term.CapN = term.Np + sign * rn;

            // How much the body can increase the part of d off the normal.
            term.LinP = (v - vn * normal).magnitude;
            term.CapP = term.Ap + (r - rn * normal).magnitude;

            // How much the body can change |d|.
            term.LinF = v.magnitude;
            term.CapF = term.Ap + r.magnitude;
        }

        return true;
    }

    /// <summary>
    /// Whether an eclipse is impossible over the whole of the next
    /// <paramref name="tau"/> seconds, or forever if
    /// <paramref name="infinite"/> is set.
    /// </summary>
    static bool IsSafe(in State state, double tau, bool infinite)
    {
        double devN = 0.0;
        double devP = 0.0;
        double devF = 0.0;

        for (int i = 0; i < state.Count; ++i)
        {
            ref var term = ref state.Terms[i];
            if (infinite)
            {
                devN += term.CapN;
                devP += term.CapP;
                devF += term.CapF;
                continue;
            }

            // The error in a linear prediction is at most ½Aτ². Along the
            // normal that bound is convex and starts at 0, so its maximum over
            // the step is at one of the ends.
            var q = 0.5 * term.A * tau * tau;
            var speed = term.V * tau;
            devN += Math.Min(
                Math.Min(Math.Max(0.0, term.LinN * tau + q), term.Vn * tau),
                term.CapN
            );
            devP += Math.Min(Math.Min(term.LinP * tau + q, speed), term.CapP);
            devF += Math.Min(Math.Min(term.LinF * tau + q, speed), term.CapF);
        }

        var dLo = state.D - devF;
        if (!(dLo > 0.0))
            return false;

        // Leave a little room for rounding error, like the exact bound does.
        var limit = state.L0 + state.Radius / dLo;
        limit = limit * (1.0 + 1e-6) + 1e-9;
        if (!(limit < 1.0))
            return false;

        // |ŝ·n̂| = |d_n| / √(d_n² + d_⊥²) > limit  ⇔  |d_n| > κ d_⊥
        var kappa = limit / Math.Sqrt(1.0 - limit * limit);
        return Math.Abs(state.Dn) - devN > kappa * (state.DPerp + devP);
    }

    /// <summary>
    /// The largest step that <see cref="IsSafe"/> accepts. Safety only gets
    /// harder as the step gets longer, so this can be found by growing the
    /// step and then bisecting. Returns 0 if a step of
    /// <paramref name="minStep"/> isn't safe.
    /// </summary>
    static double ComputeSafeStep(in State state, double minStep)
    {
        double lo = 0.0;
        double hi = minStep;
        for (int i = 0; i < MaxGrowIter && IsSafe(in state, hi, infinite: false); ++i)
        {
            lo = hi;
            hi *= 4.0;
        }

        if (lo == 0.0)
            return 0.0;

        for (int i = 0; i < MaxBisectIter && hi - lo > 1e-3 * lo; ++i)
        {
            var mid = 0.5 * (lo + hi);
            if (IsSafe(in state, mid, infinite: false))
                lo = mid;
            else
                hi = mid;
        }

        return lo;
    }
}
