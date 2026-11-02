using System;
using System.Collections.Generic;
using BackgroundResourceProcessing.Utils;
using UnityEngine;

namespace BackgroundResourceProcessing.Behaviour;

/// <summary>
/// A <see cref="ConverterBehaviour"/> that models the behaviour of solar
/// panels in the background.
/// </summary>
///
/// <remarks>
/// There are a few different factors that affect the changepoint of a
/// solar panel (and also its power output).
/// </remarks>
public class SolarPanelBehaviour : ConverterBehaviour
{
    private const double TimeEfficiencyCurveMult = 1.1574074074074073E-05;

    /// <summary>
    /// The resource that this solar panel produces.
    /// </summary>
    [KSPField(isPersistant = true)]
    public string ResourceName;

    /// <summary>
    /// The flow mode for resource output by this solar panel.
    /// </summary>
    [KSPField(isPersistant = true)]
    public ResourceFlowMode FlowMode;

    /// <summary>
    /// The maximum amount of error that is allowed. This is used to
    /// compute when the next changepoint should occur.
    /// </summary>
    [KSPField(isPersistant = true)]
    public float MaxError = 0.1f;

    /// <summary>
    /// The rate at which the panel would produce power at the current
    /// angle of attack were it orbiting Kerbin.
    /// </summary>
    ///
    /// <remarks>
    /// Any parameters which do not vary over time should be multiplied
    /// into here. This includes configured multipliers, atmospheric
    /// attenuation, water attenuation, etc. AoA for landed vessels should
    /// be approximated throughout the day and added here.
    /// </remarks>
    [KSPField(isPersistant = true)]
    public double ChargeRate;

    /// <summary>
    /// The time that this panel was launched. Used to compute changepoints
    /// for thing related to time efficiency curves.
    /// </summary>
    [KSPField(isPersistant = true)]
    public double LaunchUT;

    /// <summary>
    /// A curve that determines how much power this
    /// </summary>
    [KSPField(isPersistant = true)]
    public FloatCurve PowerCurve = null;

    /// <summary>
    /// How this panel's performance varies over time.
    /// </summary>
    [KSPField(isPersistant = true)]
    public FloatCurve TimeEfficiencyCurve = null;

    /// <summary>
    /// The name of the star this panel's rates are computed against
    /// (generally the panel's tracking body). When unset, the strongest
    /// currently-lit star from the vessel's shadow state is used.
    /// </summary>
    [KSPField]
    public string StarName = null;

    /// <summary>
    /// The light this panel receives from each star, depending on which star
    /// it is pointed at. When set, <see cref="ChargeRate"/> excludes the
    /// panel's angle of attack and the panel tracks the brightest
    /// currently-lit star, like Kopernicus' multi-star panel fixer does.
    /// </summary>
    ///
    /// <remarks>
    /// The entry for star A lists the exposure factor for every star while
    /// the panel is pointed at A. A single entry means the panel only ever
    /// points at that star (e.g. Kopernicus' manual tracking mode).
    /// </remarks>
    public List<StarTracking> Tracking = null;

    /// <summary>
    /// The exposure of a panel to each star while it is pointed at
    /// <see cref="StarName"/>.
    /// </summary>
    public class StarTracking
    {
        public string StarName;
        public List<StarExposure> Exposures = [];

        public double GetExposure(CelestialBody star)
        {
            if (star == null)
                return 0.0;

            foreach (var exposure in Exposures)
            {
                if (exposure.StarName == star.bodyName)
                    return exposure.Factor;
            }

            return 0.0;
        }
    }

    public struct StarExposure
    {
        public string StarName;
        public double Factor;
    }

    private CelestialBody starBody = null;
    private readonly Dictionary<string, CelestialBody> starCache = [];

    protected override void OnSave(ConfigNode node)
    {
        base.OnSave(node);
        if (StarName != null)
            node.AddValue(nameof(StarName), StarName);

        if (Tracking != null)
        {
            foreach (var tracking in Tracking)
            {
                var tnode = node.AddNode("TRACKING");
                tnode.AddValue("star", tracking.StarName);

                foreach (var exposure in tracking.Exposures)
                {
                    var enode = tnode.AddNode("EXPOSURE");
                    enode.AddValue("star", exposure.StarName);
                    enode.AddValue("factor", exposure.Factor);
                }
            }
        }
    }

    protected override void OnLoad(ConfigNode node)
    {
        base.OnLoad(node);

        var tnodes = node.GetNodes("TRACKING");
        if (tnodes.Length == 0)
            return;

        Tracking = [];
        foreach (var tnode in tnodes)
        {
            var tracking = new StarTracking { StarName = tnode.GetValue("star") };
            if (tracking.StarName == null)
                continue;

            foreach (var enode in tnode.GetNodes("EXPOSURE"))
            {
                var name = enode.GetValue("star");
                double factor = 0.0;
                if (name == null || !enode.TryGetDouble("factor", ref factor))
                    continue;

                tracking.Exposures.Add(new() { StarName = name, Factor = factor });
            }

            Tracking.Add(tracking);
        }

        if (Tracking.Count == 0)
            Tracking = null;
    }

