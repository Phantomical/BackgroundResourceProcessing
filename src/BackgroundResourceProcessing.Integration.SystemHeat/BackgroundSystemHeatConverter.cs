using BackgroundResourceProcessing.Behaviour;
using BackgroundResourceProcessing.Converter;
using BackgroundResourceProcessing.Utils;
using SystemHeat;

namespace BackgroundResourceProcessing.Integration.SystemHeat;

/// <summary>
/// Background adapter for <see cref="ModuleSystemHeatConverter"/>.
/// Behaves like <see cref="BackgroundResourceConverter{T}"/> but also outputs heat flux
/// into the per-loop <c>BRPSystemHeatFlux{N}</c> fake inventory on the vessel's root part.
/// </summary>
public class BackgroundSystemHeatConverter : BackgroundResourceConverter<ModuleSystemHeatConverter>
{
    public BackgroundSystemHeatConverter()
    {
        UsePreparedRecipe = ConditionalExpression.Never;
    }

    public override ModuleBehaviour GetBehaviour(ModuleSystemHeatConverter module)
    {
        var behaviour = base.GetBehaviour(module);
        if (behaviour == null)
            return null;

        var marker = module.vessel?.rootPart?.FindModuleImplementing<BRPSystemHeatMarker>();
        SystemHeatFlux.AddFluxOutput(behaviour, marker, module.heatModule, module.systemPower);
        return behaviour;
    }
}
