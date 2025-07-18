using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection.Emit;
using HarmonyLib;
using LifeSupport;
using UnityEngine;

namespace BackgroundResourceProcessing.Integration.USILifeSupport;

[KSPAddon(KSPAddon.Startup.Instantly, true)]
public class Loader : MonoBehaviour
{
    void Awake()
    {
        Harmony harmony = new("BackgroundResourceProcessing.Integration.USILifeSupport");
        harmony.PatchAll(typeof(Loader).Assembly);
    }
}

[HarmonyPatch(typeof(LifeSupportMonitor))]
[HarmonyPatch("GetVesselStats")]
public static class LifeSupportMonitor_GetVesselStats_Patch
{
    static IEnumerable<CodeInstruction> Transpiler(
        IEnumerable<CodeInstruction> instructions,
        ILGenerator generator
    )
    {
        // This transpiler patches the following method:
        // https://github.com/UmbraSpaceIndustries/USI-LS/blob/main/Source/USILifeSupport/LifeSupportMonitor.cs#L132

        // First we need to determine the indices of the vesselSuppliesTimeLeft
        // and vesselEcTimeLeft variables. We do this by inspecting the arguments
        // of the call to LifeSupportMonitor.GetCrewStat.
        //
        // This should hopefully be resilient to future updates to the mod, though
        // there hasn't really been any commits made in the last half decade so
        // it probably won't be an issue.
        var matcher = new CodeMatcher(instructions, generator);
        matcher
            .MatchStartForward(
                new CodeMatch(
                    CodeInstruction.Call(
                        typeof(LifeSupportMonitor),
                        "GetCrewStat",
                        [
                            typeof(ProtoCrewMember),
                            typeof(Vessel),
                            typeof(double),
                            typeof(double),
                            typeof(double),
                            typeof(double),
                        ]
                    )
                )
            )
            .ThrowIfInvalid("Could not find call to LifeSupportMonitor.GetCrewStat");

        var suppliesTimeLoc = GetLoadInstruction(matcher, -4);
        var ecTimeLoc = GetLoadInstruction(matcher, -3);

        double dummy = 0;
        // We now know the locations of those variables. We just need to patch
        // in a call to the our helper method just before the loop that updates
        // crew settings.
        //
        // The easiest way to do that is to inject our new call _just_ before
        // the call to Vessel.GetVesselCrew().
        matcher
            .MatchStartBackwards(new CodeMatch(Callvirt<Vessel>(vessel => vessel.GetVesselCrew())))
            .ThrowIfInvalid("Could not find call to Vessel.GetVesselCrew")
            .Insert(
                [
                    // We know that the vessel is on top of the stack so we just
                    // duplicate it for our own uses.
                    new CodeInstruction(OpCodes.Dup),
                    // Now we just need to load the ref parameters from the slots
                    // we discovered earlier.
                    new CodeInstruction(OpCodes.Ldloca, suppliesTimeLoc),
                    new CodeInstruction(OpCodes.Ldloca, ecTimeLoc),
                    // And then make our call, which will remove the 3 parameters
                    // from the stack leaving it exactly as it was before.
                    CodeInstruction.Call<Vessel>(vessel =>
                        UpdateTimeLeftWrapper(vessel, ref dummy, ref dummy)
                    ),
                ]
            );

        return matcher.Instructions();
    }

    static LocalBuilder GetLoadInstruction(CodeMatcher matcher, int offset)
    {
        var inst = matcher.InstructionAt(offset);
        if (inst.opcode == OpCodes.Ldloc_S)
            return (LocalBuilder)inst.operand;
        if (inst.opcode == OpCodes.Ldloc)
            return (LocalBuilder)inst.operand;

        throw new Exception(
            $"Unable to determine store location as instruction at offset {offset} was not a ldloc instruction"
        );
    }

    static CodeInstruction Callvirt<T>(Expression<Action<T>> expression)
    {
        return new CodeInstruction(OpCodes.Callvirt, SymbolExtensions.GetMethodInfo(expression));
    }

