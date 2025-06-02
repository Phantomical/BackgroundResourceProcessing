using System;
using System.Collections.Generic;
using UnityEngine;

namespace BackgroundResourceProcessing.Modules
{
    /// <summary>
    /// A module that synchronizes the mass of a space object with a resource.
    /// </summary>
    public sealed class ModuleBackgroundSpaceObjectResourceSynchronizer
        : PartModule,
            IBackgroundPartResource
    {
        [KSPField]
        public string ResourceName;

        private ModuleSpaceObjectInfo info;
        private BackgroundResourceProcessor processor;

        public IEnumerable<FakePartResource> GetResources()
        {
            var info = GetLinkedInfo();
            if (info == null)
            {
                LogUtil.Warn($"{GetType().Name}: SpaceObject has no linked ModuleSpaceObjectInfo");
                return null;
            }

            FakePartResource resource = new()
            {
                resourceName = ResourceName,
                amount = Math.Max(info.currentMassVal - info.massThresholdVal, 0.0),
            };
            resource.maxAmount = resource.amount;
            return [resource];
        }

        public void UpdateStoredAmount(string resourceName, double amount)
        {
            if (resourceName != ResourceName)
                return;

            var info = GetLinkedInfo();
            if (info == null)
                return;

            info.currentMassVal = info.massThresholdVal + amount;
        }

        private ModuleSpaceObjectInfo GetLinkedInfo()
        {
            if (info != null)
                return info;

            info = part.FindModuleImplementing<ModuleSpaceObjectInfo>();
            return info;
        }

        private BackgroundResourceProcessor GetLinkedProcessor()
        {
            if (processor != null)
                return processor;

            if (vessel == null)
                return null;

            processor = vessel.FindVesselModuleImplementing<BackgroundResourceProcessor>();
            return processor;
        }
    }
}
