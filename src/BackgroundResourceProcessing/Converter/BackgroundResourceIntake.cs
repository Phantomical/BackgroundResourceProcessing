using BackgroundResourceProcessing.Behaviour;

namespace BackgroundResourceProcessing.Converter;

public class BackgroundResourceIntake : BackgroundConverter<ModuleResourceIntake>
{
    public override ModuleBehaviour GetBehaviour(ModuleResourceIntake module)
    {
        // The stock module has a rather long list of preconditions to check
        // before it can actually intake resources.
        if (module.vessel == null || module.intakeTransform == null)
            return null;
        if (!module.intakeEnabled || !module.moduleIsEnabled)
            return null;
        if (module.part.ShieldedFromAirstream)
            return null;
        if (module.checkNode && module.node.attachedPart == null)
            return null;
        if (module.checkForOxygen && !module.vessel.mainBody.atmosphereContainsOxygen)
            return null;
        if (module.vessel.staticPressurekPa < module.kPaThreshold)
            return null;
        if (module.disableUnderwater && module.vessel.mainBody.ocean)
        {
            if (
                FlightGlobals.getAltitudeAtPos(
                    (Vector3d)module.intakeTransform.position,
                    module.vessel.mainBody
                ) < 0.0
            )
                return null;
        }
        if (module.underwaterOnly)
        {
            if (!module.vessel.mainBody.ocean)
                return null;
            if (
                FlightGlobals.getAltitudeAtPos(
                    (Vector3d)module.intakeTransform.position,
                    module.vessel.mainBody
                ) > 0.0
            )
                return null;
        }

        double amount =
            module.intakeSpeed
            * module.unitScalar
            * module.area
            * (double)module.machCurve.Evaluate(0f);

        if (module.underwaterOnly)
            amount *= module.vessel.mainBody.oceanDensity;
        else
            amount *= module.vessel.atmDensity;

        double units = amount * module.densityRecip;

        return new(
            new ConstantProducer(
                [
                    new ResourceRatio()
                    {
                        ResourceName = module.resourceName,
                        Ratio = units,
                        FlowMode = ResourceFlowMode.NULL,
                        DumpExcess = false,
                    },
                ]
            )
        );
    }
}
