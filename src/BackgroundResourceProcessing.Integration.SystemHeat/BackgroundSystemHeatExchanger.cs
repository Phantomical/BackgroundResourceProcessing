using System.Collections.Generic;
using BackgroundResourceProcessing.Behaviour;
using BackgroundResourceProcessing.Converter;
using SystemHeat;

namespace BackgroundResourceProcessing.Integration.SystemHeat;

/// <summary>
/// Background adapter for <see cref="ModuleSystemHeatExchanger"/>.
/// When active, draws heat flux from the source loop and injects it into
/// the destination loop, consuming ElectricCharge at the configured rate.
///
/// ToggleSource = false → source = heatModule2, dest = heatModule1
/// ToggleSource = true  → source = heatModule1, dest = heatModule2
/// </summary>
public class BackgroundSystemHeatExchanger : BackgroundConverter<ModuleSystemHeatExchanger>
{
    public override ModuleBehaviour GetBehaviour(ModuleSystemHeatExchanger module)
    {
        if (!module.Enabled)
            return null;
        if (module.heatModule1 == null || module.heatModule2 == null)
            return null;
        if (module.HeatRate <= 0)
            return null;

        var marker = module.vessel?.rootPart?.FindModuleImplementing<BRPSystemHeatMarker>();
        if (marker == null)
            return null;

        var sourceModule = module.ToggleSource ? module.heatModule1 : module.heatModule2;
        var destModule = module.ToggleSource ? module.heatModule2 : module.heatModule1;

        double ecCost =
            module.temperatureDeltaCostCurve.Evaluate(module.OutletAdjustement)
            + module.heatFlowCostCurve.Evaluate(module.HeatRate);
        double deltaHeat = module.temperatureDeltaHeatCurve.Evaluate(module.OutletAdjustement);

        var inputs = new List<ResourceRatio>();
        if (ecCost > 0)
            inputs.Add(
                new ResourceRatio(
                    "ElectricCharge",
                    ecCost,
                    false,
                    ResourceFlowMode.STAGE_PRIORITY_FLOW
                )
            );
        inputs.Add(
            new ResourceRatio()
            {
                ResourceName = SystemHeatFlux.ResourceName(sourceModule.currentLoopID),
                Ratio = module.HeatRate,
                FlowMode = ResourceFlowMode.ALL_VESSEL,
                DumpExcess = false,
            }
        );

        var outputs = new List<ResourceRatio>
        {
            new ResourceRatio()
            {
                ResourceName = SystemHeatFlux.ResourceName(destModule.currentLoopID),
                Ratio = module.HeatRate + deltaHeat,
                FlowMode = ResourceFlowMode.ALL_VESSEL,
                DumpExcess = true,
            },
        };

        var behaviour = new ModuleBehaviour(new ConstantConverter(inputs, outputs, []));
        behaviour.AddPullModule(marker);
        behaviour.AddPushModule(marker);
        return behaviour;
    }
}
