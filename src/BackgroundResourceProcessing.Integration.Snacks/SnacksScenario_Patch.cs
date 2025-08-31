using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BackgroundResourceProcessing.Behaviour;
using BackgroundResourceProcessing.Collections;
using BackgroundResourceProcessing.Core;
using HarmonyLib;
using Smooth.Collections;
using Snacks;
using UnityEngine;

namespace BackgroundResourceProcessing.Integration.Snacks;

class SnackshotCache : SimulationCache<SnackshotCache.CachedVesselSnackshot>
{
    internal class CachedSnackshot
    {
        public string resourceName;
        public InventoryState state;
        public double exhaustedUT;
        public bool showInSnapshot = false;

        public Snackshot GetSnackshotAtUT(double UT, double recordedUT)
        {
            return new()
            {
                resourceName = resourceName,
                amount = state.GetAmountAfterTime(UT - recordedUT),
                maxAmount = state.maxAmount,
                showTimeRemaining = showInSnapshot,
                // Snacks treats negative estimatedTimeRemaining as having
                // an infinite estimated time.
                estimatedTimeRemaining = double.IsPositiveInfinity(exhaustedUT)
                    ? double.NegativeInfinity
                    : Math.Max(exhaustedUT - UT, 0.0),
                isSimulatorRunning = false,
            };
        }
    }

    internal struct CachedVesselSnackshot
    {
        public double recordedUT;
        public List<CachedSnackshot> snackshots;
    }

    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly MethodInfo CreateVesselSnackshotMethod =
        typeof(SnacksScenario).GetMethod("createVesselSnackshot", Flags);

    public VesselSnackshot GetSimulatedSnackshot(SnacksScenario scenario, Vessel vessel)
    {
        var snackshot = CreateVesselSnackshot(scenario, vessel);
        if (snackshot == null)
            return null;

        var UT = Planetarium.GetUniversalTime();
        var cached = GetVesselEntry(
            vessel,
            processor => ComputeCachedSnackshot(scenario, processor)
        );

        snackshot.snackshots = cached
            .snackshots.Select(s => s.GetSnackshotAtUT(UT, cached.recordedUT))
            .ToList();
        snackshot.convertersAssumedActive = false;

        return snackshot;
    }

    private CachedVesselSnackshot ComputeCachedSnackshot(
        SnacksScenario scenario,
        BackgroundResourceProcessor processor
    )
    {
        var vessel = processor.Vessel;
        var simulator = processor.GetSimulator();
        var crewCount = vessel.GetCrewCount();

        if (crewCount > 0)
        {
            VesselState vesselState = null;
            foreach (var resp in scenario.resourceProcessors)
            {
                List<ResourceRatio> inputs = [];
                List<ResourceRatio> outputs = [];

                resp.AddConsumedAndProducedResources(crewCount, 1.0, inputs, outputs);

                // Not entirely sure how this is supposed to work in the simulator but this
                // most closely matches how it seems to be simulated.
                for (int i = 0; i < outputs.Count; ++i)
                    outputs[i] = outputs[i] with { DumpExcess = true };

                var converter = new Core.ResourceConverter(
                    new ConstantConverter(inputs, outputs) { SourceModule = "SnacksScenario" }
                );
                simulator.AddConverter(
                    converter,
                    vesselState ??= processor.GetVesselState(processor.LastChangepoint),
                    new AddConverterOptions() { LinkToAll = true }
                );
            }

            simulator.ComputeInitialRates();
        }

        Dictionary<string, CachedSnackshot> snackshots = [];
        snackshots.AddAll(
            simulator
                .GetResourceStates()
                .Select(entry =>
                {
                    var (resource, state) = entry;
                    var snackshot = new CachedSnackshot()
                    {
                        resourceName = resource,
                        state = state,
                        exhaustedUT = double.PositiveInfinity,
                    };

                    return new KeyValuePair<string, CachedSnackshot>(resource, snackshot);
                })
        );

        foreach (var resp in scenario.resourceProcessors)
        {
            foreach (var input in resp.inputList)
            {
                if (input.showInSnapshot)
                    if (snackshots.TryGetValue(input.resourceName, out var snackshot))
                        snackshot.showInSnapshot = true;
            }

            foreach (var output in resp.outputList)
            {
                if (output.showInSnapshot)
                    if (snackshots.TryGetValue(output.resourceName, out var snackshot))
                        snackshot.showInSnapshot = true;
            }
        }

        foreach (var UT in simulator.Steps())
        {
            var states = simulator.GetResourceStates();
            foreach (var (resource, state) in states)
            {
                if (state.amount != 0.0 || state.rate >= 0.0)
                    continue;
                if (!snackshots.TryGetValue(resource, out var snackshot))
                    continue;
                snackshot.exhaustedUT = Math.Min(snackshot.exhaustedUT, UT);
            }
        }

        return new()
        {
            recordedUT = processor.LastChangepoint,
            snackshots = [.. snackshots.Values.Where(snackshot => snackshot.showInSnapshot)],
        };
    }

    private VesselSnackshot CreateVesselSnackshot(SnacksScenario scenario, Vessel vessel)
    {
        return (VesselSnackshot)CreateVesselSnackshotMethod.Invoke(scenario, [vessel]);
    }
}

[HarmonyPatch(typeof(SnacksScenario), "updateSnapshots")]
static class SnacksScenario_UpdateSnapshots_Patch
{
    static IEnumerator<YieldInstruction> Postfix(
        IEnumerator<YieldInstruction> __result,
        SnacksScenario __instance
    )
    {
        var settings = HighLogic.CurrentGame.Parameters.CustomParams<ModIntegrationSettings>();
        if (!(settings?.EnableWildBlueIntegration ?? false))
            return __result;

        return UpdateSnapshots(__instance);
    }

    static IEnumerator<YieldInstruction> UpdateSnapshots(SnacksScenario scenario)
    {
        var cache = scenario.gameObject.AddOrGetComponent<SnackshotCache>();

        foreach (var vessel in FlightGlobals.Vessels)
        {
            cache.GetSimulatedSnackshot(scenario, vessel);
            yield return new WaitForFixedUpdate();
        }
    }
}
