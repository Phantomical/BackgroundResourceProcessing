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
using WildBlueIndustries;

namespace BackgroundResourceProcessing.Integration.WildBlueTools;

[HarmonyPatch]
static class WBIBackgroundConverterMethods
{
    static void EmptyMethod() { }

    [HarmonyReversePatch]
    [HarmonyPatch(typeof(WBIBackgroundConverterMethods), nameof(EmptyMethod))]
    internal static void EmailPlayer(
        this WBIBackgroundConverter self,
        string resourceName,
        WBIBackroundEmailTypes emailType
    )
    {
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> _)
        {
            return CallMethod(AccessTools.Method(typeof(WBIBackgroundConverter), "emailPlayer"));
        }

        Transpiler(null);
        throw new NotImplementedException();
    }

    [HarmonyReversePatch]
    [HarmonyPatch(typeof(WBIBackgroundConverterMethods), nameof(EmptyMethod))]
    internal static void SupplyAmount(
        this WBIBackgroundConverter self,
        string resourceName,
        double supply,
        ResourceFlowMode flowMode,
        bool dumpExcess
    )
    {
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> _)
        {
            return CallMethod(AccessTools.Method(typeof(WBIBackgroundConverter), "supplyAmount"));
        }

        Transpiler(null);
        throw new NotImplementedException();
    }

    [HarmonyReversePatch]
    [HarmonyPatch(typeof(WBIBackgroundConverterMethods), nameof(EmptyMethod))]
    internal static double RequestAmount(
        this WBIBackgroundConverter self,
        string resourceName,
        double demand,
        ResourceFlowMode flowMode
    )
    {
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> _)
        {
            return CallMethod(AccessTools.Method(typeof(WBIBackgroundConverter), "requestAmount"));
        }

        Transpiler(null);
        throw new NotImplementedException();
    }

    [HarmonyReversePatch]
    [HarmonyPatch(typeof(WBIBackgroundConverterMethods), nameof(EmptyMethod))]
    internal static double GetAmount(
        this WBIBackgroundConverter self,
        string resourceName,
        ResourceFlowMode flowMode
    )
    {
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> _)
        {
            return CallMethod(AccessTools.Method(typeof(WBIBackgroundConverter), "getAmount"));
        }

        Transpiler(null);
        throw new NotImplementedException();
    }

    private static List<CodeInstruction> CallMethod(MethodBase method)
    {
        int pcount = method.GetParameters().Length;
        if (!method.IsStatic)
            pcount += 1;

        var insts = new List<CodeInstruction>(pcount + 2);
        for (int i = 0; i < pcount; ++i)
        {
            insts.Add(
                i switch
                {
                    0 => new CodeInstruction(OpCodes.Ldarg_0),
                    1 => new CodeInstruction(OpCodes.Ldarg_1),
                    2 => new CodeInstruction(OpCodes.Ldarg_2),
                    3 => new CodeInstruction(OpCodes.Ldarg_3),
                    _ => i <= 255
                        ? new CodeInstruction(OpCodes.Ldarg_S, (byte)i)
                        : new CodeInstruction(OpCodes.Ldarg, i),
                }
            );
        }

        if (method.IsVirtual)
            insts.Add(new CodeInstruction(OpCodes.Callvirt, method));
        else
            insts.Add(new CodeInstruction(OpCodes.Call, method));

        insts.Add(new CodeInstruction(OpCodes.Ret));

        return insts;
    }
}

[HarmonyPatch(typeof(WBIBackgroundConverter))]
static class WBIBackgroundConverter_Methods_Patch
{
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    #region Accessors & Reflection Types
    static readonly MethodInfo EmailPlayerMethod = typeof(WBIBackgroundConverter).GetMethod(
        "emailPlayer",
        Flags
    );
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
            instance.SupplyAmount(resourceName, supply, flowMode, dumpExcess);
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
        {
            instance.isContainerFull = true;
            instance.EmailPlayer(resourceName, WBIBackroundEmailTypes.containerFull);
        }
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
            return instance.RequestAmount(resourceName, demand, flowMode);

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
            return instance.GetAmount(resourceName, flowMode);

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

[HarmonyPatch(typeof(WBIBackgroundConverter))]
[HarmonyPatch("ProduceYieldResources")]
static class WBIBackgroundConverter_ProduceYieldResources_Patch
{
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static readonly FieldInfo YieldsListField = typeof(WBIBackgroundConverter).GetField(
        "yieldsList",
        Flags
    );
    static readonly FieldInfo ProtoPartField = typeof(WBIBackgroundConverter).GetField(
        "protoPart",
        Flags
    );
    static readonly FieldInfo ModuleSnapshotField = typeof(WBIBackgroundConverter).GetField(
        "moduleSnapshot",
        Flags
    );

    static bool Prefix(WBIBackgroundConverter __instance, ProtoVessel vessel)
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
                var snapshot = (ProtoPartModuleSnapshot)ModuleSnapshotField.GetValue(converter);
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

        var protoResources = (IDictionary)ProtoResourcesField.GetValue(__instance);
        protoResources.Clear();

        return false;
    }
}
