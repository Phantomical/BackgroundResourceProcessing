using BackgroundResourceProcessing.Collections;
using BackgroundResourceProcessing.Core;
using DF;
using HarmonyLib;
using UnityEngine;

namespace BackgroundResourceProcessing.Integration.DeepFreeze;

public struct DeepFreezerStats
{
    public double LastChangepoint;
    public InventoryState EcState;

    /// <summary>The UT at which EC is predicted to run out, or +Infinity.</summary>
    public double EcExhaustedUT;

    public readonly double GetEcAtUT(double UT) =>
        EcState.amount + EcState.rate * (UT - LastChangepoint);

    public static DeepFreezerStats SimulateVessel(BackgroundResourceProcessor processor)
    {
        var simulator = processor.GetSimulator();

        double? ecExhausted = null;
        foreach (var stepTime in simulator.Steps())
        {
            var ec = simulator.GetResourceState("ElectricCharge");
            if (ec.amount <= 0.0 && ec.rate <= 0.0)
            {
                ecExhausted = stepTime;
                break;
            }

            if (ec.rate < 0.0)
            {
                double zero = stepTime + ec.amount / -ec.rate;
                double next = simulator.NextChangepoint;
                if (double.IsPositiveInfinity(next) || zero <= next)
                {
                    ecExhausted = zero;
                    break;
                }
            }
        }

        return new DeepFreezerStats
        {
            LastChangepoint = processor.LastChangepoint,
            EcState = processor.GetResourceState("ElectricCharge"),
            EcExhaustedUT = ecExhausted ?? double.PositiveInfinity,
        };
    }
}

public class DeepFreezerSimulationCache : SimulationCache<DeepFreezerStats>
{
    static DeepFreezerSimulationCache Instance = null;

    public static DeepFreezerSimulationCache GetInstance()
    {
        if (Instance != null)
            return Instance;

        var gameObject = new GameObject("BRP DeepFreeze Simulation Cache");
        return gameObject.AddComponent<DeepFreezerSimulationCache>();
    }

    private new void Awake()
    {
        base.Awake();
        Instance ??= this;
    }

    private new void OnDestroy()
    {
        base.OnDestroy();
        if (ReferenceEquals(Instance, this))
            Instance = null;
    }
}

/// <summary>
/// Drives DeepFreeze's "LastUpd" and "TimeRem" UI columns off BRP's simulator.
/// DF's native calculation is a single-rate linear extrapolation of
/// <c>storedEC</c> — it has no knowledge of eclipses or other future
/// changepoints, and its per-vessel divisor omits <c>numFrznCrew</c>, so for
/// multi-kerbal freezers it reports TimeRem that is <c>numFrznCrew</c>× too
/// large. For unloaded vessels the native calc also relies on <c>storedEC</c>
/// being refreshed externally (previously by BackgroundResources), which we
/// suppress.
/// </summary>
[HarmonyPatch(typeof(DFIntMemory), "UpdatePredictedVesselEC")]
static class DFIntMemory_UpdatePredictedVesselEC_Patch
{
    static bool Prefix(VesselInfo vesselInfo, Vessel vessel, double currentTime)
    {
        var settings = HighLogic.CurrentGame?.Parameters.CustomParams<ModIntegrationSettings>();
        if (!(settings?.EnableDeepFreezeIntegration ?? false))
            return true;

        if (vessel == null)
            return true;

        var processor = vessel.FindVesselModuleImplementing<BackgroundResourceProcessor>();
        if (processor == null)
            return true;

        var cache = DeepFreezerSimulationCache.GetInstance();
        var stats = cache.GetVesselEntry(processor, DeepFreezerStats.SimulateVessel);

        vesselInfo.predictedECOut = double.IsPositiveInfinity(stats.EcExhaustedUT)
            ? double.PositiveInfinity
            : stats.EcExhaustedUT - currentTime;

        // For loaded vessels, the live DeepFreezer PartModules are authoritative
        // for storedEC and partInfo, so only override predictedECOut.
        if (vessel.loaded)
            return false;

        double storedEC = stats.GetEcAtUT(currentTime);
        vesselInfo.storedEC = storedEC;
        vesselInfo.lastUpdate = currentTime;

        bool ecAvailable = storedEC > 0.0 || stats.EcState.rate > 0.0;
        foreach (var frzr in DF.DeepFreeze.Instance.KnownFreezerParts)
        {
            if (frzr.Value.vesselID != vessel.id)
                continue;

            var partInfo = frzr.Value;
            partInfo.lastUpdate = currentTime;

            if (ecAvailable)
            {
                partInfo.timeLastElectricity = currentTime;
                partInfo.outofEC = false;
                partInfo.deathCounter = currentTime;
            }
            else
            {
                partInfo.outofEC = true;
            }
        }

        return false;
    }
}
