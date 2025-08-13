using System.Collections.Generic;
using System.Reflection;
using BackgroundResourceProcessing.Behaviour;
using BackgroundResourceProcessing.Converter;
using BackgroundResourceProcessing.Core;
using BackgroundResourceProcessing.Inventory;
using ExtraplanetaryLaunchpads;

namespace BackgroundResourceProcessing.Integration.EL;

/// <summary>
/// A background converter for an EL launchpad.
/// </summary>
///
/// <remarks>
///   This module works by taking a <see cref="WorkHoursResource"/> and
///   either consuming or building the required resources for the ship
///   being built. It expects a <see cref="BackgroundELLaunchpadInventory"/>
///   to be present on the same module which will update the EL WorkNet.
/// </remarks>
public class BackgroundELLaunchpad : BackgroundConverter<ELLaunchpad>
{
    private static readonly FieldInfo SourcesField = typeof(ELVesselWorkNet).GetField(
        "sources",
        BindingFlags.Instance | BindingFlags.NonPublic
    );

    [KSPField]
    public string WorkHoursResource = "BRPELWorkHours";

    public override ModuleBehaviour GetBehaviour(ELLaunchpad module)
    {
        var control = module.control;
        if (control == null)
            return null;
        if (!control.isActive)
            return null;

        var work = control.CalculateWork();
        List<ResourceRatio> inputs =
        [
            new ResourceRatio()
            {
                ResourceName = WorkHoursResource,
                Ratio = work,
                FlowMode = ResourceFlowMode.ALL_VESSEL,
            },
        ];

        List<ResourceRatio> outputs =
        [
            new()
            {
                ResourceName = WorkHoursResource,
                Ratio = work,
                FlowMode = ResourceFlowMode.NO_FLOW,
            },
        ];

        if (control.state == ELBuildControl.State.Building)
        {
            var required = control.builtStuff.required;

            foreach (var res in required)
            {
                if (res.amount == 0.0)
                    continue;

                inputs.Add(
                    new ResourceRatio()
                    {
                        ResourceName = res.name,
                        Ratio = res.amount,
                        FlowMode = ResourceFlowMode.ALL_VESSEL_BALANCE,
                    }
                );
            }
        }
        else if (control.state == ELBuildControl.State.Canceling)
        {
            var built = control.builtStuff.required;
            var cost = control.buildCost.required;

            foreach (var bres in built)
            {
                var cres = FindResource(cost, bres.name);
                var remaining = cres.amount - bres.amount;

                if (remaining <= 0.0)
                    continue;

                outputs.Add(
                    new ResourceRatio()
                    {
                        ResourceName = bres.name,
                        Ratio = remaining,
                        FlowMode = ResourceFlowMode.ALL_VESSEL_BALANCE,
                        DumpExcess = true,
                    }
                );
            }
        }
        else
        {
            return null;
        }

        ModuleBehaviour behaviour = new(new ConstantConverter(inputs, outputs));
        behaviour.AddPushModule(module);

        var workNet = control.workNet;
        if (workNet != null)
        {
            var sources = (List<ELWorkSource>)SourcesField.GetValue(workNet);
            if (sources != null)
            {
                foreach (var source in sources)
                {
                    if (source is PartModule partModule)
                        behaviour.AddPullModule(partModule);
                }
            }
        }

        return behaviour;
    }

    private static BuildResource FindResource(List<BuildResource> list, string name)
    {
        foreach (var res in list)
        {
            if (res.name == name)
                return res;
        }

        return null;
    }
}

/// <summary>
/// An inventory representing the amount of <see cref="WorkHoursResource"/>
/// that need to be produced before the vessel is complete. On resume it
/// updates the state of its linked <see cref="ELLaunchpad"/>.
/// </summary>
public class BackgroundELLaunchpadInventory : BackgroundInventory<ELLaunchpad>
{
    [KSPField]
    public string WorkHoursResource = "BRPELWorkHours";

    public override List<FakePartResource> GetResources(ELLaunchpad launchpad)
    {
        var control = launchpad.control;
        if (control == null)
            return null;
        if (!control.isActive)
            return null;

        var work = control.CalculateWork();
        var output = new FakePartResource()
        {
            resourceName = WorkHoursResource,
            amount = 0.0,
            maxAmount = work,
        };

        return [output];
    }

    public override void UpdateResource(ELLaunchpad launchpad, ResourceInventory inventory)
    {
        // The main thing we need to do here is to update the launchpad's
        // tracked resources to account for the work done in the background.

        if (inventory.ResourceName != WorkHoursResource)
            return;
        if (inventory.Amount == 0.0)
            return;

        var control = launchpad.control;
        if (control == null)
            return;
        if (!control.isActive)
            return;

        var vessel = launchpad.vessel;
        var workNet = vessel.FindVesselModuleImplementing<ELVesselWorkNet>();
        if (workNet == null)
            return;

        var workHours = inventory.Amount;
        var total = inventory.MaxAmount;
        if (total <= 0.0 || !MathUtil.IsFinite(total))
            return;

        var ratio = MathUtil.Clamp01(workHours / total);

        if (control.state == ELBuildControl.State.Building)
        {
            var required = control.builtStuff.required;

            foreach (var res in required)
            {
                if (res.amount <= 0.0)
                    continue;

                res.amount -= res.amount * ratio;
            }
        }
        else if (control.state == ELBuildControl.State.Canceling)
        {
            var built = control.builtStuff.required;
            var cost = control.buildCost.required;

            foreach (var bres in built)
            {
                var cres = FindResource(cost, bres.name);
                var remaining = cres.amount - bres.amount;

                if (remaining <= 0.0)
                    continue;

                bres.amount += ratio * remaining;
            }
        }

        // Prevent EL from doing its own catch-up, since we have already
        // updated the relevant work sinks.
        workNet.lastUpdate = Planetarium.GetUniversalTime();
    }

    private static BuildResource FindResource(List<BuildResource> list, string name)
    {
        foreach (var res in list)
        {
            if (res.name == name)
                return res;
        }

        return null;
    }
}
