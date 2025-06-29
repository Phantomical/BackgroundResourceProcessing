using System;
using System.Collections.Generic;
using BackgroundResourceProcessing.Core;

namespace BackgroundResourceProcessing.Inventory
{
    /// <summary>
    /// This is a fake inventory that wraps a <see cref="ModuleSpaceObjectInfo"/>
    /// and exposes its remaining mass as a configurable resource inventory.
    /// </summary>
    public class BackgroundSpaceObjectMass : BackgroundInventory<ModuleSpaceObjectInfo>
    {
        [KSPField]
        public string ResourceName = "BRPSpaceObjectMass";

        public override List<FakePartResource> GetResources(ModuleSpaceObjectInfo module)
        {
            var remainingMass = Math.Max(module.currentMassVal - module.massThresholdVal, 0.0);

            FakePartResource resource = new()
            {
                resourceName = ResourceName,
                amount = remainingMass,
                maxAmount = remainingMass,
            };

            return [resource];
        }

        public override void UpdateResource(
            ModuleSpaceObjectInfo module,
            ResourceInventory inventory
        )
        {
            if (inventory.resourceName != ResourceName)
                return;

            module.currentMassVal = module.massThresholdVal + inventory.amount;
        }
    }
}
