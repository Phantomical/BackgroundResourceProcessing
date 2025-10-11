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

    internal static readonly FieldInfo RoverAverageSpeedField;
    internal static readonly FieldInfo RoverAverageSpeedAtNightField;

    internal static readonly FieldInfo ShipAverageSpeedField;
    internal static readonly FieldInfo ShipAverageSpeedAtNightField;

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

        RoverAverageSpeedField = RoverController.GetField("averageSpeed", Instance);
        RoverAverageSpeedAtNightField = RoverController.GetField("averageSpeedAtNight", Instance);

        ShipAverageSpeedField = ShipController.GetField("averageSpeed", Instance);
        ShipAverageSpeedAtNightField = ShipController.GetField("averageSpeedAtNight", Instance);
    }

    internal static bool IsEnabled =>
        HighLogic
            .CurrentGame?.Parameters?.CustomParams<ModIntegrationSettings>()
            ?.EnableBonVoyageIntegration ?? false;

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
        {
            RoverAverageSpeedField.SetValue(controller, averageSpeed);
            RoverAverageSpeedAtNightField.SetValue(controller, averageSpeed);
        }
        else if (type == ShipController)
        {
            ShipAverageSpeedField.SetValue(controller, averageSpeed);
            ShipAverageSpeedAtNightField.SetValue(controller, averageSpeed);
        }
    }

    internal static void OnAutopilotActivated(object controller)
    {
        var vessel = (Vessel)BVVesselField.GetValue(controller);
        if (vessel is null)
            return;

        var processor = vessel.FindVesselModuleImplementing<BackgroundResourceProcessor>();
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
        var averageSpeed = (double)RoverAverageSpeedField.GetValue(controller);

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
        var averageSpeed = (double)ShipAverageSpeedField.GetValue(controller);

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
