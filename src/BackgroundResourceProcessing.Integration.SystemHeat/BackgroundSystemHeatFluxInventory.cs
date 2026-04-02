using System.Collections.Generic;
using System.Linq;
using BackgroundResourceProcessing.Core;
using BackgroundResourceProcessing.Inventory;
using SystemHeat;
using UnityEngine;

namespace BackgroundResourceProcessing.Integration.SystemHeat;

/// <summary>
/// Creates one <see cref="FakePartResource"/> per active SystemHeat loop ID found
/// on the vessel. These zero-capacity inventories represent heat-flux "pipes":
/// producers output into them with DumpExcess = true, consumers draw from them.
/// The zero MaxAmount forces the solver to treat heat as an instantaneous flow.
/// </summary>
public class BackgroundSystemHeatFluxInventory : BackgroundInventory<BRPSystemHeatMarker>
{
    public override List<FakePartResource> GetResources(BRPSystemHeatMarker module)
    {
        var simulator = module?.vessel?.FindVesselModuleImplementing<SystemHeatVessel>()?.simulator;
        if (simulator?.HeatLoops is null)
            return null;
        if (simulator.HeatLoops.Count == 0)
            return null;

        List<FakePartResource> resources = [];
        foreach (var loop in simulator.HeatLoops)
        {
            resources.Add(
                new FakePartResource()
                {
                    ResourceName = SystemHeatFlux.ResourceName(loop.ID),
                    Amount = 0.0,
                    MaxAmount = 0.0,
                }
            );
        }

        return resources;
    }

    public override void UpdateResource(BRPSystemHeatMarker module, ResourceInventory inventory) { }
}
