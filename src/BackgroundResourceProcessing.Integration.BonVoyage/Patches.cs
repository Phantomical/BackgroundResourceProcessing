using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using BonVoyage;
using HarmonyLib;

namespace BackgroundResourceProcessing.Integration.BonVoyage;

[HarmonyPatch]
internal static class RoverShipController_SystemCheck_Patch
{
    const BindingFlags Instance =
        BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    static readonly FieldInfo InfiniteElectricityField = GetFieldInfo(() =>
        CheatOptions.InfiniteElectricity
    );
    static readonly FieldInfo ElectricPowerOtherField = EventDispatcher.BVController.GetField(
        "electricPower_Other",
        Instance
    );

    static IEnumerable<MethodInfo> TargetMethods()
    {
        yield return EventDispatcher.RoverController.GetMethod("SystemCheck", Instance);
        yield return EventDispatcher.ShipController.GetMethod("SystemCheck", Instance);
    }

    static IEnumerable<CodeInstruction> Transpiler(
        IEnumerable<CodeInstruction> instructions,
        ILGenerator gen
    )
    {
        // return instructions;
        var matcher = new CodeMatcher(instructions, gen);
        matcher
            .MatchStartForward(
                new CodeMatch(inst =>
                {
                    if (inst.opcode != OpCodes.Ldsfld)
                        return false;

                    if (inst.operand is not FieldInfo field)
                        return false;

                    return field == InfiniteElectricityField;
                })
            )
            .ThrowIfInvalid("Could not find access of CheatOptions::InfiniteElectricity")
            .Advance(1)
            // Just replacing the ldsfld instruction seems to cause weird errors
            // in harmony so instead we just throw away its stack value and call
            // our replacement method to push a new value on the stack.
            .Insert(
                new CodeInstruction(OpCodes.Pop),
                new CodeInstruction(
                    OpCodes.Call,
                    SymbolExtensions.GetMethodInfo(() => IsElectricityProvided())
                )
            )
            .MatchStartForward(
                new CodeMatch(inst =>
                {
                    if (inst.opcode != OpCodes.Stfld)
                        return false;

                    if (inst.operand is not FieldInfo field)
                        return false;

                    return field == ElectricPowerOtherField;
                })
            )
            .ThrowIfInvalid("Could not find store to electricPower_Other")
            // We need electricPower_Other to be >= requiredPower. However, if
            // we replace it unconditionally then the BonVoyage window is no
            // longer useful.
            //
            // As a compromise we change we set it to requiredPower only if
            // electricPower_Other is less than requiredPower.
            .Insert(
                new CodeInstruction(OpCodes.Ldarg_0),
                new CodeInstruction(OpCodes.Ldfld, ElectricPowerOtherField),
                new CodeInstruction(
                    OpCodes.Call,
                    SymbolExtensions.GetMethodInfo(() => Math.Max(0.0, 0.0))
                )
            );

        return matcher.Instructions();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static bool IsElectricityProvided()
    {
        if (CheatOptions.InfiniteElectricity)
            return true;

        // If we are enabled we make BonVoyage think that there is always
        // enough power.
        if (EventDispatcher.IsEnabled)
            return true;

        return false;
    }

    static FieldInfo GetFieldInfo(Expression expr)
    {
        if (expr is not LambdaExpression lambda)
            throw new ArgumentException("expected a lambda expression");

        if (lambda.Body is not MemberExpression mexpr)
            throw new ArgumentException("expression must be a member access");

        if (mexpr.Member is not FieldInfo field)
            throw new ArgumentException("member must not be a property");

        return field;
    }
}

[HarmonyPatch]
internal static class RoverShipController_Update_Patch
{
    const BindingFlags Instance =
        BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    static readonly Type RoverController = EventDispatcher.RoverController;
    static readonly Type ShipController = EventDispatcher.ShipController;
    static readonly Type BVController = EventDispatcher.BVController;

    static readonly FieldInfo VesselField = EventDispatcher.BVVesselField;
    static readonly FieldInfo ActiveField = EventDispatcher.BVActiveField;
    static readonly FieldInfo LastUpdateField = BVController.GetField("lastTimeUpdated", Instance);
    static readonly FieldInfo RoverAngleField = RoverController.GetField("angle", Instance);
    static readonly FieldInfo ShipAngleField = ShipController.GetField("angle", Instance);

    static IEnumerable<MethodInfo> TargetMethods()
    {
        yield return RoverController.GetMethod("Update", Instance);
        yield return ShipController.GetMethod("Update", Instance);
    }

    static void Postfix(object __instance)
    {
        var active = (bool)ActiveField.GetValue(__instance);
        var vessel = (Vessel)VesselField.GetValue(__instance);

        if (vessel == null || vessel.isActiveVessel || !active)
            return;
        var lastUpdateTime = (double)LastUpdateField.GetValue(__instance);
        if (lastUpdateTime == 0)
            return;

        double angle;
        var type = __instance.GetType();
        if (type == RoverController)
            angle = (double)RoverAngleField.GetValue(__instance);
        else if (type == ShipController)
            angle = (double)ShipAngleField.GetValue(__instance);
        else
            return;

        var shadow = angle > 90.0;

        var processor = vessel.FindVesselModuleImplementing<BackgroundResourceProcessor>();
        if (processor.ShadowState is null)
            return;

        if (shadow != processor.ShadowState.Value.InShadow)
            processor.RefreshShadowState();
    }
}

[HarmonyPatch]
internal static class BVController_Ctor_Patch
{
    const BindingFlags Instance =
        BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    static readonly MethodInfo AddOnStateChangedMethod = EventDispatcher.BVController.GetMethod(
        "add_OnStateChanged",
        Instance
    );
    static readonly PropertyInfo StateProperty = EventDispatcher.BVController.GetProperty(
        "State",
        Instance
    );

    static MethodBase TargetMethod() =>
        EventDispatcher.BVController.GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
            null,
            [typeof(Vessel), typeof(ConfigNode)],
            null
        );

    static void Postfix(object __instance)
    {
        AddOnStateChangedMethod.Invoke(__instance, [new EventHandler(OnStateChanged)]);
    }

    static void OnStateChanged(object controller, EventArgs _)
    {
        if (!EventDispatcher.IsEnabled)
            return;

        Enum state = (Enum)StateProperty.GetValue(controller);
        int value = Convert.ToInt32(state);

        switch (value)
        {
            case 0: // Idle
            case 1: // ControllerDisabled
            case 4: // AwaitingSunlight
                EventDispatcher.OnAutopilotDeactivated(controller);
                break;

            case 3: // Moving
                EventDispatcher.OnAutopilotActivated(controller);
                break;
        }
    }
}

[HarmonyPatch(typeof(DetectKerbalism), nameof(DetectKerbalism.Found))]
internal static class DetectKerbalism_Found_Patch
{
    static bool Prefix(ref bool __result)
    {
        // We pretend that kerbalism is enabled since we want to effectively
        // disable all the same features in BonVoyage.
        if (EventDispatcher.IsEnabled)
        {
            __result = true;
            return false;
        }

        return true;
    }
}
