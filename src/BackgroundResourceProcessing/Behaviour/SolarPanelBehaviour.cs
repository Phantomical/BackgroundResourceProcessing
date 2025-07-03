using System;
using System.Data;
using BackgroundResourceProcessing.Utils;
using UnityEngine;

namespace BackgroundResourceProcessing.Behaviour
{
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

        public override ConverterResources GetResources(VesselState state)
        {
            double rate = ChargeRate;
            if (PowerCurve != null)
            {
                rate *= PowerCurve.Evaluate((float)GetSolarDistance(state));
                rate *= GetPowerMultiplier(state);
            }
            else
            {
                rate *= GetSolarFlux(state) / PhysicsGlobals.SolarLuminosityAtHome;
            }

            var resources = new ConverterResources
            {
                Inputs =
                [
                    new()
                    {
                        ResourceName = ResourceName,
                        Ratio = rate,
                        FlowMode = FlowMode,
                    },
                ],
            };
            return resources;
        }

        public override double GetNextChangepoint(VesselState state)
        {
            var efficiencyCh = GetTimeEfficiencyChangepoint(state);
            var powerCh = GetPowerChangepoint(state);

            return Math.Min(efficiencyCh, powerCh);
        }

        protected virtual double GetSolarDistance(VesselState state)
        {
            var sun = Planetarium.fetch.Sun;
            return (sun.position - state.Vessel.vesselTransform.position).magnitude;
        }

        /// <summary>
        /// Get the current solar flux for this vessel.
        /// </summary>
        /// <param name="state"></param>
        /// <returns></returns>
        protected virtual double GetSolarFlux(VesselState state)
        {
            return state.Vessel.solarFlux;
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

            var vessel = state.Vessel;
            var bodies = GetReferenceBodies(vessel);

            var orbit = bodies.planet?.orbit ?? vessel.orbit;
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
                hi = distance * Mathf.Sqrt(1f + MaxError);
                lo = distance * Mathf.Sqrt(1f - MaxError);
            }
            if (orbit.PeR <= lo && hi <= orbit.ApR)
                return double.PositiveInfinity;

            double? chV = null;
            var v = orbit.TrueAnomalyAtUT(state.CurrentTime);

            if (lo < orbit.PeR)
            {
                var loV = orbit.TrueAnomalyAtRadius(lo);
                if (MathUtil.IsFinite(loV))
                    chV = loV;
            }

            if (orbit.ApR < hi)
            {
                var hiV = orbit.TrueAnomalyAtRadius(hi);
                if (MathUtil.IsFinite(hiV))
                {
                    if ((chV ?? double.NegativeInfinity) < hiV)
                        chV = hiV;
                }
            }

            if (chV == null)
                return double.PositiveInfinity;

            return orbit.GetUTforTrueAnomaly((double)chV, 0.0);
        }

        /// <summary>
        /// Get the last parent body before the sun.
        /// </summary>
        /// <param name="vessel"></param>
        /// <returns>
        /// The body, or <c>null</c> if the vessel is orbiting the sun directly.
        /// </returns>
        private ReferenceBodies GetReferenceBodies(Vessel vessel)
        {
            CelestialBody current = null;
            CelestialBody parent = vessel.orbit.referenceBody;

            while (!parent.isStar)
            {
                // We somehow aren't orbiting any stars whatsoever?
                if (parent.orbit == null || parent.orbit.referenceBody)
                    return new();

                current = parent;
                parent = parent.orbit.referenceBody;
            }

            return new() { star = parent, planet = current };
        }

        private struct ReferenceBodies
        {
            public CelestialBody star;
            public CelestialBody planet;
        }
    }
}
