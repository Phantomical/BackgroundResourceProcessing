using System;
using System.Reflection;
using BackgroundResourceProcessing.Behaviour;
using UnityEngine;

namespace BackgroundResourceProcessing.Converter;

public class BackgroundSolarPanel : BackgroundConverter<ModuleDeployableSolarPanel>
{
    private const BindingFlags Flags =
        BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    /// <summary>
    /// The maximum allowed error in solar power before the solver will be
    /// made to recompute solar panel rates.
    /// </summary>
    [KSPField]
    public float MaxError = 0.1f;

    private readonly int landedLayerMask =
        (1 << LayerMask.NameToLayer("PhysicalObjects"))
        | (1 << LayerMask.NameToLayer("TerrainColliders"))
        | (1 << LayerMask.NameToLayer("Local Scenery"))
        | LayerUtil.DefaultEquivalent;
    private readonly int orbitLayerMask =
        (1 << LayerMask.NameToLayer("PhysicalObjects")) | LayerUtil.DefaultEquivalent;

    public override ModuleBehaviour GetBehaviour(ModuleDeployableSolarPanel panel)
    {
        if (!panel.isEnabled)
            return null;

        // If the solar panel is not extended then it generates no power.
        if (panel.deployState != ModuleDeployablePart.DeployState.EXTENDED)
            return null;

        // This solar panel is currently within a fairing and so is not
        // receiving any power.
        if (panel.part.ShieldedFromAirstream && panel.applyShielding)
            return null;

        var vessel = panel.vessel;
        double power = panel.chargeRate * panel.flowMult * panel.efficiencyMult;

        switch (vessel.situation)
        {
            // Docked is a weird one. It could really be anything.
            // We treat it as being in orbit here.
            case Vessel.Situations.DOCKED:
            case Vessel.Situations.ESCAPING:
            case Vessel.Situations.ORBITING:
            case Vessel.Situations.SUB_ORBITAL:
                // In orbit we assume the vessel maintains the same
                // orientation relative to the sun at all times.
                //
                // This isn't quite the same as how things behave in KSP,
                // but it is a reasonable simplification to make here.
                var sun = GetSunDirection(panel.panelRotationTransform.position);
                if (!GetPanelLOS(panel, sun, orbitLayerMask))
                    return null;

                power *= GetOrbitAoAFactor(panel);
                break;

            // Generally it should be pretty much impossible for this to
            // happen long-term in the background. With mods, however, it
            // may be possible so we'll assume this is equivalent to being
            // landed.
            case Vessel.Situations.FLYING:
            case Vessel.Situations.LANDED:
            case Vessel.Situations.PRELAUNCH:
            case Vessel.Situations.SPLASHED:
                // So landed solar panels require some special consideration.
                // We don't want to constantly be re-evaluating panel AoAs
                // but at the same time we want to at least be somewhat
                // accurate.
                //
                // The solution I've gone with here is to evaluate the panel's
                // AoA at 5 different points and then used a weighted average
                // of those points to compute the effective AoA factor.

                ComputeLandedSunVectors(panel, out var sunrise, out var noon, out var sunset);
                var midpoint1 = (sunrise + noon).normalized;
                var midpoint2 = (sunset + noon).normalized;

                double weightedAoA = 0.0;
                if (GetPanelLOS(panel, sunrise, landedLayerMask))
                    weightedAoA += 0.125 * GetOptimalPanelAoAFactor(panel, sunrise);
                if (GetPanelLOS(panel, midpoint1, landedLayerMask))
                    weightedAoA += 0.25 * GetOptimalPanelAoAFactor(panel, midpoint1);
                if (GetPanelLOS(panel, noon, landedLayerMask))
                    weightedAoA += 0.25 * GetOptimalPanelAoAFactor(panel, noon);
                if (GetPanelLOS(panel, midpoint2, landedLayerMask))
                    weightedAoA += 0.25 * GetOptimalPanelAoAFactor(panel, midpoint2);
                if (GetPanelLOS(panel, sunset, landedLayerMask))
                    weightedAoA += 0.125 * GetOptimalPanelAoAFactor(panel, sunset);

                power *= weightedAoA;

                // It's possible to be both LANDED and underwater, so we
                // can't constrain this check to only when splashed.
                power *= GetSubmergedFactor(panel);

                break;

            default:
                return null;
        }

        if (power == 0.0)
            return null;

        var behaviour = new SolarPanelBehaviour();
        if (panel.useCurve)
            behaviour.PowerCurve = panel.powerCurve;
        behaviour.TimeEfficiencyCurve = panel.timeEfficCurve;

        behaviour.ResourceName = panel.resourceName;
        behaviour.FlowMode = ResourceFlowMode.ALL_VESSEL_BALANCE;
        behaviour.MaxError = MaxError;
        behaviour.LaunchUT = panel.launchUT;
        behaviour.ChargeRate = power;

        return new(behaviour);
    }

    /// <summary>
    /// Get the net electricity production for this panel if it were in
    /// Kerbin's orbit.
    /// </summary>
    /// <param name="panel"></param>
    /// <returns></returns>
    /// <exception cref="NotImplementedException"></exception>
    public double GetOrbitAoAFactor(ModuleDeployableSolarPanel panel)
    {
        var start = panel.panelRotationTransform.position;
        return GetOptimalPanelAoAFactor(panel, GetSunDirection(start));
    }

