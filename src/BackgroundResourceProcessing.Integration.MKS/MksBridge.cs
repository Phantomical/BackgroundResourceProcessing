using System;
using System.Collections.Generic;
using PlanetaryLogistics;
using USITools;

namespace BackgroundResourceProcessing.Integration.MKS;

/// <summary>
/// Thin, defensive bridge to MKS's Planetary Logistics data and warehouse
/// modules. Every MKS access is guarded so that a missing or not-yet-initialised
/// MKS scenario degrades to "no pool" rather than throwing.
/// </summary>
internal static class MksBridge
{
    /// <summary>
    /// Resources MKS never moves through Planetary Logistics
    /// (<c>ModulePlanetaryLogistics._blackList</c>).
    /// </summary>
    private static readonly HashSet<string> Blacklist =
    [
        "EnrichedUranium",
        "DepletedFuel",
        "Construction",
        "ReplacementParts",
        "ElectricCharge",
    ];

    public static bool IsBlacklisted(string resource) => Blacklist.Contains(resource);

    /// <summary>
    /// Whether changes to MKS's planetary logistics data will stick in the
    /// current scene.
    /// </summary>
    ///
    /// <remarks>
    /// MKS only adds its scenario to the space center, editor and flight
    /// scenes. In any other scene (notably the tracking station)
    /// <see cref="PlanetaryLogisticsScenario.Instance"/> is left over from the
    /// previous scene, and has been destroyed. Reading from it gives the
    /// values as of when that scene was saved, but anything written to it is
    /// discarded on the next scene load.
    /// </remarks>
    public static bool CanWrite
    {
        get
        {
            switch (HighLogic.LoadedScene)
            {
                case GameScenes.FLIGHT:
                case GameScenes.SPACECENTER:
                    break;
                default:
                    return false;
            }

            return PlanetaryLogisticsScenario.Instance != null;
        }
    }

    /// <summary>
    /// The current amount stored in a planetary pool, or 0 if MKS is
    /// unavailable / no entry exists.
    /// </summary>
    public static double GetStored(int bodyIndex, string resource)
    {
        // In the tracking station the scenario from the previous scene has
        // been destroyed, so Unity's == reports it as null, but its data is
        // still the last saved state and is what we want to read.
        if (ReferenceEquals(PlanetaryLogisticsScenario.Instance, null))
            return 0.0;

        try
        {
            var mgr = PlanetaryLogisticsManager.Instance;
            if (!mgr.DoesLogEntryExist(resource, bodyIndex))
                return 0.0;
            return mgr.FetchLogEntry(resource, bodyIndex).StoredQuantity;
        }
        catch (Exception e)
        {
            LogUtil.Error($"[MKS] Failed to read planetary logistics entry: {e}");
            return 0.0;
        }
    }

    /// <summary>
    /// Add <paramref name="delta"/> to a planetary pool, clamping it at 0.
    /// </summary>
    ///
    /// <returns>Whether the change was applied.</returns>
    public static bool AddStored(int bodyIndex, string resource, double delta)
    {
        if (!CanWrite)
            return false;

        try
        {
            var mgr = PlanetaryLogisticsManager.Instance;
            var entry = mgr.FetchLogEntry(resource, bodyIndex);
            entry.StoredQuantity = Math.Max(entry.StoredQuantity + delta, 0.0);
            mgr.TrackLogEntry(entry);
            return true;
        }
        catch (Exception e)
        {
            LogUtil.Error($"[MKS] Failed to write planetary logistics entry: {e}");
            return false;
        }
    }

    /// <summary>
    /// The resources this vessel shares with the planetary pool, mapped to the
    /// flight ids of the warehouse parts that hold them. This mirrors
    /// <c>ModulePlanetaryLogistics.FixedUpdate</c>: resources on a warehouse
    /// part with planetary transfer enabled, minus the MKS blacklist.
    /// </summary>
    ///
    /// <remarks>
    /// Locked resources are skipped. MKS dumps a locked warehouse's contents
    /// into the pool all at once, which it has already done by the time the
    /// vessel gets unloaded.
    /// </remarks>
    public static Dictionary<string, List<uint>> GetPooledWarehouses(Vessel vessel)
    {
        var result = new Dictionary<string, List<uint>>();
        if (vessel == null)
            return result;

        var warehouses = vessel.FindPartModulesImplementing<USI_ModuleResourceWarehouse>();
        foreach (var warehouse in warehouses)
        {
            if (!warehouse.soiTransferEnabled)
                continue;

            uint flightId = warehouse.part.flightID;
            foreach (PartResource res in warehouse.part.Resources)
            {
                if (IsBlacklisted(res.resourceName))
                    continue;
                if (!res.flowState)
                    continue;

                if (!result.TryGetValue(res.resourceName, out var ids))
                    result[res.resourceName] = ids = [];
                if (!ids.Contains(flightId))
                    ids.Add(flightId);
            }
        }

        return result;
    }

    /// <summary>
    /// Whether a kerbal with the logistics skill is within 500m of the vessel,
    /// which MKS requires in order to pull resources out of the pool.
    /// </summary>
    ///
    /// <remarks>
    /// This is evaluated when the vessel is recorded, so a kerbal on a nearby
    /// vessel counts for as long as this vessel stays unloaded.
    /// </remarks>
    public static bool HasLogisticsCrew(Vessel vessel)
    {
        if (vessel == null)
            return false;

        try
        {
            return LogisticsTools.NearbyCrew(vessel, 500f, "LogisticsSkill");
        }
        catch (Exception e)
        {
            LogUtil.Error($"[MKS] Failed to check for logistics crew: {e}");
            return false;
        }
    }
}
