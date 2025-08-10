using System;
using System.Reflection;
using BackgroundResourceProcessing.Behaviour;
using BackgroundResourceProcessing.Converter;
using NearFutureSolar;
using UnityEngine;

namespace BackgroundResourceProcessing.Integration.NearFutureSolar;

public class BackgroundCurvedSolarPanel : BackgroundConverter<ModuleCurvedSolarPanel>
{
    private const BindingFlags Flags =
        BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    private static readonly FieldInfo PanelTransformsField =
        typeof(ModuleCurvedSolarPanel).GetField("panelTransforms", Flags);

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

    public override ModuleBehaviour GetBehaviour(ModuleCurvedSolarPanel panel)
    {
        if (!panel.isEnabled)
            return null;

        if (panel.Deployable && panel.State != ModuleDeployablePart.DeployState.EXTENDED)
            return null;

        var vessel = panel.vessel;
        var processor = vessel.FindVesselModuleImplementing<BackgroundResourceProcessor>();
        if (processor == null)
            return null;

        var star = processor.ShadowState?.Star;
        if (star == null)
            return null;

        double effectiveAoA = 0.0;
        double power = panel.TotalEnergyRate;
        var transforms = GetTransforms(panel);

        switch (vessel.situation)
        {
            case Vessel.Situations.DOCKED:
            case Vessel.Situations.ESCAPING:
            case Vessel.Situations.ORBITING:
            case Vessel.Situations.SUB_ORBITAL:
                // In orbit we assume the vessel maintains the same
                // orientation relative to the sun at all times.
                //
                // This isn't quite the same as how things behave in KSP,
                // but it is a reasonable simplification to make here.

                for (int i = 0; i < transforms.Length; ++i)
                {
                    var transform = transforms[i];
                    var direction = (star.transform.position - transform.position).normalized;

                    effectiveAoA += GetTransformAoAFactor(
                        panel,
                        transform,
                        direction,
                        orbitLayerMask
                    );
                }

                effectiveAoA /= transforms.Length;
                break;

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

                for (int i = 0; i < transforms.Length; ++i)
                {
                    var transform = transforms[i];

                    effectiveAoA +=
                        GetTransformAoAFactor(panel, transform, sunrise, landedLayerMask) * 0.125;
                    effectiveAoA +=
                        GetTransformAoAFactor(panel, transform, midpoint1, landedLayerMask) * 0.25;
                    effectiveAoA +=
                        GetTransformAoAFactor(panel, transform, noon, landedLayerMask) * 0.25;
                    effectiveAoA +=
                        GetTransformAoAFactor(panel, transform, midpoint2, landedLayerMask) * 0.25;
                    effectiveAoA +=
                        GetTransformAoAFactor(panel, transform, sunset, landedLayerMask) * 0.125;
                }

                effectiveAoA /= transforms.Length;

                break;

            default:
                return null;
        }

        power *= effectiveAoA;

        if (power == 0.0)
            return null;

        var behaviour = new SolarPanelBehaviour
        {
            ResourceName = panel.ResourceName,
            FlowMode = ResourceFlowMode.ALL_VESSEL_BALANCE,
            MaxError = MaxError,
            ChargeRate = power,
        };

        return new(behaviour);
    }

    private static double GetTransformAoAFactor(
        ModuleCurvedSolarPanel panel,
        Transform transform,
        Vector3d direction,
        int layerMask
    )
    {
        double AoA = Vector3.Dot(transform.forward, direction);
        if (AoA < 0.0)
            return 0.0;

        if (!GetTransformLOS(panel, transform, direction, layerMask))
            return 0.0;

        return AoA;
    }

    /// <summary>
    /// Can this tracking solar panel see the sun, if it were rotated optimally?
    /// </summary>
    /// <returns></returns>
    private static bool GetTransformLOS(
        ModuleCurvedSolarPanel panel,
        Transform transform,
        Vector3d direction,
        int layerMask
    )
    {
        // This is a bit of a hack. We don't want to include the part in
        // the raycast so we temporarily disable just its collider for
        // the rest of the method.
        using var guard = new DisablePartCollider(panel.part);

        Ray ray = new(transform.position, direction);
        return !Physics.Raycast(ray, float.MaxValue, layerMask);
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
        ModuleCurvedSolarPanel panel,
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

    private Transform[] GetTransforms(ModuleCurvedSolarPanel panel)
    {
        return (Transform[])PanelTransformsField.GetValue(panel);
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
