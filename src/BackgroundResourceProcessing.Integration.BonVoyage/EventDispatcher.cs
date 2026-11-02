using System;
using System.Reflection;
using UnityEngine;
using BV = BonVoyage.BonVoyage;

namespace BackgroundResourceProcessing.Integration.BonVoyage;

[KSPAddon(KSPAddon.Startup.AllGameScenes, once: false)]
internal class EventDispatcher : MonoBehaviour
{
    const BindingFlags Instance =
        BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    const BindingFlags Static = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;

    static readonly MethodInfo GetControllerOfVesselMethod;

    internal static readonly Type BVController;
    internal static readonly Type RoverController;
    internal static readonly Type ShipController;

    internal static readonly FieldInfo BVActiveField;
    internal static readonly FieldInfo BVRequiredPowerField;
    internal static readonly FieldInfo BVVesselField;

    // The day-time average speed lives on the controller's movement
    // controller (BonVoyage.MovementControllers.Controller), while the night
    // time speed lives on the controller itself.
    internal static readonly FieldInfo RoverMoveControllerField;
    internal static readonly FieldInfo RoverAverageSpeedAtNightField;

    internal static readonly FieldInfo ShipMoveControllerField;
    internal static readonly FieldInfo ShipAverageSpeedAtNightField;

    internal static readonly FieldInfo MoveControllerAverageSpeedField;

    /// <summary>
    /// Whether all the BonVoyage members we need were found. If BonVoyage
    /// changes its internals then we disable the integration instead of
    /// throwing on every record and changepoint.
    /// </summary>
    internal static readonly bool Available;

    static EventDispatcher()
    {
        GetControllerOfVesselMethod = typeof(BV).GetMethod(
            "GetControllerOfVessel",
            Instance,
            null,
            [typeof(Vessel)],
            null
        );

        var assembly = typeof(BV).Assembly;

        BVController = assembly.GetType("BonVoyage.BVController", throwOnError: true);
        RoverController = assembly.GetType("BonVoyage.RoverController", throwOnError: true);
        ShipController = assembly.GetType("BonVoyage.ShipController", throwOnError: true);

        BVVesselField = BVController.GetField("vessel", Instance);
        BVActiveField = BVController.GetField("active", Instance);
        BVRequiredPowerField = BVController.GetField("requiredPower", Instance);

        RoverMoveControllerField = RoverController.GetField("moveController", Instance);
        RoverAverageSpeedAtNightField = RoverController.GetField("averageSpeedAtNight", Instance);

        ShipMoveControllerField = ShipController.GetField("moveController", Instance);
        ShipAverageSpeedAtNightField = ShipController.GetField("averageSpeedAtNight", Instance);

        var moveController = assembly.GetType("BonVoyage.MovementControllers.Controller");
        MoveControllerAverageSpeedField = moveController?.GetField("averageSpeed", Instance);

        bool available = true;
        void Check(object member, string name)
        {
            if (member is not null)
                return;

            Debug.LogError(
                $"[BackgroundResourceProcessing] BonVoyage integration: could not find {name}. The BonVoyage integration will be disabled."
            );
            available = false;
        }

        Check(GetControllerOfVesselMethod, "BonVoyage.GetControllerOfVessel");
        Check(BVVesselField, "BVController.vessel");
        Check(BVActiveField, "BVController.active");
        Check(BVRequiredPowerField, "BVController.requiredPower");
        Check(RoverMoveControllerField, "RoverController.moveController");
        Check(RoverAverageSpeedAtNightField, "RoverController.averageSpeedAtNight");
        Check(ShipMoveControllerField, "ShipController.moveController");
        Check(ShipAverageSpeedAtNightField, "ShipController.averageSpeedAtNight");
        Check(MoveControllerAverageSpeedField, "MovementControllers.Controller.averageSpeed");

        Available = available;
    }

    static double GetAverageSpeed(object controller, FieldInfo moveControllerField)
    {
        var moveController = moveControllerField.GetValue(controller);
        if (moveController is null)
            return 0.0;

        return (double)MoveControllerAverageSpeedField.GetValue(moveController);
    }

    static void SetAverageSpeed(
        object controller,
        FieldInfo moveControllerField,
        FieldInfo atNightField,
        double speed
    )
    {
        var moveController = moveControllerField.GetValue(controller);
        if (moveController is not null)
            MoveControllerAverageSpeedField.SetValue(moveController, speed);

        atNightField.SetValue(controller, speed);
    }

    internal static bool IsEnabled =>
        Available
        && (
            HighLogic
                .CurrentGame?.Parameters?.CustomParams<ModIntegrationSettings>()
                ?.EnableBonVoyageIntegration ?? false
        );

    void Start()
    {
        BackgroundResourceProcessor.onVesselRecord.Add(OnVesselRecord);
        BackgroundResourceProcessor.onVesselChangepoint.Add(OnVesselChangepoint);
    }

