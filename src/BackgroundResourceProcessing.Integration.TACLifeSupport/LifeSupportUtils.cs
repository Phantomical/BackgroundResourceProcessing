using Tac;

namespace BackgroundResourceProcessing.Integration.TACLifeSupport;

/// <summary>
/// Shared by <see cref="ModuleBackgroundTACLifeSupport"/> and
/// <see cref="VesselSimulationCache"/> — the two have to agree on which
/// converters exist or their depletion estimates drift apart.
/// </summary>
internal static class LifeSupportUtils
{
    /// <summary>
    /// Mirrors <c>LifeSupportController.NeedOxygen</c>.
    /// </summary>
    internal static bool NeedsOxygen(Vessel vessel, VesselInfo vesselInfo)
    {
        if (
            vessel.mainBody == FlightGlobals.GetHomeBody()
            || vessel.mainBody.atmosphereContainsOxygen
        )
        {
            double atmDensity;
            if (vessel.loaded)
            {
                atmDensity = vessel.atmDensity;
            }
            else
            {
                double seaLevelPressure = vessel.mainBody.GetPressure(0);
                if (seaLevelPressure <= 0)
                    return true;

                atmDensity = vessel.staticPressurekPa / seaLevelPressure;
            }

            // Thick enough to just open a window.
            if (atmDensity > 0.5)
                return false;

            // Thin, but the vents can make up the difference if there's EC to spare.
            if (
                atmDensity > 0.2
                && vesselInfo.remainingElectricity > vesselInfo.estimatedElectricityConsumptionRate
            )
                return false;
        }

        return true;
    }
}
