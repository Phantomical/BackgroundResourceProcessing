using System.Collections.Generic;
using BackgroundResourceProcessing.Core;
using BackgroundResourceProcessing.Utils;

namespace BackgroundResourceProcessing.Inventory;

public class BackgroundScienceLabInventory : BackgroundInventory<ModuleScienceLab>
{
    [KSPField]
    public string DataResourceName = "BRPScienceLabData";

    [KSPField]
    public string ScienceResourceName = "BRPScience";

    public override List<FakePartResource> GetResources(ModuleScienceLab lab)
    {
        FakePartResource data = new()
        {
            resourceName = DataResourceName,
            amount = lab.dataStored,
            maxAmount = lab.dataStorage,
        };

        FakePartResource science = new()
        {
            resourceName = ScienceResourceName,
            amount = lab.storedScience,
            // So for some reason the science cap is actually specified on the
            // science converter module??
            maxAmount = lab.Converter?.scienceCap ?? double.PositiveInfinity,
        };

        return [data, science];
    }

    public override void UpdateResource(ModuleScienceLab lab, ResourceInventory inventory)
    {
        if (inventory.resourceName == DataResourceName)
        {
            var delta = lab.dataStored - inventory.amount;
            lab.dataStored = (float)MathUtil.Clamp(inventory.amount + delta, 0.0, lab.dataStorage);
        }
        else if (inventory.resourceName == ScienceResourceName)
        {
            var delta = lab.storedScience - inventory.amount;
            lab.storedScience = (float)
                MathUtil.Clamp(inventory.amount + delta, 0.0, inventory.maxAmount);
        }
    }

    public override void UpdateSnapshot(
        ProtoPartModuleSnapshot module,
        ResourceInventory inventory,
        SnapshotUpdate update
    )
    {
        var node = module.moduleValues;

        if (inventory.resourceName == DataResourceName)
        {
            node.TryGetValue("dataStored", ref inventory.amount);
            base.UpdateSnapshot(module, inventory, update);
            node.SetValue("dataStored", inventory.amount, createIfNotFound: true);
            inventory.originalAmount = inventory.amount;
        }
        else if (inventory.resourceName == ScienceResourceName)
        {
            node.TryGetValue("storedSciencec", ref inventory.amount);
            base.UpdateSnapshot(module, inventory, update);
            node.SetValue("storedScience", inventory.amount);
            inventory.originalAmount = inventory.amount;
        }
        else
        {
            base.UpdateSnapshot(module, inventory, update);
        }
    }
}
