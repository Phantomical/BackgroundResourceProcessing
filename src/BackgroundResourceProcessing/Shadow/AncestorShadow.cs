using System;
using BackgroundResourceProcessing.Mathematics;
using BackgroundResourceProcessing.Maths;
using BackgroundResourceProcessing.Utils;
using Unity.Burst.CompilerServices;

namespace BackgroundResourceProcessing.Shadow;

using Orbit = BackgroundResourceProcessing.Mathematics.Orbit;
using Terminator = OrbitShadow.Terminator;

/// <summary>
/// The shadows cast on a vessel by the bodies that its parent body orbits,
/// such as Jool's shadow on a vessel orbiting Laythe, and by the other bodies
/// in a binary system.
/// </summary>
///
/// <remarks>
/// <para>
/// The vessel's position relative to such a body P is the position of the
/// body C that orbits P (the carrier, Laythe in the example) plus the
/// vessel's offset from C. That isn't a Kepler orbit, so the analytic
/// solution in <see cref="OrbitShadow"/> doesn't apply directly. But the
/// offset is never more than ρ, the sum of the apoapses (each capped at the
/// SOI) of the orbits between the vessel and C. Moving a point by ρ changes
/// its distance to the edge of the shadow by at most <c>ρ(1 + R/D)</c>, so
/// the vessel can only be in shadow while C is in a shadow of radius
/// <c>R + 2ρ</c>. C's orbit is a Kepler orbit around P, so
/// <see cref="OrbitShadow"/> finds those windows exactly.
/// </para>
///
/// <para>
/// Within each window, the vessel's exact crossings are found by
/// conservative advancement. The distance to the shadow edge changes no
/// faster than a bound L on its derivative, so a step of <c>|margin|/L</c>
/// can't cross the edge. Stepping like that and bisecting any sign change
/// finds every crossing except windows shorter than
/// <see cref="MinScanStep"/>.
/// </para>
///
/// <para>
/// Only bodies below the common ancestor of the vessel and the star are
/// included. Above that the star orbits the body (or the body is the star),
/// and the body is either the star itself or a body like a black hole at the
/// centre of the system.
/// </para>
///
/// <para>
/// Planet packs build binary systems by putting both bodies in orbit around an
/// invisible barycentre, which casts no shadow. The other bodies orbiting a
/// barycentre that the vessel orbits (directly or through one of them) can
/// shadow it, and so can the other bodies orbiting a barycentre that is the
/// common ancestor of the vessel and the star. None of them carry the vessel,
/// so there are no windows to restrict the search to, and the time is searched
/// directly in the same way, one orbit of the vessel at a time. That is also
/// the fallback for an ancestor whose carrier gets too close to it for the
/// windows to work.
/// </para>
/// </remarks>
internal static unsafe class AncestorShadow
{
    const int MaxDepth = 16;

    /// <summary>
    /// The most ancestors whose shadows are included.
    /// </summary>
    const int MaxOccluders = 8;

    /// <summary>
    /// The most terminators of the carrier's widened shadow to step through
    /// for each occluder before giving up and recomputing later.
    /// </summary>
    const int MaxCarrierSteps = 16;

    /// <summary>
    /// The most steps to take when searching one window.
    /// </summary>
    const int MaxScanSteps = 4096;

    /// <summary>
    /// The shortest step to take when searching a window, in seconds. Shadow
    /// windows shorter than this can be missed.
    /// </summary>
    const double MinScanStep = 0.1;

    /// <summary>
    /// Crossings are found to within this many seconds.
    /// </summary>
    const double RootTolerance = 1e-3;

    /// <summary>
    /// How long, in seconds, the bound on the rate of change of the margin is
    /// used for before it is computed again.
    /// </summary>
    const double LipschitzInterval = 3600.0;

    struct Occluder
    {
        /// <summary>The body that casts the shadow.</summary>
        public int Body;

        /// <summary>The body orbiting <see cref="Body"/> that the vessel is
        /// (directly or indirectly) orbiting, or -1 if there is none because
        /// <see cref="Body"/> isn't an ancestor of the vessel.</summary>
        public int Carrier;

        /// <summary>The common ancestor of the vessel and
        /// <see cref="Body"/>. This is <see cref="Body"/> itself if it is an
        /// ancestor of the vessel, and otherwise the invisible barycentre that
        /// both of them orbit.</summary>
        public int Ancestor;

        /// <summary>The maximum distance between the vessel and the
        /// carrier, or <see cref="Ancestor"/> if there is no carrier.</summary>
        public double Offset;

