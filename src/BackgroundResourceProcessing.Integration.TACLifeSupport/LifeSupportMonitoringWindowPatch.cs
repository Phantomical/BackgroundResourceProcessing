using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using BackgroundResourceProcessing.Collections;
using HarmonyLib;
using Tac;
using UnityEngine;

namespace BackgroundResourceProcessing.Integration.TACLifeSupport
{
    public struct SavedVesselInfo { }

    /// <summary>
    /// Transpiler patch for TAC-LS LifeSupportMonitoringWindow.DrawWindowContents
    /// to update VesselInfo with BRP simulation results before display
    /// </summary>
    [HarmonyPatch]
    public static class LifeSupportMonitoringWindow_DrawWindowContents_Patch
    {
        const BindingFlags Flags =
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

        static readonly MethodInfo ListItemGetter = typeof(List<KeyValuePair<Guid, VesselInfo>>)
            .GetProperty("Item")
            .GetMethod;
        static readonly MethodInfo DrawWindowContents;
        static readonly MethodInfo DrawVesselInfo;

        static LifeSupportMonitoringWindow_DrawWindowContents_Patch()
        {
            var assembly = typeof(VesselInfo).Assembly;
            var window = assembly
                .GetTypes()
                .Where(type => type.Name == "LifeSupportMonitoringWindow")
                .First();

            DrawWindowContents = window.GetMethod("DrawWindowContents", Flags);
            DrawVesselInfo = window.GetMethod("DrawVesselInfo", Flags);
        }

        static MethodInfo TargetMethod() => DrawWindowContents;

        /// <summary>
        /// Transpiler that injects our BRP integration call after getting each KeyValuePair from the list
        /// </summary>
        static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions,
            ILGenerator generator
        )
        {
            var matcher = new CodeMatcher(instructions, generator);
            var slot = generator.DeclareLocal(typeof(VesselStats?));
            var info = generator.DeclareLocal(typeof(VesselInfo));

            // Find the get_Item call that retrieves KeyValuePair from knownVesselsList
            matcher
                .MatchStartForward(new CodeMatch(OpCodes.Callvirt, ListItemGetter))
                .ThrowIfInvalid("Could not find get_Item call for knownVesselsList");

            VesselStats? stats = default;
            // Insert our call right after get_Item but before stloc.2
            // Stack at this point has: KeyValuePair<Guid, VesselInfo>
            matcher
                .Advance(1) // Move past the get_Item call
                .Insert(
                    [
                        new CodeInstruction(OpCodes.Dup), // Duplicate the KeyValuePair on stack\
                        new CodeInstruction(OpCodes.Ldarg_0),
                        new CodeInstruction(OpCodes.Ldloca, slot.LocalIndex),
                        CodeInstruction.Call(() =>
                            OverrideVesselInfoEstimates(default, default, out stats)
                        ),
                        new CodeInstruction(OpCodes.Stloc, info.LocalIndex),
                    ]
                );

            matcher
                .MatchStartForward(new CodeMatch(OpCodes.Call, DrawVesselInfo))
                .ThrowIfInvalid("Could not find call to DrawVesselInfo");

            matcher
                .Advance(1)
                .Insert(
                    [
                        new CodeInstruction(OpCodes.Ldloc, info.LocalIndex),
                        new CodeInstruction(OpCodes.Ldloc, slot.LocalIndex),
                        CodeInstruction.Call(() => RestoreVesselInfo(null, null)),
                    ]
                );

            return matcher.Instructions();
        }

        public static VesselInfo OverrideVesselInfoEstimates(
            KeyValuePair<Guid, VesselInfo> entry,
            MonoBehaviour window,
            out VesselStats? saved
        )
        {
            saved = null;
            var (vesselId, info) = entry;

            var settings = HighLogic.CurrentGame?.Parameters.CustomParams<ModIntegrationSettings>();
            if (!(settings?.EnableTACLSIntegration ?? false))
                return info;

            try
            {
                var cache = VesselSimulationCache.GetInstance();
                var vessel = FindVesselWithId(vesselId);
                if (vessel == null || info == null)
                    return info;

                var _stats = cache.GetCachedVesselStats(vessel, info);
                if (_stats == null)
                    return info;
                var stats = _stats.Value;

                saved = new()
                {
                    FoodExhaustedUT = info.estimatedTimeFoodDepleted,
                    WaterExhaustedUT = info.estimatedTimeWaterDepleted,
                    OxygenExhaustedUT = info.estimatedTimeOxygenDepleted,
                    ElectricityExhaustedUT = info.estimatedTimeElectricityDepleted,
                };

                info.estimatedTimeFoodDepleted = stats.FoodExhaustedUT;
                info.estimatedTimeWaterDepleted = stats.WaterExhaustedUT;
                info.estimatedTimeOxygenDepleted = stats.OxygenExhaustedUT;
                info.estimatedTimeElectricityDepleted = stats.ElectricityExhaustedUT;
            }
            catch (Exception e)
            {
                LogUtil.Error($"Computing vessel info estimates threw an exception: {e}");
            }

            return info;
        }

        public static void RestoreVesselInfo(VesselInfo info, VesselStats? saved)
        {
            if (saved is null)
                return;

            var cached = saved.Value;
            info.estimatedTimeFoodDepleted = cached.FoodExhaustedUT;
            info.estimatedTimeWaterDepleted = cached.WaterExhaustedUT;
            info.estimatedTimeOxygenDepleted = cached.OxygenExhaustedUT;
            info.estimatedTimeElectricityDepleted = cached.ElectricityExhaustedUT;
        }

        static Vessel FindVesselWithId(Guid id)
        {
            foreach (var vessel in FlightGlobals.Vessels)
            {
                if (vessel.id == id)
                    return vessel;
            }

            return null;
        }
    }
}
