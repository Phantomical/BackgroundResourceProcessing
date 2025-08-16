using System.Collections.Generic;
using System.Linq;
using BackgroundResourceProcessing.Core;
using BackgroundResourceProcessing.Inventory;
using SimpleBoiloff;

namespace BackgroundResourceProcessing.Integration.CryoTanks;

public class BackgroundCryoTankBoiloffInventory : BackgroundInventory<ModuleCryoTank>
{
    [KSPField]
    public string BoiloffResource = "BRPCryoTankBoiloff";

    public override List<FakePartResource> GetResources(ModuleCryoTank module)
    {
        if (!module.HasAnyBoiloffResource)
            return null;

        List<BoiloffFuel> fuels =
        [
            .. BackgroundCryoTank.GetFuels(module).Where(fuel => fuel.fuelPresent),
        ];
        List<FakePartResource> resources = [];
        foreach (var fuel in fuels)
        {
            string boiloff;
            if (fuels.Count == 1)
                boiloff = BoiloffResource;
            else
                boiloff = $"{BoiloffResource}:{fuel.fuelName}";

            resources.Add(
                new FakePartResource()
                {
                    ResourceName = boiloff,
                    Amount = 0.0,
                    MaxAmount = 0.0,
                }
            );
        }

        return resources;
    }

    public override void UpdateResource(ModuleCryoTank module, ResourceInventory inventory) { }
}
