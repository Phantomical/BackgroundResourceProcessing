using System.Collections.Generic;
using System.Linq;
using BackgroundResourceProcessing.Behaviour;
using BackgroundResourceProcessing.Converter;
using BackgroundResourceProcessing.Utils;
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
            // The curve maps throttle percent directly to the absolute EC rate;
            // unlike fuel/waste, SystemHeat does not multiply it by throttle.
            double ecRatio = module.ElectricalGeneration.Evaluate(module.CurrentThrottle);
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
        // SystemHeat caps auto-throttle generation by core damage.
        double maxEC = module.ElectricalGeneration.Evaluate(100f) * module.CoreIntegrity / 100f;
        double maxHeat = CalculateWasteHeat(module, 100f);

        // CalculateGoalThrottle never commands less than the throttle that
        // generates the EC of MinimumThrottle, and HandleResourceActivities
        // burns fuel at that throttle regardless of demand, dumping any EC
        // that doesn't fit. Model that floor as a separate converter that
        // dumps its EC, and the rest of the throttle range as a converter
        // whose EC output is limited by demand.
        //
        // A reactor set to hibernate on warp has no floor. It would shut down
        // if it went on rails, but it can be recorded before that happens, and
        // it shouldn't have to burn fuel when nothing needs the EC.
        double floor = 1.0;
        if (module.HibernateOnWarp)
        {
            floor = 0.0;
        }
        else if (maxEC > 0.0)
        {
            double minEC = module.ElectricalGeneration.Evaluate(module.MinimumThrottle);
            floor = MathUtil.Clamp(minEC / maxEC, 0.0, 1.0);
        }

        // Waste heat follows SystemHeat's heat curve rather than scaling
        // linearly with throttle, so evaluate it at the floor's throttle and
        // give the rest to the demand-driven converter.
        double floorHeat = floor > 0.0 ? CalculateWasteHeat(module, (float)(floor * 100.0)) : 0.0;
        double restHeat = System.Math.Max(maxHeat - floorHeat, 0.0);

        List<ConverterBehaviour> converters = [];
        if (floor > 0.0)
        {
            converters.Add(
                MakeAutoConverter(module, marker, floor, maxEC, floorHeat, dumpExcess: true)
            );
        }
        if (floor < 1.0)
        {
            converters.Add(
                MakeAutoConverter(module, marker, 1.0 - floor, maxEC, restHeat, dumpExcess: false)
            );
        }

        if (converters.Count == 0)
            return null;

        var behaviour = new ModuleBehaviour(converters);
        if (marker != null)
            behaviour.AddPushModule(marker);
        return behaviour;
    }

    private static ConstantConverter MakeAutoConverter(
        ModuleSystemHeatFissionReactor module,
        BRPSystemHeatMarker marker,
        double fraction,
        double maxEC,
        double heat,
        bool dumpExcess
    )
    {
        var inputs = ScaleResources(module.inputs, fraction);
        var outputs = ScaleResources(module.outputs, fraction);

        outputs.Add(
            new ResourceRatio()
            {
                ResourceName = "ElectricCharge",
                Ratio = maxEC * fraction,
                FlowMode = ResourceFlowMode.ALL_VESSEL,
                DumpExcess = dumpExcess,
            }
        );

        // Heat scales along with the resources within this converter, so that
        // the solver scales both proportionally when EC demand is below
        // maximum.
        AppendHeatFluxDirect(outputs, module, marker, heat);
        return new ConstantConverter(inputs, outputs, []);
    }

    private static ModuleBehaviour GetMinimumThrottleBehaviour(
        ModuleSystemHeatFissionReactor module,
        BRPSystemHeatMarker marker
    )
    {
        // MinimumThrottle is a percent (default 25), like CurrentThrottle.
        double throttle = module.MinimumThrottle * 0.01;

        var inputs = ScaleResources(module.inputs, throttle);
        var outputs = ScaleResources(module.outputs, throttle);

        AppendHeatFluxDirect(
            outputs,
            module,
            marker,
            CalculateWasteHeat(module, module.MinimumThrottle)
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