        /// <summary>The maximum speed of the vessel relative to
        /// <see cref="Body"/>.</summary>
        public double Speed;

        /// <summary>The maximum distance of the vessel from
        /// <see cref="Body"/>.</summary>
        public double MaxDistance;
    }

    /// <summary>
    /// Combine the terminator for the shadow of the vessel's parent body with
    /// the shadows of its ancestors.
    /// </summary>
    [SkipLocalsInit]
    public static Terminator Combine(
        SolarSystem system,
        in Orbit vessel,
        int starIndex,
        double UT,
        Terminator parent
    )
    {
        Occluder* occluders = stackalloc Occluder[MaxOccluders];
        int count = FindOccluders(system, in vessel, starIndex, occluders);
        if (count == 0)
            return parent;

        Terminator* terms = stackalloc Terminator[MaxOccluders + 1];
        terms[0] = parent;
        int n = 1;

        // The combined state is only known until the earliest horizon of any
        // shadow, so there is no point in searching further than that.
        var limit = GetHorizon(in parent);
        for (int i = 0; i < count; ++i)
        {
            // An ancestor whose shadow can't be computed is left out, which
            // is how every ancestor was treated before their shadows were
            // included.
            var term = ComputeTerminator(system, in vessel, starIndex, in occluders[i], UT, limit);
            if (term.Failed)
                continue;

            terms[n++] = term;
            limit = Math.Min(limit, GetHorizon(in term));
        }

        return Merge(terms, n);
    }

    /// <summary>
    /// Find the bodies other than the vessel's parent that can cast a shadow
    /// on it: its visible ancestors, and the bodies in the same binary
    /// systems.
    /// </summary>
    [SkipLocalsInit]
    static int FindOccluders(
        SolarSystem system,
        in Orbit vessel,
        int starIndex,
        Occluder* occluders
    )
    {
        if (starIndex < 0 || starIndex >= system.Count)
            return 0;

        int* starChain = stackalloc int[MaxDepth];
        int starCount = 0;
        for (int index = starIndex; index >= 0 && index < system.Count && starCount < MaxDepth; )
        {
            starChain[starCount++] = index;
            ref var body = ref system[index];
            if (!body.HasOrbit)
                break;
            index = body.Orbit.ParentBodyIndex ?? -1;
        }

        // Walk up from the vessel's parent. At each level, the vessel is no
        // further than `offset` from the body at that level, and moves no
        // faster than `speed` relative to it.
        int level = vessel.ParentBodyIndex ?? -1;
        int below = -1;
        double innerAp = vessel.Eccentricity < 1.0 ? vessel.ApR : double.PositiveInfinity;
        double innerMu = vessel.GravParameter;
        double offset = 0.0;
        double speed = MaxSpeed(in vessel);
        int count = 0;

        for (int depth = 0; depth < MaxDepth && count < MaxOccluders; ++depth)
        {
            if (level < 0 || level >= system.Count)
                break;

            ref var body = ref system[level];

            // The vessel can't be further from the body than its SOI.
            var reach = innerAp;
            if (body.HasOrbit)
            {
                ref var own = ref body.Orbit;
                reach = Math.Min(
                    reach,
                    own.SemiMajorAxis * Math.Pow(innerMu / own.GravParameter, 0.4)
                );
            }
            offset += reach;
            if (!MathUtil.IsFinite(offset) || !MathUtil.IsFinite(speed))
                break;

            // An invisible body is the barycentre of a binary system, and the
            // other bodies orbiting it can block light from the star.
            if (body.Invisible)
            {
                count = AddSiblings(
                    system,
                    level,
                    below,
                    starChain,
                    starCount,
                    offset,
                    speed,
                    occluders,
                    count
                );
            }

            if (Contains(starChain, starCount, level) || !body.HasOrbit)
                break;

            ref var orbit = ref body.Orbit;
            int occluder = orbit.ParentBodyIndex ?? -1;
            if (occluder < 0 || occluder >= system.Count)
                break;

            // A carrier on an escape orbit has no maximum distance from the
            // occluder. No body in a real system does this.
            if (!(orbit.Eccentricity < 1.0))
                break;

            speed += MaxSpeed(in orbit);
            if (
                count < MaxOccluders
                && !Contains(starChain, starCount, occluder)
                && !system[occluder].Invisible
            )
            {
                occluders[count++] = new Occluder
                {
                    Body = occluder,
                    Carrier = level,
                    Ancestor = occluder,
                    Offset = offset,
                    Speed = speed,
                    MaxDistance = orbit.ApR + offset,
                };
            }

            innerAp = orbit.ApR;
            innerMu = orbit.GravParameter;
            below = level;
            level = occluder;
        }

        return count;
    }

