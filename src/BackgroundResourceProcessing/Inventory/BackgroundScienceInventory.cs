using System;
using System.Collections.Generic;
using System.Reflection;
using BackgroundResourceProcessing.Core;

namespace BackgroundResourceProcessing.Inventory
{
    public class BackgroundScienceInventory : BackgroundInventory<ModuleScienceConverter>
    {
        private static readonly FieldInfo LastUpdateTimeField =
            typeof(ModuleScienceConverter).GetField(
                "lastUpdateTime",
                BindingFlags.Instance | BindingFlags.NonPublic
            );

        [KSPField]
        public string TimePassedResourceName = "BRPScienceLabTime";

        public override List<FakePartResource> GetResources(ModuleScienceConverter module)
        {
            var lastUpdate = (double)LastUpdateTimeField.GetValue(module);

            FakePartResource resource = new()
            {
                resourceName = TimePassedResourceName,
                amount = lastUpdate,
                maxAmount = double.PositiveInfinity,
            };

            return [resource];
        }

        public override void UpdateResource(
            ModuleScienceConverter module,
            ResourceInventory inventory
        )
        {
            if (inventory.resourceName != TimePassedResourceName)
                return;

            var now = Planetarium.GetUniversalTime();
            var lastUpdate = (double)LastUpdateTimeField.GetValue(module);
            var savedUpdate = inventory.originalAmount;

            // The amount of time that was simulated without us noticing.
            // This can happen if other mods happen to be doing things to
            // labs in the background.
            //
            // In that case, we try to remain accurate, but this may not be
            // possible depending on how updates have happened.
            var missed = Math.Max(lastUpdate - savedUpdate, 0.0);

            // If we missed more time than we should have, we just saturate
            // to say that no time has passed.
            var amount = Math.Max(inventory.amount - missed, 0.0);

            var newUpdate = now - amount;
            LastUpdateTimeField.SetValue(module, newUpdate);
        }
    }
}
