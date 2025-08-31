using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Reflection.Emit;
using BackgroundResourceProcessing.Collections;
using BackgroundResourceProcessing.Converter;
using HarmonyLib;
using Snacks;

namespace BackgroundResourceProcessing.Integration.Snacks;

[HarmonyPatch(typeof(SnacksBackgroundConverter))]
static class SnacksBackgroundConverter_Methods_Patch
{
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    #region Accessors & Reflection Types
    delegate void EmailPlayerDelegate(
        SnacksBackgroundConverter self,
        string resourceName,
        SnacksBackroundEmailTypes emailType
    );
    delegate void SupplyAmountDelegate(
        SnacksBackgroundConverter self,
        string resourceName,
        double supply,
        ResourceFlowMode flowMode,
        bool dumpExcess
    );
    delegate double RequestAmountDelegate(
        SnacksBackgroundConverter self,
        string resourceName,
        double demand,
        ResourceFlowMode flowMode
    );
    delegate double GetAmountDelegate(
        SnacksBackgroundConverter self,
        string resourceName,
        ResourceFlowMode flowMode
    );

    static readonly MethodInfo EmailPlayerMethod = typeof(SnacksBackgroundConverter).GetMethod(
        "emailPlayer",
        Flags
    );
    static readonly MethodInfo SupplyAmountMethod = typeof(SnacksBackgroundConverter).GetMethod(
        "supplyAmount",
        Flags
    );
    static readonly MethodInfo RequestAmountMethod = typeof(SnacksBackgroundConverter).GetMethod(
        "requestAmount",
        Flags
    );
    static readonly MethodInfo GetAmountMethod = typeof(SnacksBackgroundConverter).GetMethod(
        "getAmount",
        Flags
    );

    static readonly EmailPlayerDelegate EmailPlayerFunc = CreateInvokeDelegate<EmailPlayerDelegate>(
        EmailPlayerMethod
    );

    static readonly SupplyAmountDelegate SupplyAmountFunc =
        CreateInvokeDelegate<SupplyAmountDelegate>(SupplyAmountMethod);
    static readonly RequestAmountDelegate RequestAmountFunc =
        CreateInvokeDelegate<RequestAmountDelegate>(RequestAmountMethod);
    static readonly GetAmountDelegate GetAmountFunc = CreateInvokeDelegate<GetAmountDelegate>(
        GetAmountMethod
    );
    #endregion

    #region Target Fields
    static readonly FieldInfo ProtoPartField = typeof(SnacksBackgroundConverter).GetField(
        "protoPart",
        Flags
    );

    #endregion

