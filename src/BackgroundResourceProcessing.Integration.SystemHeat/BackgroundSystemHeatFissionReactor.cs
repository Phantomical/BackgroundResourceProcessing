using System.Collections.Generic;
using System.Linq;
using BackgroundResourceProcessing.Behaviour;
using BackgroundResourceProcessing.Converter;
using SystemHeat;

namespace BackgroundResourceProcessing.Integration.SystemHeat;

/// <summary>
/// Background adapter for <see cref="ModuleSystemHeatFissionReactor"/>.
/// Replicates the four-branch SelectFirst logic from SystemHeat.cfg and adds
/// heat-flux output to each active branch.
/// </summary>
public class BackgroundSystemHeatFissionReactor
    : BackgroundConverter<ModuleSystemHeatFissionReactor>
{
    public override ModuleBehaviour GetBehaviour(ModuleSystemHeatFissionReactor module)
    {
        if (!module.Enabled)
            return null;

        var marker = module.vessel?.rootPart?.FindModuleImplementing<BRPSystemHeatMarker>();

        // Branch 1: manual throttle control.
        if (module.ManualControl)
            return GetManualControlBehaviour(module, marker);

        // Branch 2: auto-throttle driven by electricity demand.
        if (module.GeneratesElectricity)
            return GetAutoElectricityBehaviour(module, marker);

        // Branch 3: hibernate — reactor idles with no resource flow.
        if (module.Hibernating || module.HibernateOnWarp)
            return null;

        // Branch 4: idle at minimum throttle.
        if (module.MinimumThrottle > 0)
            return GetMinimumThrottleBehaviour(module, marker);

        return null;
    }

    public override void OnRestore(
        ModuleSystemHeatFissionReactor module,
        Core.ResourceConverter converter
    )
    {
        module.LastUpdateTime = Planetarium.GetUniversalTime();
    }

    private static ModuleBehaviour GetManualControlBehaviour(
        ModuleSystemHeatFissionReactor module,
        BRPSystemHeatMarker marker
    )
    {
        double throttle = module.CurrentThrottle * 0.01;

        var inputs = ScaleResources(module.inputs, throttle);
        var outputs = ScaleResources(module.outputs, throttle);

        if (module.GeneratesElectricity && module.CurrentThrottle > 0)
        {
            // Pre-divide by throttle because the multiplier already scaled everything;
            // we want the EC rate at the actual throttle, not throttle².
            double ecRatio =
                module.ElectricalGeneration.Evaluate(module.CurrentThrottle)
                / (module.CurrentThrottle * 0.01);
            outputs.Add(
                new ResourceRatio()
                {
                    ResourceName = "ElectricCharge",
                    Ratio = ecRatio,
                    FlowMode = ResourceFlowMode.ALL_VESSEL,
                    DumpExcess = true,
                }
            );
        }

        AppendHeatFluxDirect(outputs, module, marker, module.CurrentHeatGeneration);
        return MakeBehaviour(inputs, outputs, marker, isPush: true);
    }

    private static ModuleBehaviour GetAutoElectricityBehaviour(
        ModuleSystemHeatFissionReactor module,
        BRPSystemHeatMarker marker
    )
    {
        var inputs = module.inputs.ToList();
        var outputs = module.outputs.ToList();

        outputs.Add(
            new ResourceRatio()
            {
                ResourceName = "ElectricCharge",
                Ratio = module.ElectricalGeneration.Evaluate(100f),
                FlowMode = ResourceFlowMode.ALL_VESSEL,
                DumpExcess = false,
            }
        );

        // Resources are at 100% throttle; heat must also be at 100% so the
        // solver scales both proportionally when EC demand is below maximum.
        AppendHeatFluxDirect(outputs, module, marker, CalculateWasteHeat(module, 100f));
        return MakeBehaviour(inputs, outputs, marker, isPush: true);
    }

    private static ModuleBehaviour GetMinimumThrottleBehaviour(
        ModuleSystemHeatFissionReactor module,
        BRPSystemHeatMarker marker
    )
    {
        double throttle = module.MinimumThrottle;

        var inputs = ScaleResources(module.inputs, throttle);
        var outputs = ScaleResources(module.outputs, throttle);

        AppendHeatFluxDirect(
            outputs,
            module,
            marker,
            CalculateWasteHeat(module, module.MinimumThrottle * 100f)
        );
        return MakeBehaviour(inputs, outputs, marker, isPush: true);
    }

    private static float CalculateWasteHeat(ModuleSystemHeatFissionReactor module, float throttle)
    {
        return module.HeatGeneration.Evaluate(throttle)
            * (1f - module.Efficiency)
            * module.CoreIntegrity
            / 100f;
    }

    private static void AppendHeatFluxDirect(
        List<ResourceRatio> outputs,
        ModuleSystemHeatFissionReactor module,
        BRPSystemHeatMarker marker,
        double fluxRate
    )
    {
        if (marker == null || module.heatModule == null || fluxRate <= 0)
            return;

        outputs.Add(
            new ResourceRatio()
            {
                ResourceName = SystemHeatFlux.ResourceName(module.heatModule.currentLoopID),
                Ratio = fluxRate,
                FlowMode = ResourceFlowMode.ALL_VESSEL,
                DumpExcess = true,
            }
        );
    }

    private static ModuleBehaviour MakeBehaviour(
        List<ResourceRatio> inputs,
        List<ResourceRatio> outputs,
        BRPSystemHeatMarker marker,
        bool isPush
    )
    {
        var behaviour = new ModuleBehaviour(new ConstantConverter(inputs, outputs, []));
        if (marker != null && isPush)
            behaviour.AddPushModule(marker);
        return behaviour;
    }

    private static List<ResourceRatio> ScaleResources(
        IEnumerable<ResourceRatio> source,
        double multiplier
    )
    {
        return source
            .Select(r => new ResourceRatio()
            {
                ResourceName = r.ResourceName,
                Ratio = r.Ratio * multiplier,
                FlowMode = r.FlowMode,
                DumpExcess = r.DumpExcess,
            })
            .ToList();
    }
}
