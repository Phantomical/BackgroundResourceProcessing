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
//   BoiloffOccuring = false → cooling was active; we add the cooling system's
//                             heat to the tank's loop, the same way that
//                             ModuleSystemHeatCryoTank.ResolveHeat does, so
//                             that radiators on the loop have to reject it.
//   BoiloffOccuring = true  → boiloff was happening, continue it in background.
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
            return GetCoolingBehaviour(module);

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

    private static ModuleBehaviour GetCoolingBehaviour(ModuleSystemHeatCryoTank module)
    {
        var marker = module.vessel?.rootPart?.FindModuleImplementing<BRPSystemHeatMarker>();
        if (marker == null || module.heatModule == null)
            return null;

        // GetTotalCoolingCost is the heat per 1000 units of capacity. This
        // matches how ModuleSystemHeatCryoTank computes finalCoolingHeatCost.
        double coolingCost =
            module.GetTotalCoolingCost() * module.GetTotalMaxResouceAmount() / 1000.0;
        if (module.isJettisoned)
            coolingCost *= module.JettisonCoolingScale;
        if (!(coolingCost > 0.0))
            return null;

        // The tank adds this heat to its loop, so it is an output, the same as
        // reactor waste heat. Radiators on the loop consume it.
        var fluxOutput = new ResourceRatio()
        {
            ResourceName = SystemHeatFlux.ResourceName(module.heatModule.currentLoopID),
            Ratio = coolingCost,
            FlowMode = ResourceFlowMode.ALL_VESSEL,
            DumpExcess = true,
        };

        var converter = new ConstantConverter([], [fluxOutput], []);
        var behaviour = new ModuleBehaviour(converter);
        behaviour.AddPushModule(marker);
        return behaviour;
    }

    public override void OnRestore(
        ModuleSystemHeatCryoTank module,
        Core.ResourceConverter converter
    )
    {
        module.LastUpdateTime = Planetarium.GetUniversalTime();
    }
}