    /// <summary>
    /// Add the bodies orbiting the invisible body <paramref name="level"/>,
    /// other than the ones that the vessel or the star are part of.
    /// </summary>
    static int AddSiblings(
        SolarSystem system,
        int level,
        int below,
        int* starChain,
        int starCount,
        double offset,
        double speed,
        Occluder* occluders,
        int count
    )
    {
        for (int index = 0; index < system.Count && count < MaxOccluders; ++index)
        {
            if (index == below || index == level)
                continue;

            ref var sibling = ref system[index];
            if (!sibling.HasOrbit || sibling.Invisible)
                continue;
            if ((sibling.Orbit.ParentBodyIndex ?? -1) != level)
                continue;
            if (Contains(starChain, starCount, index))
                continue;
            if (!(sibling.Orbit.Eccentricity < 1.0))
                continue;

            var maxSpeed = speed + MaxSpeed(in sibling.Orbit);
            var maxDistance = offset + sibling.Orbit.ApR;
            if (!MathUtil.IsFinite(maxSpeed) || !MathUtil.IsFinite(maxDistance))
                continue;

            occluders[count++] = new Occluder
            {
                Body = index,
                Carrier = -1,
                Ancestor = level,
                Offset = offset,
                Speed = maxSpeed,
                MaxDistance = maxDistance,
            };
        }

        return count;
    }

    static bool Contains(int* chain, int count, int index)
    {
        for (int i = 0; i < count; ++i)
        {
            if (chain[i] == index)
                return true;
        }

        return false;
    }

    /// <summary>
    /// The speed at periapsis, which is the fastest that a body on the orbit
    /// moves relative to its parent.
    /// </summary>
    static double MaxSpeed(in Orbit orbit) =>
        Math.Sqrt(orbit.KinematicGravParameter * (1.0 + orbit.Eccentricity) / orbit.PeR);

    /// <summary>
    /// The time until which a terminator gives the state.
    /// </summary>
    static double GetHorizon(in Terminator term) => term.HasNext ? term.NextUT : term.UT;

    /// <summary>
    /// Compute the next two terminators for the shadow of one ancestor.
    /// Returns a failure if they can't be computed.
    /// </summary>
    ///
    /// <param name="limit">
    /// The search stops once the state is known up to this time, since the
    /// combined state isn't known past it anyway.
    /// </param>
    [SkipLocalsInit]
    static Terminator ComputeTerminator(
        SolarSystem system,
        in Orbit vessel,
        int starIndex,
        in Occluder occluder,
        double UT,
        double limit
    )
    {
        var radius = system[occluder.Body].Radius;
        var padded = radius + 2.0 * occluder.Offset;
        var scanner = new Scanner(system, in vessel, starIndex, in occluder, radius);

        bool start = scanner.Margin(UT) <= 0.0;
        bool state = start;
        double* times = stackalloc double[2];
        int found = 0;
        var t = UT;

        // The carrier needs to stay far enough from the occluder that the
        // vessel can't reach the shadow while the carrier is on the sunward
        // side of the occluder. If it doesn't, or there is no carrier, the
        // time is searched directly instead. That takes a step for every
        // LipschitzInterval even far from the shadow, so it only goes one
        // orbit of the vessel ahead and leaves the rest to a recompute.
        bool windowed =
            occluder.Carrier >= 0
            && system[occluder.Carrier].Orbit.PeR > 1.1 * (padded + occluder.Offset);
        if (!windowed)
        {
            var hi = limit;
            if (vessel.Eccentricity < 1.0 && MathUtil.IsFinite(vessel.Period))
                hi = Math.Min(hi, UT + vessel.Period);

            t = scanner.Scan(UT, hi, ref state, times, ref found);
            return MakeTerminator(UT, t, start, times, found);
        }

        ref var carrier = ref system[occluder.Carrier].Orbit;

        // Step through the carrier's widened shadow one terminator at a time.
        // Outside of it the vessel is lit, and inside it the vessel's own
        // crossings are searched for.
        for (int i = 0; i < MaxCarrierSteps && found < 2 && t < limit; ++i)
        {
            var window = OrbitShadow.ComputeSingleOrbitTerminator(
                system,
                in carrier,
                starIndex,
                t,
                padded
            );
            if (window.Failed || !(window.UT > t))
                return Terminator.Failure();

            if (window.InShadow)
            {
                // The carrier never leaves the widened shadow, which can't
                // happen for a body that orbits the occluder.
                if (!(window.UT < double.MaxValue))
                    return Terminator.Failure();

                var reached = scanner.Scan(t, window.UT, ref state, times, ref found);
                if (reached < window.UT)
                {
                    t = reached;
                    break;
                }
            }

            t = window.UT;
            if (!(t < double.MaxValue))
            {
                t = double.MaxValue;
                break;
            }
        }

        return MakeTerminator(UT, t, start, times, found);
    }