    public static void UpdateTimeLeftWrapper(
        Vessel vessel,
        ref double suppliesTimeLeft,
        ref double ecTimeLeft
    )
    {
        double cachedSupplies = suppliesTimeLeft;
        double cachedEc = ecTimeLeft;

        try
        {
            UpdateTimeLeft(vessel, ref suppliesTimeLeft, ref ecTimeLeft);
        }
        catch (Exception e)
        {
            // Avoid breaking things if it turns out that we are actually broken.
            Debug.LogError(
                $"[BackgroundResourceProcessing] LifeSupportMonitor.GetVesselStats patch threw an exception: {e}"
            );

            suppliesTimeLeft = cachedSupplies;
            ecTimeLeft = cachedEc;
        }
    }

    static void UpdateTimeLeft(Vessel vessel, ref double suppliesTimeLeft, ref double ecTimeLeft)
    {
        var settings = HighLogic.CurrentGame?.Parameters.CustomParams<Settings>();
        if (!(settings?.EnableUSILSIntegration ?? false))
            return;

        var processor = vessel.FindVesselModuleImplementing<BackgroundResourceProcessor>();
        if (processor == null)
            return;

        var module = vessel.FindVesselModuleImplementing<ModuleBackgroundUSILifeSupport>();
        if (module == null)
            return;

        var original = processor.Converters;

        var simulator = processor.GetSimulator();
        var converters = simulator.Converters;

        if (module.EcConverterIndex < 0 || module.SupplyConverterIndex < 0)
            return;

        var ec = converters[module.EcConverterIndex];
        var supply = converters[module.SupplyConverterIndex];

        double? ecExhausted = null;
        double? supplyExhausted = null;
        foreach (var changepoint in simulator.Steps())
        {
            if (ec.rate < 1.0)
                ecExhausted ??= changepoint;
            if (supply.rate < 1.0)
                supplyExhausted ??= changepoint;

            if (ecExhausted != null && supplyExhausted != null)
                break;
        }

        var now = Planetarium.GetUniversalTime();

        if (vessel.loaded)
        {
            // If we've run out of a resource and the vessel is loaded then we
            // should just reuse the existing time left calculated by USI-LS.
            if (ecExhausted != now)
                ecTimeLeft = (ecExhausted ?? double.PositiveInfinity) - now;
            if (supplyExhausted != now)
                suppliesTimeLeft = (supplyExhausted ?? double.PositiveInfinity) - now;
        }
        else
        {
            // The simulator does not have any of the behaviours, so it is necessary
            // to get them out of the original resource processor
            var ecB = original[module.EcConverterIndex].Behaviour as USILifeSupportBehaviour;
            var supplyB =
                original[module.SupplyConverterIndex].Behaviour as USILifeSupportBehaviour;

            // If the vessel is not loaded then the lastSatisfied on the behaviours
            // will actually contain a relevant value and we can use that.
            var ecEffectTime = ecB?.lastSatisfied ?? ecExhausted ?? double.PositiveInfinity;
            var supplyEffectTime =
                supplyB?.lastSatisfied ?? supplyExhausted ?? double.PositiveInfinity;

            suppliesTimeLeft = supplyEffectTime - now;
            ecTimeLeft = ecEffectTime - now;
        }
    }
}

[HarmonyPatch(typeof(LifeSupportMonitor))]
[HarmonyPatch("GetResourceInVessel")]
public static class LifeSupportMonitor_GetResourceInVessel_Patch
{
    static bool Prefix(ref double __result, Vessel vessel, string resourceName)
    {
        var settings = HighLogic.CurrentGame?.Parameters.CustomParams<Settings>();
        if (!(settings?.EnableUSILSIntegration ?? false))
            return true;

        if (vessel == null)
            return true;

        if (vessel.loaded)
            return true;

        var processor = vessel.FindVesselModuleImplementing<BackgroundResourceProcessor>();
        if (processor == null)
            return true;

        processor.UpdateBackgroundState();
        var state = processor.GetResourceState(resourceName);
        __result = state.amount;

        return false;
    }
}
