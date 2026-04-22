using BackgroundResourceProcessing.Behaviour;
using BackgroundResourceProcessing.Converter;
using SystemHeat;

namespace BackgroundResourceProcessing.Integration.SystemHeat;

public class BackgroundSystemHeatAsteroidDrill
    : BackgroundSpaceObjectDrill<ModuleSystemHeatAsteroidHarvester>
{
    protected override ResourceRatio GetPowerConsumption(ModuleSystemHeatAsteroidHarvester module)
    {
        return new("ElectricCharge", module.PowerConsumption, false);
    }

    public override ModuleBehaviour GetBehaviour(ModuleSystemHeatAsteroidHarvester module)
    {
        var behaviour = base.GetBehaviour(module);
        if (behaviour == null)
            return null;

        var marker = module.vessel?.rootPart?.FindModuleImplementing<BRPSystemHeatMarker>();
        SystemHeatFlux.AddFluxOutput(behaviour, marker, module.heatModule, module.systemPower);
        return behaviour;
    }
}

public class BackgroundSystemHeatCometDrill
    : BackgroundSpaceObjectDrill<ModuleSystemHeatCometHarvester>
{
    protected override ResourceRatio GetPowerConsumption(ModuleSystemHeatCometHarvester module)
    {
        return new("ElectricCharge", module.PowerConsumption, false);
    }

    public override ModuleBehaviour GetBehaviour(ModuleSystemHeatCometHarvester module)
    {
        var behaviour = base.GetBehaviour(module);
        if (behaviour == null)
            return null;

        var marker = module.vessel?.rootPart?.FindModuleImplementing<BRPSystemHeatMarker>();
        SystemHeatFlux.AddFluxOutput(behaviour, marker, module.heatModule, module.systemPower);
        return behaviour;
    }
}