    /// <summary>
    /// Build the terminator from the crossings that a search found, where
    /// <paramref name="t"/> is the time up to which it searched.
    /// </summary>
    static Terminator MakeTerminator(double UT, double t, bool start, double* times, int found)
    {
        if (found == 0)
        {
            if (!(t > UT))
                return Terminator.Failure();
            return new Terminator { UT = t, InShadow = start };
        }

        var term = new Terminator { UT = times[0], InShadow = start };
        var next = found >= 2 ? times[1] : t;
        if (next > times[0])
        {
            term.HasNext = true;
            term.NextInShadow = !start;
            term.NextUT = next;
        }

        return term;
    }

    /// <summary>
    /// Merge the terminators for several shadows. The vessel is in shadow if
    /// any of them says so.
    /// </summary>
    ///
    /// <remarks>
    /// Each terminator gives the state up to its <c>UT</c>, and with
    /// <c>HasNext</c> also the state up to its <c>NextUT</c>. So the combined
    /// state is known until the earliest time at which one of them runs out,
    /// and the only changes before then are at the <c>UT</c> of each one.
    /// Changes of one shadow while the vessel is in another don't change the
    /// combined state, so they are skipped.
    /// </remarks>
    static Terminator Merge(Terminator* terms, int count)
    {
        double horizon = double.PositiveInfinity;
        bool start = false;
        for (int k = 0; k < count; ++k)
        {
            ref var term = ref terms[k];
            horizon = Math.Min(horizon, term.HasNext ? term.NextUT : term.UT);
            start |= term.InShadow;
        }

        double first = double.NaN;
        double second = double.NaN;
        var cursor = double.NegativeInfinity;
        while (double.IsNaN(second))
        {
            // The next change of any one shadow, before the horizon.
            var e = horizon;
            for (int k = 0; k < count; ++k)
            {
                var u = terms[k].UT;
                if (u > cursor && u < e)
                    e = u;
            }

            if (!(e < horizon))
                break;
            cursor = e;

            bool state = false;
            for (int k = 0; k < count; ++k)
            {
                ref var term = ref terms[k];
                state |= e < term.UT ? term.InShadow : term.NextInShadow;
            }

            var expected = double.IsNaN(first) ? start : !start;
            if (state == expected)
                continue;

            if (double.IsNaN(first))
                first = e;
            else
                second = e;
        }

        if (double.IsNaN(first))
            return new Terminator { UT = horizon, InShadow = start };

        var result = new Terminator { UT = first, InShadow = start };
        var nextUT = double.IsNaN(second) ? horizon : second;
        if (nextUT > first)
        {
            result.HasNext = true;
            result.NextInShadow = !start;
            result.NextUT = nextUT;
        }

        return result;
    }

