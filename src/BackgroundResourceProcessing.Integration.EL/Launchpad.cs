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
/// <para>
///   This module works by taking a <see cref="WorkHoursResource"/> and
///   either consuming or building the required resources for the ship
///   being built. It expects a <see cref="BackgroundELLaunchpadInventory"/>
///   to be present on the same module which will update the EL WorkNet.
/// </para>
///
/// <para>
///   EL builds every resource independently: <c>ELBuildControl.DoWork_Build</c>
///   splits the available work between the resources that still need building
///   and that have stock on the pad, so running out of one resource doesn't
///   stall the others. We model that with one converter per resource, each of
///   which records its progress in its own inventory on the launchpad.
/// </para>
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
        if (control.paused)
            return null;

        List<ConverterBehaviour> converters = [];
        if (control.state == ELBuildControl.State.Building)
        {
            foreach (var res in control.builtStuff.required)
            {
                if (res.amount <= 0.0)
                    continue;

                var work = res.kerbalHours * res.amount;
                if (!(work > 0.0))
                    continue;

                List<ResourceRatio> inputs =
                [
                    WorkInput(work),
                    new()
                    {
                        ResourceName = res.name,
                        Ratio = res.amount,
                        FlowMode = ResourceFlowMode.ALL_VESSEL_BALANCE,
                    },
                ];
                List<ResourceRatio> outputs = [ProgressOutput(res.name, work)];

                converters.Add(new ConstantConverter(inputs, outputs, []));
            }
        }
        else if (control.state == ELBuildControl.State.Canceling)
        {
            var cost = control.buildCost.required;
            foreach (var bres in control.builtStuff.required)
            {
                var cres = BackgroundELLaunchpadInventory.FindResource(cost, bres.name);
                if (cres == null)
                    continue;

                var remaining = cres.amount - bres.amount;
                if (remaining <= 0.0)
                    continue;

                var work = bres.kerbalHours * remaining;
                if (!(work > 0.0))
                    continue;

                List<ResourceRatio> inputs = [WorkInput(work)];
                List<ResourceRatio> outputs =
                [
                    ProgressOutput(bres.name, work),
                    new()
                    {
                        ResourceName = bres.name,
                        Ratio = remaining,
                        FlowMode = ResourceFlowMode.ALL_VESSEL_BALANCE,
                        DumpExcess = true,
                    },
                ];

                converters.Add(new ConstantConverter(inputs, outputs, []));
            }
        }

        if (converters.Count == 0)
            return null;

        ModuleBehaviour behaviour = new(converters);
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

    private ResourceRatio WorkInput(double work) =>
        new()
        {
            ResourceName = WorkHoursResource,
            Ratio = work,
            FlowMode = ResourceFlowMode.ALL_VESSEL,
        };

    private ResourceRatio ProgressOutput(string resourceName, double work) =>
        new()
        {
            ResourceName = BackgroundELLaunchpadInventory.ProgressResourceName(
                WorkHoursResource,
                resourceName
            ),
            Ratio = work,
            FlowMode = ResourceFlowMode.NO_FLOW,
        };
}

/// <summary>
/// The inventories that track how much work has been done on each resource of
/// the ship being built (or cancelled). On resume it updates the state of its
/// linked <see cref="ELLaunchpad"/>.
/// </summary>
public class BackgroundELLaunchpadInventory : BackgroundInventory<ELLaunchpad>
{
    [KSPField]
    public string WorkHoursResource = "BRPELWorkHours";

    /// <summary>
    /// The name of the inventory that tracks the work done on one build
    /// resource. These only ever exist as background inventories so they
    /// don't need a resource definition.
    /// </summary>
    internal static string ProgressResourceName(string workHoursResource, string resourceName) =>
        $"{workHoursResource}:{resourceName}";

    public override List<FakePartResource> GetResources(ELLaunchpad launchpad)
    {
        var control = launchpad.control;
        if (control == null)
            return null;
        if (!control.isActive)
            return null;

        List<FakePartResource> resources = [];
        if (control.state == ELBuildControl.State.Building)
        {
            foreach (var res in control.builtStuff.required)
            {
                if (res.amount <= 0.0)
                    continue;

                var work = res.kerbalHours * res.amount;
                if (!(work > 0.0))
                    continue;

                resources.Add(Progress(res.name, work));
            }
        }
        else if (control.state == ELBuildControl.State.Canceling)
        {
            var cost = control.buildCost.required;
            foreach (var bres in control.builtStuff.required)
            {
                var cres = FindResource(cost, bres.name);
                if (cres == null)
                    continue;

                var work = bres.kerbalHours * (cres.amount - bres.amount);
                if (!(work > 0.0))
                    continue;

                resources.Add(Progress(bres.name, work));
            }
        }

        return resources;
    }

    private FakePartResource Progress(string resourceName, double work) =>
        new()
        {
            ResourceName = ProgressResourceName(WorkHoursResource, resourceName),
            Amount = 0.0,
            MaxAmount = work,
        };

    public override void UpdateResource(ELLaunchpad launchpad, ResourceInventory inventory)
    {
        // The main thing we need to do here is to update the launchpad's
        // tracked resources to account for the work done in the background.

        var prefix = WorkHoursResource + ":";

        // Older versions tracked the progress of every resource together in a
        // single inventory named after the work hours resource.
        bool legacy = inventory.ResourceName == WorkHoursResource;
        if (!legacy && !inventory.ResourceName.StartsWith(prefix))
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

        // BRP has been responsible for this build while the vessel was
        // unloaded, even if no progress was made (e.g. because a resource ran
        // out). Prevent EL from doing its own catch-up, which would otherwise
        // apply up to a full catch-up interval of work on load.
        workNet.lastUpdate = Planetarium.GetUniversalTime();

        if (inventory.Amount == 0.0)
            return;

        var total = inventory.MaxAmount;
        if (total <= 0.0 || !MathUtil.IsFinite(total))
            return;

        var ratio = MathUtil.Clamp01(inventory.Amount / total);
        string only = legacy ? null : inventory.ResourceName.Substring(prefix.Length);

        if (control.state == ELBuildControl.State.Building)
        {
            foreach (var res in control.builtStuff.required)
            {
                if (only != null && res.name != only)
                    continue;
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
                if (only != null && bres.name != only)
                    continue;

                var cres = FindResource(cost, bres.name);
                if (cres == null)
                    continue;

                var remaining = cres.amount - bres.amount;
                if (remaining <= 0.0)
                    continue;

                bres.amount += ratio * remaining;
            }
        }
    }

    internal static BuildResource FindResource(List<BuildResource> list, string name)
    {
        foreach (var res in list)
        {
            if (res.name == name)
                return res;
        }

        return null;
    }
}
