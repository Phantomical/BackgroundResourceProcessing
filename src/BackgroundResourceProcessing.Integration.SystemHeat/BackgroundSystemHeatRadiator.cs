using System;
using System.Collections.Generic;
using BackgroundResourceProcessing.Behaviour;
using BackgroundResourceProcessing.Converter;
using SystemHeat;

namespace BackgroundResourceProcessing.Integration.SystemHeat;

/// <summary>
/// Background adapter for <see cref="ModuleSystemHeatRadiator"/>.
/// When cooling is active, consumes heat flux from the per-loop fake inventory.
/// The flux consumption rate is derived from the radiator's temperature curve,
/// evaluated at <c>max(nominalLoopTemperature, currentLoopTemperature)</c>.
/// </summary>
public class BackgroundSystemHeatRadiator : BackgroundConverter<ModuleSystemHeatRadiator>
{
    public override ModuleBehaviour GetBehaviour(ModuleSystemHeatRadiator module)
    {
        if (!module.IsCooling)
            return null;

        var marker = module.vessel?.rootPart?.FindModuleImplementing<BRPSystemHeatMarker>();
        if (marker == null || module.heatModule == null)
            return null;

        var effectiveTemp = Math.Max(
            module.heatModule.nominalLoopTemperature,
            module.heatModule.currentLoopTemperature
        );
        var fluxRate = module.temperatureCurve.Evaluate((float)effectiveTemp);

        var inputs = new List<ResourceRatio>();
        foreach (var res in module.resHandler.inputResources)
            inputs.Add(new ResourceRatio(res.resourceDef.name, res.rate, false, res.flowMode));

        inputs.Add(
            new ResourceRatio()
            {
                ResourceName = SystemHeatFlux.ResourceName(module.heatModule.currentLoopID),
                Ratio = fluxRate,
                FlowMode = ResourceFlowMode.ALL_VESSEL,
                DumpExcess = false,
            }
        );

        var behaviour = new ModuleBehaviour(new ConstantConverter(inputs, [], []));
        behaviour.AddPullModule(marker);
        return behaviour;
    }
}