    #region Patching
    static IEnumerable<MethodBase> TargetMethods()
    {
        var type = typeof(SnacksBackgroundConverter);

        yield return AccessTools.Method(
            type,
            nameof(SnacksBackgroundConverter.CheckRequiredResources)
        );
        yield return AccessTools.Method(
            type,
            nameof(SnacksBackgroundConverter.ConsumeInputResources)
        );
        yield return AccessTools.Method(
            type,
            nameof(SnacksBackgroundConverter.ProduceOutputResources)
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
        SnacksBackgroundConverter instance,
        string resourceName,
        double supply,
        ResourceFlowMode flowMode,
        bool dumpExcess,
        ProtoVessel proto
    )
    {
        var settings = HighLogic.CurrentGame.Parameters.CustomParams<ModIntegrationSettings>();
        if (!(settings?.EnableWildBlueIntegration ?? false))
            SupplyAmountFunc(instance, resourceName, supply, flowMode, dumpExcess);

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
        {
            instance.isContainerFull = true;
            EmailPlayerFunc(instance, resourceName, SnacksBackroundEmailTypes.containerFull);
        }
    }

    static double RequestAmount(
        SnacksBackgroundConverter instance,
        string resourceName,
        double demand,
        ResourceFlowMode flowMode,
        ProtoVessel proto
    )
    {
        var settings = HighLogic.CurrentGame.Parameters.CustomParams<ModIntegrationSettings>();
        if (!(settings?.EnableWildBlueIntegration ?? false))
            return RequestAmountFunc(instance, resourceName, demand, flowMode);

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
        SnacksBackgroundConverter instance,
        string resourceName,
        ResourceFlowMode flowMode,
        ProtoVessel proto
    )
    {
        var settings = HighLogic.CurrentGame.Parameters.CustomParams<ModIntegrationSettings>();
        if (!(settings?.EnableWildBlueIntegration ?? false))
            return GetAmountFunc(instance, resourceName, flowMode);

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

    static F CreateInvokeDelegate<F>(MethodInfo method)
    {
        var ps = method
            .GetParameters()
            .Select(param => Expression.Parameter(param.ParameterType, param.Name))
            .ToList();

        if (!method.IsStatic)
        {
            var instance = Expression.Parameter(method.DeclaringType, "this");
            var expr = Expression.Call(instance, method, ps);

            return Expression.Lambda<F>(expr, [instance, .. ps]).Compile();
        }
        else
        {
            var expr = Expression.Call(method, ps);
            return Expression.Lambda<F>(expr, ps).Compile();
        }
    }
    #endregion
}

[HarmonyPatch(typeof(SnacksBackgroundConverter))]
[HarmonyPatch(nameof(SnacksBackgroundConverter.ProduceyieldsList))]
static class SnacksBackgroundConverter_ProduceYieldsList_Patch
{
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static readonly FieldInfo YieldsListField = typeof(SnacksBackgroundConverter).GetField(
        "yieldsList",
        Flags
    );
    static readonly FieldInfo ProtoPartField = typeof(SnacksBackgroundConverter).GetField(
        "protoPart",
        Flags
    );
    static readonly FieldInfo ModuleSnapshotField = typeof(SnacksBackgroundConverter).GetField(
        "moduleSnapshot",
        Flags
    );

    static bool Prefix(SnacksBackgroundConverter __instance, ProtoVessel vessel)
    {
        var settings = HighLogic.CurrentGame.Parameters.CustomParams<ModIntegrationSettings>();
        if (!(settings?.EnableWildBlueIntegration ?? false))
            return true;

        var yields = (List<ResourceRatio>)YieldsListField.GetValue(__instance);
        if (yields.Count == 0)
            return true;

        var processor =
            vessel.vesselRef.FindVesselModuleImplementing<BackgroundResourceProcessor>();

        var protoPart = (ProtoPartSnapshot)ProtoPartField.GetValue(__instance);
        var protoModule = (ProtoPartModuleSnapshot)ModuleSnapshotField.GetValue(__instance);

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

[HarmonyPatch(typeof(SnacksBackgroundConverter))]
[HarmonyPatch(nameof(SnacksBackgroundConverter.GetBackgroundConverters))]
static class SnacksBackgroundConverter_GetBackgroundConverters_Patch
{
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    static readonly FieldInfo InputListField = typeof(SnacksBackgroundConverter).GetField(
        "inputList",
        Flags
    );
    static readonly FieldInfo OutputListField = typeof(SnacksBackgroundConverter).GetField(
        "outputList",
        Flags
    );
    static readonly FieldInfo YieldsListField = typeof(SnacksBackgroundConverter).GetField(
        "yieldsList",
        Flags
    );

    static Dictionary<Vessel, List<SnacksBackgroundConverter>> Postfix(
        Dictionary<Vessel, List<SnacksBackgroundConverter>> converters
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
                var module = AssemblyLoader.GetClassByName(
                    typeof(PartModule),
                    converter.moduleName
                );
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
                var yields = (List<ResourceRatio>)YieldsListField.GetValue(converter);
                if (yields.Count == 0)
                    return true;

                // We have a converter which has a special yield behaviour, but
                // also has normal behaviour that would conflict with BRP.
                //
                // To adjust, we strip out all inputs and outputs from the
                // converter but we don't actually delete it.
                var inputs = (List<ResourceRatio>)InputListField.GetValue(converter);
                var outputs = (List<ResourceRatio>)OutputListField.GetValue(converter);

                inputs.Clear();
                outputs.Clear();

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

[HarmonyPatch(typeof(SnacksBackgroundConverter))]
[HarmonyPatch(nameof(SnacksBackgroundConverter.PrepareToProcess))]
static class SnacksBackgroundConverter_PrepareToProcess_Patch
{
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static readonly FieldInfo ProtoResourcesField = typeof(SnacksBackgroundConverter).GetField(
        "protoResources",
        Flags
    );

    // Combined with all the other patches nothing actually uses protoResources
    // anymore so this patch skips PrepareToProcess as an optimization.
    static bool Prefix(SnacksBackgroundConverter __instance)
    {
        var settings = HighLogic.CurrentGame.Parameters.CustomParams<ModIntegrationSettings>();
        if (!(settings?.EnableWildBlueIntegration ?? false))
            return true;

        var protoResources = (IDictionary)ProtoResourcesField.GetValue(__instance);
        protoResources.Clear();

        return false;
    }
}
