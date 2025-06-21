using System;
using System.Collections.Generic;
using BackgroundResourceProcessing.Modules;
using ExtraplanetaryLaunchpads;
using UnityEngine;

namespace BackgroundResourceProcessing.Integration.EL
{
    /// <summary>
    /// A background converter for an EL launchpad.
    /// </summary>
    ///
    /// <remarks>
    ///   This module works by taking a <see cref="WorkHoursResource"/> and
    ///   either consuming or building the required resources for the ship being
    ///   built. It has a fake inventory that tracks the amount of work that
    ///   has been done, then when the ship is loaded again, it updates the
    ///   resources used by the linked launchpad module.
    /// </remarks>
    public class ModuleBackgroundELLaunchpad : BackgroundConverterBase, IBackgroundELWorkSink
    {
        private ELLaunchpad module;

        [KSPField(isPersistant = true)]
        private uint cachedPersistentModuleId;

        [KSPField(isPersistant = true)]
        private double totalWork = 0.0;

        [KSPField]
        public string WorkHoursResource;

        protected override ConverterBehaviour GetConverterBehaviour()
        {
            var launchpad = GetLinkedLaunchpad();
            if (launchpad == null)
                return null;

            var control = launchpad.control;
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
                new ResourceRatio()
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

            return new ConstantConverter(inputs, outputs);
        }

        public override BackgroundResourceSet GetLinkedBackgroundResources()
        {
            var set = new BackgroundResourceSet();
            set.AddPushResource(this);
            return set;
        }

        public IEnumerable<FakePartResource> GetResources()
        {
            var launchpad = GetLinkedLaunchpad();
            if (launchpad == null)
                return null;

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

            totalWork = work;

            return [output];
        }

        public void UpdateStoredAmount(string resourceName, double amount)
        {
            // The main thing we need to do here is to update the launchpad's
            // tracked resources to account for the work done in the background.

            if (resourceName != WorkHoursResource)
                return;
            if (amount == 0.0)
                return;

            var launchpad = GetLinkedLaunchpad();
            if (launchpad == null)
                return;

            var control = launchpad.control;
            if (control == null)
                return;
            if (!control.isActive)
                return;

            // This method is called before OnStart so control hasn't yet had
            // a chance to actually find the vessel WorkNet module.
            var workNet = vessel.FindVesselModuleImplementing<ELVesselWorkNet>();
            if (workNet == null)
                return;

            var workHours = amount;
            var total = totalWork;
            if (totalWork == 0.0)
                total = control.CalculateWork();
            if (totalWork <= 0.0 || !MathUtil.IsFinite(totalWork))
                return;

            var ratio = MathUtil.Clamp01(workHours / total);

            Debug.Log($"[BRP] Ratio is {ratio:g4} = {workHours:g4}/{total:g4}");

            if (control.state == ELBuildControl.State.Building)
            {
                var required = control.builtStuff.required;

                foreach (var res in required)
                {
                    if (res.amount <= 0.0)
                        continue;

                    Debug.Log(
                        $"[BRP] Resource {res.name}: current={res.amount:g4} delta={res.amount * ratio}"
                    );

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
            totalWork = 0.0;
        }

        private ELLaunchpad GetLinkedLaunchpad()
        {
            if (module != null)
                return module;

            var launchpad = LinkedModuleUtil.GetLinkedModule<ELLaunchpad>(
                this,
                ref cachedPersistentModuleId
            );
            module = launchpad;
            return launchpad;
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
}