    public override ConverterResources GetResources(VesselState state)
    {
        double rate = ChargeRate;
        if (state.ShadowState.InShadow)
        {
            rate = 0.0;
        }
        else if (Tracking != null)
        {
            rate *= GetTrackedRateFactor(state);
        }
        else if (PowerCurve != null)
        {
            rate *= PowerCurve.Evaluate((float)GetSolarDistance(state));
            rate *= GetPowerMultiplier(state);
        }
        else
        {
            rate *= GetSolarFluxFactor(state);
        }

        var efficiencyCh = GetTimeEfficiencyChangepoint(state);
        var powerCh = GetPowerChangepoint(state);
        var shadowCh = state.ShadowState.NextTerminatorEstimate;
        var changepoint = Math.Min(Math.Min(efficiencyCh, powerCh), shadowCh);

        var resources = new ConverterResources
        {
            Outputs =
            [
                new()
                {
                    ResourceName = ResourceName,
                    Ratio = rate,
                    FlowMode = FlowMode,
                },
            ],
            NextChangepoint = changepoint,
        };
        return resources;
    }

    /// <summary>
    /// Get the star that this panel's rates are computed against.
    /// </summary>
    protected CelestialBody GetStar(VesselState state)
    {
        if (Tracking != null)
            return FindStar(SelectTracking(state).StarName) ?? state.ShadowState.Star;

        if (starBody != null)
            return starBody;

        if (!string.IsNullOrEmpty(StarName))
        {
            starBody = PSystemManager.Instance?.localBodies?.Find(body =>
                body.isStar && body.bodyName == StarName
            );
            if (starBody != null)
                return starBody;
        }

        return state.ShadowState.Star;
    }

    /// <summary>
    /// Pick the star the panel is pointed at: the brightest currently-lit
    /// star that it can track. If none of the lit stars can be tracked then
    /// the panel stays pointed at the first star it knows about.
    /// </summary>
    internal StarTracking SelectTracking(VesselState state)
    {
        var lit = state.ShadowState.LitStars;
        if (lit == null && state.ShadowState.Star != null)
            lit = [state.ShadowState.Star];

        if (lit != null)
        {
            // LitStars is ordered by descending flux, so the first match is
            // the brightest star the panel can point at.
            foreach (var star in lit)
            {
                if (star == null)
                    continue;

                foreach (var tracking in Tracking)
                {
                    if (tracking.StarName == star.bodyName)
                        return tracking;
                }
            }
        }

        return Tracking[0];
    }

    /// <summary>
    /// Compute the factor applied to <see cref="ChargeRate"/> when the
    /// panel's exposure to each star is known. This sums the light from
    /// every lit star given the panel's orientation towards the tracked star.
    /// </summary>
    private double GetTrackedRateFactor(VesselState state)
    {
        var tracking = SelectTracking(state);

        if (PowerCurve != null)
        {
            var star = FindStar(tracking.StarName);
            if (star == null)
                return 0.0;

            return tracking.GetExposure(star)
                * PowerCurve.Evaluate((float)GetSolarDistance(state))
                * GetPowerMultiplier(state);
        }

        var lit = state.ShadowState.LitStars;
        if (lit == null)
        {
            var star = state.ShadowState.Star;
            if (star == null)
                return 0.0;

            return tracking.GetExposure(star)
                * ShadowState.StarProvider.GetSolarFluxFactor(star, Vessel);
        }

        double factor = 0.0;
        foreach (var star in lit)
        {
            var exposure = tracking.GetExposure(star);
            if (exposure <= 0.0)
                continue;

            factor += exposure * ShadowState.StarProvider.GetSolarFluxFactor(star, Vessel);
        }

        return factor;
    }

    private CelestialBody FindStar(string name)
    {
        if (string.IsNullOrEmpty(name))
            return null;
        if (starCache.TryGetValue(name, out var body) && body != null)
            return body;

        body = PSystemManager.Instance?.localBodies?.Find(body =>
            body.isStar && body.bodyName == name
        );
        if (body != null)
            starCache[name] = body;
        return body;
    }

    protected virtual double GetSolarDistance(VesselState state)
    {
        // Stock evaluates powerCurve at the raw center-to-center distance
        // (ModuleDeployableSolarPanel does not subtract the star's radius).
        var star = GetStar(state);
        return (star.position - Vessel.vesselTransform.position).magnitude;
    }

    /// <summary>
    /// Get the current solar flux for this vessel.
    /// </summary>
    /// <param name="state"></param>
    /// <returns></returns>
    protected virtual double GetSolarFluxFactor(VesselState state)
    {
        return state.ShadowState.GetSolarFluxFactor(Vessel);
    }

    /// <summary>
    /// A custom multiplier for the power curve, if any.
    /// </summary>
    /// <param name="state"></param>
    /// <returns></returns>
    protected virtual double GetPowerMultiplier(VesselState state)
    {
        return 1.0;
    }