    /// <summary>
    /// Evaluates the exact distance from the vessel to the edge of an
    /// ancestor's shadow, using the same model as <see cref="OrbitShadow"/>.
    /// </summary>
    readonly struct Scanner
    {
        readonly SolarSystem system;
        readonly Orbit vessel;
        readonly int parent;
        readonly int body;
        readonly int ancestor;
        readonly int star;
        readonly double radius;
        readonly double speed;
        readonly double maxDistance;

        public Scanner(
            SolarSystem system,
            in Orbit vessel,
            int star,
            in Occluder occluder,
            double radius
        )
        {
            this.system = system;
            this.vessel = vessel;
            this.parent = vessel.ParentBodyIndex ?? -1;
            this.body = occluder.Body;
            this.ancestor = occluder.Ancestor;
            this.star = star;
            this.radius = radius;
            this.speed = occluder.Speed;
            this.maxDistance = occluder.MaxDistance;
        }

        /// <summary>
        /// The signed distance from the vessel to the shadow edge, which is
        /// <c>&lt;= 0</c> in shadow.
        /// </summary>
        public double Margin(double UT)
        {
            // Sum the positions up the chain instead of subtracting absolute
            // positions. Those can be around 10¹³ m in planet packs with
            // several stars, where rounding is around a millimetre, which is
            // enough to put a terminator on the wrong side of the edge.
            var x = vessel.GetRelativePositionAtUT(UT);
            int index = parent;
            for (int i = 0; i < MaxDepth && index != ancestor && index >= 0; ++i)
            {
                ref var orbit = ref system[index].Orbit;
                x += orbit.GetRelativePositionAtUT(UT);
                index = orbit.ParentBodyIndex ?? -1;
            }

            index = body;
            for (int i = 0; i < MaxDepth && index != ancestor && index >= 0; ++i)
            {
                ref var orbit = ref system[index].Orbit;
                x -= orbit.GetRelativePositionAtUT(UT);
                index = orbit.ParentBodyIndex ?? -1;
            }

            var sunV =
                system.GetPositionAtUT(in system[body], UT)
                - system.GetPositionAtUT(in system[star], UT);
            var D = sunV.magnitude;
            var sun = sunV / D;

            var along = Vector3d.Dot(sun, x);
            if (along <= 0.0)
                return x.magnitude - radius;

            return (x - along * sun).magnitude - radius * (1.0 + along / D);
        }

        /// <summary>
        /// An upper bound on how fast <see cref="Margin"/> changes over a
        /// window starting at <paramref name="UT"/>.
        /// </summary>
        ///
        /// <remarks>
        /// The vessel moves no faster than the sum of the maximum speeds of
        /// the orbits between it and the occluder. Rotating the sun direction
        /// at a rate ω moves the shadow axis past the vessel at no more than
        /// <c>2ω|x|</c>. ω is taken from the start of the window, and doubled
        /// since it changes a little over a window, which is short compared
        /// to the occluder's orbit around the star.
        /// </remarks>
        double Lipschitz(double UT)
        {
            var sunV =
                system.GetPositionAtUT(in system[body], Dual.Variable(UT))
                - system.GetPositionAtUT(in system[star], Dual.Variable(UT));
            var D = sunV.v.magnitude;
            var omega = sunV.dv.magnitude / D;

            return (1.0 + 2.0 * radius / D) * (speed + 4.0 * maxDistance * omega);
        }

        /// <summary>
        /// Find the crossings of the shadow edge in <c>[lo, hi]</c>, adding
        /// them to <paramref name="times"/> until there are two of them.
        /// <paramref name="state"/> is the state at <paramref name="lo"/>,
        /// and is updated at each crossing.
        /// </summary>
        ///
        /// <returns>
        /// The time up to which the window has been searched. This is less
        /// than <paramref name="hi"/> if the second crossing was found, or if
        /// the search took too many steps.
        /// </returns>
        public double Scan(double lo, double hi, ref bool state, double* times, ref int found)
        {
            var t = lo;
            var m = Margin(t);
            var L = 0.0;
            var updated = double.NegativeInfinity;
            for (int i = 0; i < MaxScanSteps; ++i)
            {
                if (!(t < hi))
                    return hi;

                // The bound only holds for a while, since the rate at which
                // the sun direction turns changes over the occluder's orbit.
                if (!(t - updated < LipschitzInterval))
                {
                    L = Lipschitz(t);
                    updated = t;
                    if (!(L > 0.0) || !MathUtil.IsFinite(L))
                        return t;
                }

                var step = Math.Min(Math.Max(Math.Abs(m) / L, MinScanStep), LipschitzInterval);
                var next = Math.Min(t + step, hi);
                var mNext = Margin(next);

                if ((mNext <= 0.0) == state)
                {
                    t = next;
                    m = mNext;
                    continue;
                }

                t = Bisect(t, next, state);
                state = !state;
                times[found++] = t;
                if (found == 2)
                    return t;
                m = Margin(t);
            }

            return t;
        }

        /// <summary>
        /// Find the crossing between <paramref name="lo"/> (in state
        /// <paramref name="loState"/>) and <paramref name="hi"/>. Returns a
        /// time on the <paramref name="hi"/> side of it.
        /// </summary>
        double Bisect(double lo, double hi, bool loState)
        {
            for (int i = 0; i < 64 && hi - lo > RootTolerance; ++i)
            {
                var mid = 0.5 * (lo + hi);
                if ((Margin(mid) <= 0.0) == loState)
                    lo = mid;
                else
                    hi = mid;
            }

            return hi;
        }
    }
}
