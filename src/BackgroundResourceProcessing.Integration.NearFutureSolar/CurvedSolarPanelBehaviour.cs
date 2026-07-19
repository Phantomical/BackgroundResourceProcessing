using BackgroundResourceProcessing.Behaviour;

namespace BackgroundResourceProcessing.Integration.NearFutureSolar;

/// <summary>
/// A behaviour matching the star model of NFS' <c>ModuleCurvedSolarPanel</c>:
/// the module is hardwired to <c>FlightGlobals.Bodies[0]</c> and scales its
/// output by <c>homeAltAboveSun²/altAboveSun²</c>, with no luminosity factor.
/// </summary>
///
/// <remarks>
/// Only used when the module actually runs its own FixedUpdate. In systems
/// with more than one star Kopernicus disables the module and replaces its
/// output with the KopernicusSolarPanel fixer, which the adapter models with
/// the standard multi-star <see cref="SolarPanelBehaviour"/> instead.
/// </remarks>
public class CurvedSolarPanelBehaviour : SolarPanelBehaviour
{
    protected override double GetSolarFluxFactor(VesselState state)
    {
        var sun = FlightGlobals.Bodies[0];
        var home = FlightGlobals.GetHomeBody();
        if (sun == null || home == null)
            return 0.0;

        var homeAlt = FlightGlobals.getAltitudeAtPos(home.position, sun);
        var alt = FlightGlobals.getAltitudeAtPos((Vector3d)Vessel.vesselTransform.position, sun);

        return (homeAlt * homeAlt) / (alt * alt);
    }
}
