using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using BackgroundResourceProcessing.Collections;
using BackgroundResourceProcessing.Converter;
using HarmonyLib;
using WBIResources;

namespace BackgroundResourceProcessing.Integration.WBIResources;

[HarmonyPatch(typeof(WBIBackgroundConverter))]
static class WBIBackgroundConverter_Methods_Patch
{
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    #region Accessors & Reflection Types
    static readonly MethodInfo SupplyAmountMethod = typeof(WBIBackgroundConverter).GetMethod(
        "supplyAmount",
        Flags
    );
    static readonly MethodInfo RequestAmountMethod = typeof(WBIBackgroundConverter).GetMethod(
        "requestAmount",
        Flags
    );
    static readonly MethodInfo GetAmountMethod = typeof(WBIBackgroundConverter).GetMethod(
        "getAmount",
        Flags
    );
    #endregion

    #region Target Fields
    static readonly FieldInfo ProtoPartField = typeof(WBIBackgroundConverter).GetField(
        "protoPart",
        Flags
    );

    #endregion

    #region Patching
    static IEnumerable<MethodBase> TargetMethods()
    {
        var type = typeof(WBIBackgroundConverter);

        yield return SymbolExtensions.GetMethodInfo<WBIBackgroundConverter>(c =>
            c.CheckRequiredResources(null, 0)
        );
        yield return SymbolExtensions.GetMethodInfo<WBIBackgroundConverter>(c =>
            c.ConsumeInputResources(null, 0)
        );
        yield return SymbolExtensions.GetMethodInfo<WBIBackgroundConverter>(c =>
            c.ProduceOutputResources(null, 0)
        );
    }

    static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var matcher = new CodeMatcher(instructions);
        var pairs = new List<ValueTuple<MethodInfo, MethodInfo>>(
            [
                (
                    SupplyAmountMethod,
                    SymbolExtensions.GetMethodInfo(() =>
                        SupplyAmount(null, default, default, default, default, default)
                    )
                ),
                (
                    RequestAmountMethod,
                    SymbolExtensions.GetMethodInfo(() =>
                        RequestAmount(null, default, default, default, default)
                    )
                ),
                (
                    GetAmountMethod,
                    SymbolExtensions.GetMethodInfo(() => GetAmount(null, default, default, default))
                ),
            ]
        );

        foreach (var (target, replacement) in pairs)
        {
            // We want to replace each of these method calls with a call to our
            // patched method that also takes the ProtoVessel parameter as an
            // additional argument.
            //
            // We can always find the ProtoVessel parameter as argument 1 of the
            // function we are patching.

            matcher.Start();
            matcher
                .MatchStartForward(new CodeMatch(OpCodes.Call, target))
                .Repeat(matcher =>
                {
                    matcher.RemoveInstruction();
                    matcher.Insert(
                        [
                            new CodeInstruction(OpCodes.Ldarg_1),
                            new CodeInstruction(OpCodes.Call, replacement),
                        ]
                    );
                });
        }

        return matcher.Instructions();
    }
    #endregion

    #region Replacement Functions
    static void SupplyAmount(
        WBIBackgroundConverter instance,
        string resourceName,
        double supply,
        ResourceFlowMode flowMode,
        bool dumpExcess,
        ProtoVessel proto
    )
    {
        var settings = HighLogic.CurrentGame.Parameters.CustomParams<ModIntegrationSettings>();
        if (!(settings?.EnableWildBlueIntegration ?? false))
        {
            instance.supplyAmount(resourceName, supply, flowMode, dumpExcess);
            return;
        }

        var vessel = proto.vesselRef;
        var processor = vessel.FindVesselModuleImplementing<BackgroundResourceProcessor>();
        double added;

        if (flowMode == ResourceFlowMode.NO_FLOW)
        {
            var part = (ProtoPartSnapshot)ProtoPartField.GetValue(instance);
            var inventory = processor.GetInventoryById(new(part, resourceName));
            if (inventory == null)
                return;

            added = Math.Min(inventory.Available, supply);
            inventory.Amount += added;
        }
        else
        {
            added = processor.AddResource(resourceName, supply);
        }

        processor.MarkDirty();

        var remaining = supply - added;
        if (remaining > 1e-5 && !dumpExcess)
            instance.isContainerFull = true;
    }

    static double RequestAmount(
        WBIBackgroundConverter instance,
        string resourceName,
        double demand,
        ResourceFlowMode flowMode,
        ProtoVessel proto
    )
    {
        var settings = HighLogic.CurrentGame.Parameters.CustomParams<ModIntegrationSettings>();
        if (!(settings?.EnableWildBlueIntegration ?? false))
            return instance.requestAmount(resourceName, demand, flowMode);

        var vessel = proto.vesselRef;
        var processor = vessel.FindVesselModuleImplementing<BackgroundResourceProcessor>();

        double supply;
        if (flowMode == ResourceFlowMode.NO_FLOW)
        {
            var part = (ProtoPartSnapshot)ProtoPartField.GetValue(instance);
            var inventory = processor.GetInventoryById(new(part, resourceName));
            if (inventory == null)
                return 0.0;

            supply = Math.Min(inventory.Amount, demand);
            inventory.Amount -= supply;
        }
        else
        {
            supply = processor.RemoveResource(resourceName, demand);
        }

        processor.MarkDirty();
        return supply;
    }

    static double GetAmount(
        WBIBackgroundConverter instance,
        string resourceName,
        ResourceFlowMode flowMode,
        ProtoVessel proto
    )
    {
        var settings = HighLogic.CurrentGame.Parameters.CustomParams<ModIntegrationSettings>();
        if (!(settings?.EnableWildBlueIntegration ?? false))
            return instance.getAmount(resourceName, flowMode);

        var vessel = proto.vesselRef;
        var processor = vessel.FindVesselModuleImplementing<BackgroundResourceProcessor>();

        if (flowMode == ResourceFlowMode.NO_FLOW)
        {
            var part = (ProtoPartSnapshot)ProtoPartField.GetValue(instance);
            var inventory = processor.GetInventoryById(new(part, resourceName));

            return inventory?.Amount ?? 0.0;
        }
        else
        {
            return processor.GetResourceState(resourceName).amount;
        }
    }
    #endregion
}

