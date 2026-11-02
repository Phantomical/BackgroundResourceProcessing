using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
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

    const string KopernicusSolarPanelModule = "KopernicusSolarPanel";

    static bool GetKopernicusROFlag(PartModule fixer)
    {
        var field = fixer.GetType().GetField("ROFlag", Flags);
        return field?.GetValue(fixer) is bool flag && flag;
    }

    static bool GetKopernicusManualTracking(PartModule fixer)
    {
        var field = fixer.GetType().GetField("manualTracking", Flags);
        return field?.GetValue(fixer) is bool flag && flag;
    }

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

        // This panel is not tracking a celestial body. We don't know how to
        // handle this case (and neither does the base game).
        if (panel.trackingBody == null)
            return null;

        // This panel is not tracking a star.
        if (!panel.trackingBody.isStar)
            return null;

        var vessel = panel.vessel;

        // Kopernicus' multi-star fixer (only added when there is more than
        // one star) zeroes the stock module's output and generates power
        // itself from chargeRate and the normalized stellar flux alone. It
        // does not apply flowMult, efficiencyMult or the stock powerCurve.
        var kopernicusFixer = panel.part.Modules.GetModule(KopernicusSolarPanelModule);

        double power = panel.chargeRate;
        if (kopernicusFixer == null)
            power *= panel.flowMult * panel.efficiencyMult;

        // The fixer sums the light from every star and automatically points
        // the panel at the brightest one that is visible, so record the
        // panel's exposure to each star instead of a single angle of attack.
        List<SolarPanelBehaviour.StarTracking> tracking = null;

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
                if (kopernicusFixer != null)
                {
                    tracking = GetOrbitStarTracking(panel, kopernicusFixer);
                    break;
                }

                var sun = GetTargetDirection(panel);
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
                if (kopernicusFixer != null)
                {
                    tracking = GetLandedStarTracking(panel, kopernicusFixer);
                }
                else if (OnTidallyLockedPlanet(panel, panel.trackingBody))
                {
                    // If we're tracking the parent star of a tidally locked
                    // planet then we can use our current orientation.

                    sun = GetTargetDirection(panel);
                    power *= GetOptimalPanelAoAFactor(panel, sun);
                }
                else
                {
                    power *= GetLandedAveragedAoAFactor(panel);
                }

                // It's possible to be both LANDED and underwater, so we
                // can't constrain this check to only when splashed.
                power *= GetSubmergedFactor(panel);

                if (vessel.mainBody.atmosphere)
                    power *= GetAtmosphericAttenuationFactor(panel);

                break;

            default:
                return null;
        }

        if (power == 0.0)
            return null;
        if (tracking != null && !HasAnyExposure(tracking))
            return null;

        var behaviour = new SolarPanelBehaviour { Tracking = tracking };
        if (panel.useCurve && kopernicusFixer == null)
            behaviour.PowerCurve = panel.powerCurve;
        // The Kopernicus fixer only applies panel wear when ROFlag is set.
        if (kopernicusFixer == null || GetKopernicusROFlag(kopernicusFixer))
            behaviour.TimeEfficiencyCurve = panel.timeEfficCurve;

        behaviour.ResourceName = panel.resourceName;
        behaviour.FlowMode = ResourceFlowMode.ALL_VESSEL_BALANCE;
        behaviour.MaxError = MaxError;
        behaviour.LaunchUT = panel.launchUT;
        behaviour.ChargeRate = power;
        behaviour.StarName = panel.trackingBody.bodyName;

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
        var target = panel.trackingBody.scaledBody.transform.position;
        var sun = (target - start).normalized;

        return GetOptimalPanelAoAFactor(panel, sun);
    }

    /// <summary>
    /// Compute the panel's exposure to every relevant star, for each star
    /// that the panel could be pointed at, while in orbit.
    /// </summary>
    ///
    /// <remarks>
    /// A tracking panel rotates about its pivot axis to face the star it is
    /// tracking. The light it then gets from another star depends on the
    /// angle between that star and the panel's facing.
    /// </remarks>
    private List<SolarPanelBehaviour.StarTracking> GetOrbitStarTracking(
        ModuleDeployableSolarPanel panel,
        PartModule fixer
    )
    {
        var stars = GetCandidateStars(panel);
        var directions = new Vector3d[stars.Count];
        var visible = new bool[stars.Count];

        for (int i = 0; i < stars.Count; ++i)
        {
            directions[i] = GetTargetDirection(panel, stars[i]);
            visible[i] = GetPanelLOS(panel, directions[i], orbitLayerMask);
        }

        List<SolarPanelBehaviour.StarTracking> tracking = [];
        for (int i = 0; i < stars.Count; ++i)
        {
            if (!IsTrackable(panel, fixer, stars[i]))
                continue;

            var entry = new SolarPanelBehaviour.StarTracking { StarName = stars[i].bodyName };
            for (int j = 0; j < stars.Count; ++j)
            {
                double factor = 0.0;
                if (visible[j])
                    factor = GetTrackedAoAFactor(panel, directions[i], directions[j], i == j);

                entry.Exposures.Add(new() { StarName = stars[j].bodyName, Factor = factor });
            }

            tracking.Add(entry);
        }

        return tracking;
    }

    /// <summary>
    /// Compute the panel's exposure to every relevant star while landed.
    /// </summary>
    ///
    /// <remarks>
    /// Over a day every star sweeps across the sky, so the exposure to each
    /// star is approximated by the same day-averaged AoA used for a single
    /// star, regardless of which star the panel is pointed at. On a planet
    /// that is tidally locked to a star, that star stays fixed in the sky and
    /// the panel's current orientation is used instead.
    /// </remarks>
    private List<SolarPanelBehaviour.StarTracking> GetLandedStarTracking(
        ModuleDeployableSolarPanel panel,
        PartModule fixer
    )
    {
        var stars = GetCandidateStars(panel);
        double? averaged = null;

        List<SolarPanelBehaviour.StarExposure> exposures = [];
        foreach (var star in stars)
        {
            double factor;
            if (OnTidallyLockedPlanet(panel, star))
                factor = GetOptimalPanelAoAFactor(panel, GetTargetDirection(panel, star));
            else
                factor = averaged ??= GetLandedAveragedAoAFactor(panel);

            exposures.Add(new() { StarName = star.bodyName, Factor = factor });
        }

        List<SolarPanelBehaviour.StarTracking> tracking = [];
        foreach (var star in stars)
        {
            if (!IsTrackable(panel, fixer, star))
                continue;

            tracking.Add(new() { StarName = star.bodyName, Exposures = [.. exposures] });
        }

        return tracking;
    }

    /// <summary>
    /// Get the stars that are relevant to this panel. This is every star that
    /// is bright enough to matter, along with the star the panel is currently
    /// tracking.
    /// </summary>
    private static List<CelestialBody> GetCandidateStars(ModuleDeployableSolarPanel panel)
    {
        List<CelestialBody> stars = [];
        foreach (var star in ShadowState.StarProvider.GetRelevantStars(panel.vessel) ?? [])
        {
            if (star != null && star.isStar && !stars.Contains(star))
                stars.Add(star);
        }

        if (!stars.Contains(panel.trackingBody))
            stars.Add(panel.trackingBody);

        return stars;
    }

    /// <summary>
    /// Can the panel end up pointed at <paramref name="star"/>? With manual
    /// tracking enabled the fixer leaves the panel pointed at its current
    /// target instead of following the brightest star.
    /// </summary>
    private static bool IsTrackable(
        ModuleDeployableSolarPanel panel,
        PartModule fixer,
        CelestialBody star
    )
    {
        if (!GetKopernicusManualTracking(fixer))
            return true;

        return ReferenceEquals(star, panel.trackingBody);
    }

    private static bool HasAnyExposure(List<SolarPanelBehaviour.StarTracking> tracking)
    {
        foreach (var entry in tracking)
        {
            foreach (var exposure in entry.Exposures)
            {
                if (exposure.Factor > 0.0)
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Get the AoA factor for light coming from <paramref name="star"/> while
    /// the panel is pointed at <paramref name="tracked"/>. Both directions
    /// must be normalized.
    /// </summary>
    private double GetTrackedAoAFactor(
        ModuleDeployableSolarPanel panel,
        Vector3d tracked,
        Vector3d star,
        bool same
    )
    {
        if (same || !panel.isTracking)
            return GetOptimalPanelAoAFactor(panel, star);

        // The panel rotates about its pivot axis until it faces the tracked
        // star as closely as it can, so it ends up facing the component of
        // the tracked direction that is perpendicular to that axis.
        Vector3d axis = panel.panelRotationTransform.up;
        var facing = tracked - axis * Vector3d.Dot(axis, tracked);
        if (facing.magnitude < 1e-6)
            return 0.0;

        return Math.Max(Vector3d.Dot(facing.normalized, star), 0.0);
    }

    /// <summary>
    /// So landed solar panels require some special consideration. We don't
    /// want to constantly be re-evaluating panel AoAs but at the same time we
    /// want to at least be somewhat accurate.
    /// </summary>
    ///
    /// <remarks>
    /// The solution I've gone with here is to evaluate the panel's AoA at 5
    /// different points and then used a weighted average of those points to
    /// compute the effective AoA factor.
    /// </remarks>
    private double GetLandedAveragedAoAFactor(ModuleDeployableSolarPanel panel)
    {
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

        return weightedAoA;
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
            var axis = panel.panelRotationTransform.up;
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
    /// Get the average factor by which sunlight is attenuated by the
    /// atmosphere across the day.
    /// </summary>
    /// <param name="panel"></param>
    /// <returns></returns>
    public virtual double GetAtmosphericAttenuationFactor(ModuleDeployableSolarPanel panel)
    {
        return 1.0;
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

    private Vector3d GetTargetDirection(ModuleDeployableSolarPanel panel) =>
        GetTargetDirection(panel, panel.trackingBody);

    private static Vector3d GetTargetDirection(ModuleDeployableSolarPanel panel, CelestialBody body)
    {
        var start = ScaledSpace.LocalToScaledSpace(panel.panelRotationTransform.position);
        var target = body.scaledBody.transform.position;

        return (target - start).normalized;
    }

    /// <summary>
    /// Is this vessel's parent planet tidally locked to the target star?
    /// </summary>
    private static bool OnTidallyLockedPlanet(
        ModuleDeployableSolarPanel panel,
        CelestialBody target
    )
    {
        var vessel = panel.vessel;
        var parent = vessel.orbit.referenceBody;

        if (parent == null)
            return false;

        if (!ReferenceEquals(parent.orbit.referenceBody, target))
            return false;

        return parent.tidallyLocked;
    }

    private static readonly FieldInfo SecondaryTransformField =
        typeof(ModuleDeployableSolarPanel).GetField("secondaryTransform", Flags);

    private static Transform GetSecondaryTransform(ModuleDeployableSolarPanel panel)
    {
        return (Transform)SecondaryTransformField.GetValue(panel);
    }

    private readonly struct DisablePartCollider : IDisposable
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
