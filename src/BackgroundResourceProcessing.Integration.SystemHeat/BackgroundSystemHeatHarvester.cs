using BackgroundResourceProcessing.Behaviour;
using BackgroundResourceProcessing.Converter;
using BackgroundResourceProcessing.Utils;
using SystemHeat;

namespace BackgroundResourceProcessing.Integration.SystemHeat;

/// <summary>
/// Background adapter for <see cref="ModuleSystemHeatHarvester"/>.
/// Extends <see cref="BackgroundResourceHarvester{T}"/> to inherit the
/// abundance-based harvest rate and additionally outputs heat flux into
/// the per-loop fake inventory.
/// </summary>
public class BackgroundSystemHeatHarvester : BackgroundResourceHarvester<ModuleSystemHeatHarvester>
{
    public BackgroundSystemHeatHarvester()
    {
        UsePreparedRecipe = ConditionalExpression.Never;
    }

    public override ModuleBehaviour GetBehaviour(ModuleSystemHeatHarvester module)
    {
        var behaviour = base.GetBehaviour(module);
        if (behaviour == null)
            return null;

        var marker = module.vessel?.rootPart?.FindModuleImplementing<BRPSystemHeatMarker>();
        SystemHeatFlux.AddFluxOutput(behaviour, marker, module.heatModule, module.systemPower);
        return behaviour;
    }
}