[HarmonyPatch(typeof(WBIBackgroundConverter))]
[HarmonyPatch("ProduceYieldResources")]
static class WBIBackgroundConverter_ProduceYieldResources_Patch
{
    static bool Prefix(WBIBackgroundConverter __instance, ProtoVessel vessel)
    {
        var settings = HighLogic.CurrentGame.Parameters.CustomParams<ModIntegrationSettings>();
        if (!(settings?.EnableWildBlueIntegration ?? false))
            return true;

        var yields = __instance.yieldsList;
        if (yields.Count == 0)
            return true;

        var processor =
            vessel.vesselRef.FindVesselModuleImplementing<BackgroundResourceProcessor>();

        var protoPart = __instance.protoPart;
        var protoModule = __instance.moduleSnapshot;

        uint moduleId = 0;
        if (!protoModule.moduleValues.TryGetValue("persistentId", ref moduleId))
            return true;

        foreach (var converter in processor.Converters)
        {
            if (converter.FlightId != protoPart.flightID)
                continue;
            if (converter.ModuleId != moduleId)
                continue;

            // If our paired converter is not running then we should not be
            // yielding any resources.
            if (converter.Rate == 0.0)
                return false;

            // Otherwise we're good.
            break;
        }

        return true;
    }
}

[HarmonyPatch(typeof(WBIBackgroundConverter))]
[HarmonyPatch("GetBackgroundConverters")]
static class WBIBackgroundConverter_GetBackgroundConverters_Patch
{
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    static readonly FieldInfo ModuleSnapshotField = typeof(WBIBackgroundConverter).GetField(
        "ModuleSnapshot",
        Flags
    );

    static readonly FieldInfo InputListField = typeof(WBIBackgroundConverter).GetField(
        "inputList",
        Flags
    );
    static readonly FieldInfo OutputListField = typeof(WBIBackgroundConverter).GetField(
        "outputList",
        Flags
    );
    static readonly FieldInfo YieldsListField = typeof(WBIBackgroundConverter).GetField(
        "yieldsList",
        Flags
    );

    static Dictionary<Vessel, List<WBIBackgroundConverter>> Postfix(
        Dictionary<Vessel, List<WBIBackgroundConverter>> converters
    )
    {
        var settings = HighLogic.CurrentGame.Parameters.CustomParams<ModIntegrationSettings>();
        if (!(settings?.EnableWildBlueIntegration ?? false))
            return converters;

        List<Vessel> removed = [];
        foreach (var (vessel, list) in converters)
        {
            list.RemoveAll(converter =>
            {
                var snapshot = converter.moduleSnapshot;
                var module = AssemblyLoader.GetClassByName(typeof(PartModule), snapshot.moduleName);
                // The module type doesn't exist? Keep the background simulation
                // of it because we are clearly not simulating it.
                if (module == null)
                    return false;

                // If we don't model this converter then preserve Snacks'
                // background simulation of it.
                var adapter = BackgroundConverter.GetConverterForType(module);
                if (adapter == null)
                    return false;

                // We are simulating this module and it has no special behaviour.
                // Remove it.
                var yields = converter.yieldsList;
                if (yields.Count == 0)
                    return true;

                // We have a converter which has a special yield behaviour, but
                // also has normal behaviour that would conflict with BRP.
                //
                // To adjust, we strip out all inputs and outputs from the
                // converter but we don't actually delete it.
                converter.inputList.Clear();
                converter.outputList.Clear();

                return false;
            });

            if (list.Count == 0)
                removed.Add(vessel);
        }

        foreach (var vessel in removed)
            converters.Remove(vessel);

        return converters;
    }
}

[HarmonyPatch(typeof(WBIBackgroundConverter))]
[HarmonyPatch("PrepareToProcess")]
static class WBIBackgroundConverter_PrepareToProcess_Patch
{
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static readonly FieldInfo ProtoResourcesField = typeof(WBIBackgroundConverter).GetField(
        "protoResources",
        Flags
    );

    // Combined with all the other patches nothing actually uses protoResources
    // anymore so this patch skips PrepareToProcess as an optimization.
    static bool Prefix(WBIBackgroundConverter __instance)
    {
        var settings = HighLogic.CurrentGame.Parameters.CustomParams<ModIntegrationSettings>();
        if (!(settings?.EnableWildBlueIntegration ?? false))
            return true;

        __instance.protoResources.Clear();
        return false;
    }
}