    /// <summary>
    /// Can this tracking solar panel see the sun, if it were rotated optimally?
    /// </summary>
    /// <returns></returns>
    private bool GetPanelLOS(ModuleDeployableSolarPanel panel, Vector3d direction, int layerMask)
    {
        // This is a bit of a hack. We don't want to include the part in
        // the raycast so we temporarily disable just its collider for
        // the rest of the method.
        using var guard = new DisablePartCollider(panel.part);

        var secondary = GetSecondaryTransform(panel);
        var tracking = panel.trackingTransformLocal;
        var rotation = panel.panelRotationTransform;

        Ray ray = new(secondary.position + direction * panel.raycastOffset, direction);

        return !Physics.Raycast(ray, float.MaxValue, layerMask);
    }

    /// <summary>
    /// Get the optimal panel AoA given the provided vector pointing towards
    /// the sun. The sun vector must be normalized.
    /// </summary>
    public double GetOptimalPanelAoAFactor(ModuleDeployableSolarPanel panel, Vector3 sun)
    {
        if (panel.isTracking)
        {
            panel.panelRotationTransform.rotation.ToAngleAxis(out var _, out var axis);
            return Vector3.Cross(axis, sun).magnitude;
        }

        switch (panel.panelType)
        {
            case ModuleDeployableSolarPanel.PanelType.FLAT:
                if (panel.trackingDotTransform != null)
                    return Mathf.Clamp01(Vector3.Dot(panel.trackingDotTransform.forward, sun));

                LogUtil.Log("Panel has no tracking dot transform");

                // KSP seems to not set the AoA in this case?
                return 0;

            case ModuleDeployableSolarPanel.PanelType.CYLINDRICAL:
                var lhs = panel.alignType switch
                {
                    ModuleDeployablePart.PanelAlignType.PIVOT => panel.trackingDotTransform.forward,
                    ModuleDeployablePart.PanelAlignType.X => panel.part.partTransform.right,
                    ModuleDeployablePart.PanelAlignType.Y => panel.part.partTransform.up,
                    ModuleDeployablePart.PanelAlignType.Z => panel.part.partTransform.forward,
                    _ => Vector3.zero,
                };

                return (1f - Mathf.Abs(Vector3.Dot(lhs, sun))) * (1f / Mathf.PI);

            case ModuleDeployableSolarPanel.PanelType.SPHERICAL:
                return 0.25;

            default:
                return 0;
        }
    }

    /// <summary>
    /// Get the reduction factor in power produced due to the panel being
    /// underwater.
    /// </summary>
    /// <returns></returns>
    public double GetSubmergedFactor(ModuleDeployableSolarPanel panel)
    {
        if (panel.part.submergedPortion <= 0.0)
            return 1.0;

        var secondary = GetSecondaryTransform(panel).position;
        var depth = -FlightGlobals.getAltitudeAtPos((Vector3d)secondary, panel.vessel.mainBody);
        var depthFactor = Math.Min(0.5, (depth * 3.0 + panel.part.maxDepth) * 0.25);
        var attenuation = 1.0 / (1.0 + depthFactor * panel.part.vessel.mainBody.oceanDensity);

        if (panel.part.submergedPortion < 1.0)
            return UtilMath.LerpUnclamped(1.0, attenuation, panel.part.submergedPortion);
        else
            return attenuation;
    }

    /// <summary>
    /// Compute the direction vectors for sunset, noon, and sunrise for the
    /// current location of a landed vessel.
    /// </summary>
    ///
    /// <remarks>
    /// This assumes that the solar direction vector is not inclined relative
    /// to the planet. This is not always the case but should generally be
    /// good enough
    /// </remarks>
    public void ComputeLandedSunVectors(
        ModuleDeployableSolarPanel panel,
        out Vector3 sunrise,
        out Vector3 noon,
        out Vector3 sunset
    )
    {
        var vessel = panel.vessel;
        var planet = vessel.mainBody;

        var normal = (vessel.orbit.pos - planet.position).normalized;
        var up = planet.transformUp;

        sunrise = Vector3d.Cross(normal, up);
        noon = Vector3d.Cross(up, sunrise);
        sunset = -sunrise;
    }

    // Get the direction towards the Sun.
    protected virtual Vector3d GetSunDirection(Vector3 start)
    {
        var scaled = ScaledSpace.LocalToScaledSpace(start);
        var sun = Planetarium.fetch.Sun.scaledBody.transform.position;

        return (sun - scaled).normalized;
    }

    private static readonly FieldInfo SecondaryTransformField =
        typeof(ModuleDeployableSolarPanel).GetField("secondaryTransform", Flags);

    private static Transform GetSecondaryTransform(ModuleDeployableSolarPanel panel)
    {
        return (Transform)SecondaryTransformField.GetValue(panel);
    }

    private readonly ref struct DisablePartCollider : IDisposable
    {
        readonly Part part;
        readonly bool enabled;

        public DisablePartCollider(Part part)
        {
            this.part = part;
            this.enabled = part.collider.enabled;

            part.collider.enabled = false;
        }

        public void Dispose()
        {
            part.collider.enabled = enabled;
        }
    }
}
