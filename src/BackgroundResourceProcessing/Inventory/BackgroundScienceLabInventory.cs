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
            ResourceName = DataResourceName,
            Amount = lab.dataStored,
            MaxAmount = lab.dataStorage,
        };

        FakePartResource science = new()
        {
            ResourceName = ScienceResourceName,
            Amount = lab.storedScience,
            // So for some reason the science cap is actually specified on the
            // science converter module??
            MaxAmount = lab.Converter?.scienceCap ?? double.PositiveInfinity,
        };

        return [data, science];
    }

    public override void UpdateResource(ModuleScienceLab lab, ResourceInventory inventory)
    {
        var settings = HighLogic.CurrentGame.Parameters.CustomParams<Settings>();
        if (!settings.EnableBackgroundScienceLabProcessing)
            return;

        if (inventory.ResourceName == DataResourceName)
        {
            var delta = lab.dataStored - inventory.Amount;
            lab.dataStored = (float)MathUtil.Clamp(inventory.Amount + delta, 0.0, lab.dataStorage);
        }
        else if (inventory.ResourceName == ScienceResourceName)
        {
            var delta = lab.storedScience - inventory.Amount;
            lab.storedScience = (float)
                MathUtil.Clamp(inventory.Amount + delta, 0.0, inventory.MaxAmount);
        }
    }

    public override void UpdateSnapshot(
        ProtoPartModuleSnapshot module,
        ResourceInventory inventory,
        SnapshotUpdate update
    )
    {
        var settings = HighLogic.CurrentGame.Parameters.CustomParams<Settings>();
        if (!settings.EnableBackgroundScienceLabProcessing)
        {
            base.UpdateSnapshot(module, inventory, update);
            return;
        }

        var node = module.moduleValues;

        if (inventory.ResourceName == DataResourceName)
        {
            node.TryGetValue("dataStored", ref inventory.Amount);
            base.UpdateSnapshot(module, inventory, update);
            node.SetValue("dataStored", inventory.Amount, createIfNotFound: true);
            inventory.OriginalAmount = inventory.Amount;
        }
        else if (inventory.ResourceName == ScienceResourceName)
        {
            node.TryGetValue("storedScience", ref inventory.Amount);
            base.UpdateSnapshot(module, inventory, update);
            node.SetValue("storedScience", inventory.Amount);
            inventory.OriginalAmount = inventory.Amount;
        }
        else
        {
            base.UpdateSnapshot(module, inventory, update);
        }
    }
}