    void OnDestroy()
    {
        BackgroundResourceProcessor.onVesselRecord.Remove(OnVesselRecord);
        BackgroundResourceProcessor.onVesselChangepoint.Remove(OnVesselChangepoint);
    }

    void OnVesselRecord(BackgroundResourceProcessor processor)
    {
        if (!Available)
            return;

        var controller = GetControllerOfVessel(BV.Instance, processor.Vessel);
        if (controller is null)
            return;

        if (!(bool)BVActiveField.GetValue(controller))
            return;

        var type = controller.GetType();
        if (type == RoverController)
            RecordRoverController(controller, processor);
        else if (type == ShipController)
            RecordShipController(controller, processor);
    }

    void OnVesselChangepoint(BackgroundResourceProcessor processor, ChangepointEvent evt)
    {
        if (!Available)
            return;

        var controller = GetControllerOfVessel(BV.Instance, processor.Vessel);
        if (controller is null)
            return;

        var converter = GetBonVoyageConverter(processor);
        if (converter is null)
            return;

        var behaviour = (BonVoyageBehaviour)converter.Behaviour;
        var averageSpeed = behaviour.AverageSpeed * converter.Rate;
        if (!behaviour.Enabled)
            averageSpeed = 0.0;

        var type = controller.GetType();
        if (type == RoverController)
            SetAverageSpeed(
                controller,
                RoverMoveControllerField,
                RoverAverageSpeedAtNightField,
                averageSpeed
            );
        else if (type == ShipController)
            SetAverageSpeed(
                controller,
                ShipMoveControllerField,
                ShipAverageSpeedAtNightField,
                averageSpeed
            );
    }

    internal static void OnAutopilotActivated(object controller)
    {
        var vessel = (Vessel)BVVesselField.GetValue(controller);
        if (vessel is null)
            return;

        var processor = vessel.FindVesselModuleImplementing<BackgroundResourceProcessor>();
        if (processor is null)
            return;

        var converter = GetBonVoyageConverter(processor);
        if (converter == null)
            return;

        var behaviour = (BonVoyageBehaviour)converter.Behaviour;
        if (behaviour.Enabled)
            return;

        behaviour.Enabled = true;
        converter.NextChangepoint = Planetarium.GetUniversalTime();
        processor.MarkDirty();
    }

    internal static void OnAutopilotDeactivated(object controller)
    {
        var vessel = (Vessel)BVVesselField.GetValue(controller);
        if (vessel is null)
            return;

        var processor = vessel.FindVesselModuleImplementing<BackgroundResourceProcessor>();
        if (processor is null)
            return;

        var converter = GetBonVoyageConverter(processor);
        if (converter == null)
            return;

        var behaviour = (BonVoyageBehaviour)converter.Behaviour;
        if (!behaviour.Enabled)
            return;

        behaviour.Enabled = false;
        converter.NextChangepoint = Planetarium.GetUniversalTime();
        processor.MarkDirty();
    }

    static void RecordRoverController(object controller, BackgroundResourceProcessor processor)
    {
        var requiredPower = (double)BVRequiredPowerField.GetValue(controller);
        var averageSpeed = GetAverageSpeed(controller, RoverMoveControllerField);

        processor.AddConverter(
            new Core.ResourceConverter(
                new BonVoyageBehaviour(
                    [
                        new ResourceRatio
                        {
                            ResourceName = "ElectricCharge",
                            Ratio = requiredPower,
                            FlowMode = ResourceFlowMode.ALL_VESSEL,
                            DumpExcess = false,
                        },
                    ]
                )
                {
                    AverageSpeed = averageSpeed,
                }
            ),
            new AddConverterOptions { LinkToAll = true }
        );
    }

    static void RecordShipController(object controller, BackgroundResourceProcessor processor)
    {
        var requiredPower = (double)BVRequiredPowerField.GetValue(controller);
        var averageSpeed = GetAverageSpeed(controller, ShipMoveControllerField);

        processor.AddConverter(
            new Core.ResourceConverter(
                new BonVoyageBehaviour(
                    [
                        new ResourceRatio
                        {
                            ResourceName = "ElectricCharge",
                            Ratio = requiredPower,
                            FlowMode = ResourceFlowMode.ALL_VESSEL,
                            DumpExcess = false,
                        },
                    ]
                )
                {
                    AverageSpeed = averageSpeed,
                }
            ),
            new AddConverterOptions { LinkToAll = true }
        );
    }

    static Core.ResourceConverter GetBonVoyageConverter(BackgroundResourceProcessor processor)
    {
        foreach (var converter in processor.Converters)
        {
            if (converter.Behaviour is BonVoyageBehaviour)
                return converter;
        }

        return null;
    }

    public static object GetControllerOfVessel(BV inst, Vessel v) =>
        GetControllerOfVesselMethod.Invoke(inst, [v]);
}
