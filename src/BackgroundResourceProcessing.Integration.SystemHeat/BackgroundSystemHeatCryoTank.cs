using System.Collections.Generic;
using BackgroundResourceProcessing.Behaviour;
using BackgroundResourceProcessing.Converter;
using SystemHeat;

namespace BackgroundResourceProcessing.Integration.SystemHeat;

// ModuleSystemHeatCryoTank uses thermal loops rather than ElectricCharge for
// cooling, so we cannot model the loop temperature in the background.
//
// Instead, we read the persisted BoiloffOccuring flag (which reflects the
// cooling state as it was when the vessel last unloaded) to decide whether
// boiloff should occur in the background:
//
//   BoiloffOccuring = false → cooling was active, no background boiloff
//   BoiloffOccuring = true  → boiloff was happening, continue it in background
//
// OnRestore updates LastUpdateTime so that DoCatchup() (called from Start()
// on vessel load) computes elapsed ≈ 0 and does not re-apply boiloff that
// BRP already simulated.

public class BackgroundSystemHeatCryoTank : BackgroundConverter<ModuleSystemHeatCryoTank>
{
    [KSPField]
    public double MaxError = 0.1;

    public override ModuleBehaviour GetBehaviour(ModuleSystemHeatCryoTank module)
    {
        if (!module.HasAnyBoiloffResource)
            return null;

        if (module.CoolingEnabled && module.IsCoolable() && !module.BoiloffOccuring)
            return null;

        List<ConverterBehaviour> behaviours = [];
        foreach (var fuel in module.fuels)
        {
            if (!fuel.fuelPresent || fuel.boiloffRateSeconds <= 0)
                continue;

            behaviours.Add(
                new SystemHeatCryoTankBoiloffBehaviour(fuel.outputs)
                {
                    FuelInventoryId = new(module.part, fuel.fuelName),
                    BoiloffRate = fuel.boiloffRateSeconds,
                    MaxError = MaxError,
                }
            );
        }

        if (behaviours.Count == 0)
            return null;

        return new ModuleBehaviour(behaviours);
    }

    public override void OnRestore(
        ModuleSystemHeatCryoTank module,
        Core.ResourceConverter converter
    )
    {
        module.LastUpdateTime = Planetarium.GetUniversalTime();
    }
}