    /// <summary>
    /// Determine the time at which the time efficiency curve will go
    /// outside of its error bounds.
    /// </summary>
    protected double GetTimeEfficiencyChangepoint(VesselState state)
    {
        if (TimeEfficiencyCurve == null)
            return double.PositiveInfinity;

        var lifetime = state.CurrentTime - LaunchUT;
        var parameter = (float)(lifetime * TimeEfficiencyCurveMult);
        var changepoint = MathUtil.FindErrorBoundaryForward(
            TimeEfficiencyCurve,
            parameter,
            MaxError
        );

        return changepoint / TimeEfficiencyCurveMult + LaunchUT;
    }

    /// <summary>
    /// Determine the time at which a changepoint will occur due to
    /// produced power changing beyond the relative error bounds.
    /// </summary>
    /// <param name="state"></param>
    /// <returns></returns>
    protected double GetPowerChangepoint(VesselState state)
    {
        // Some notes here:
        // - We ignore differences due to places within the current SOI.
        //   These should generally be small enough to not matter.

        var bodies = GetReferenceBodies(Vessel, GetStar(state));

        var orbit = bodies.planet?.orbit ?? Vessel.orbit;
        var distance = orbit.GetRadiusAtUT(state.CurrentTime);

        double hi;
        double lo;
        if (PowerCurve != null)
        {
            hi = MathUtil.FindErrorBoundaryForward(PowerCurve, (float)distance, MaxError);
            lo = MathUtil.FindErrorBoundaryBackward(PowerCurve, (float)distance, MaxError);
        }
        else
        {
            // The rate scales with the inverse square of the distance to the
            // star's surface (see IStarProvider.GetSolarFluxFactor), so the
            // error boundaries are computed on the surface distance and then
            // converted back to orbital radii.
            var radius = bodies.star?.Radius ?? 0.0;
            var surface = distance - radius;
            hi = surface * Mathf.Sqrt(1f + MaxError) + radius;
            lo = surface * Mathf.Sqrt(1f - MaxError) + radius;
        }

        if (lo <= orbit.PeR && orbit.ApR <= hi)
            return double.PositiveInfinity;

        double hiV = FutureTrueAnomalyAtRadius(orbit, hi);
        double loV = FutureTrueAnomalyAtRadius(orbit, lo);

        double chV = double.PositiveInfinity;
        if (hiV < chV)
            chV = hiV;
        if (loV < chV)
            chV = loV;

        if (double.IsInfinity(chV))
            return double.PositiveInfinity;

        return orbit.TimeOfTrueAnomaly(chV, state.CurrentTime);
    }

    /// <summary>
    /// Walk up the SOI chain to the star the rate calculation is measured
    /// against, along with its immediate child in the chain.
    /// </summary>
    ///
    /// <returns>
    /// The star and the last body before it (<c>planet</c> is <c>null</c> if
    /// the vessel orbits the star directly). Stars can themselves orbit other
    /// stars in multi-star systems, so the walk only stops early at a star
    /// when it is <paramref name="star"/>; if the target is not an ancestor
    /// (or none was supplied), the first star in the chain is used instead.
    /// </returns>
    private ReferenceBodies GetReferenceBodies(Vessel vessel, CelestialBody star)
    {
        CelestialBody current = null;
        CelestialBody parent = vessel.orbit.referenceBody;

        ReferenceBodies fallback = new();
        bool haveFallback = false;

        while (parent != null)
        {
            if (ReferenceEquals(parent, star))
                return new() { star = parent, planet = current };

            if (parent.isStar && !haveFallback)
            {
                fallback = new() { star = parent, planet = current };
                haveFallback = true;
            }

            current = parent;
            parent = parent.orbit?.referenceBody;
        }

        // We never found the target star; use the first star in the chain
        // (or nothing at all if we somehow aren't orbiting any star).
        return fallback;
    }

    // Get the true anomaly at the next time the orbit will be at radius r.
    // Returns NaN if the orbit will not cross r in the future.
    private double FutureTrueAnomalyAtRadius(Orbit orbit, double r)
    {
        double a = orbit.semiMajorAxis;
        double e = orbit.eccentricity;
        double current = orbit.trueAnomaly;
        double cosv = (a * (1 - e * e) - r) / (r * e);

        if (cosv < -1.0 || cosv > 1.0)
            return double.NaN;

        // Acos returns the anomaly in the first half of the orbit relative
        // to the PE (e.g. PE -> AP).
        double v = Math.Acos(cosv);
        double u = 2 * Math.PI - v;

        if (orbit.eccentricity >= 1.0)
        {
            // For hyperbolic orbits we cannot go around so we need to be
            // somewhat more careful in how we check this.

            if (current < 2 * Math.PI)
            {
                if (current <= v)
                    return v;
                return double.NaN;
            }
            else
            {
                if (current <= u)
                    return u;
                return v;
            }
        }
        else
        {
            // Normal orbits loop around so it's just a matter of checking
            // the cases in order.
            if (current <= v)
                return v;
            if (current < u)
                return u;
            return v;
        }
    }

    private struct ReferenceBodies
    {
        public CelestialBody star;
        public CelestialBody planet;
    }
}
