using System.Collections.Generic;
using BackgroundResourceProcessing.Collections;

namespace BackgroundResourceProcessing.BurstSolver;

internal static class ResourceNames
{
    /// <summary>
    /// This is really only ever used for testing, so feel free to add a
    /// resource here if you want to see it when debugging.
    /// </summary>
    static readonly Dictionary<int, string> ResourceList = MakeResourceNameList(
        [
            // Stock Resources
            "ElectricCharge",
            "LiquidFuel",
            "Oxidizer",
            "MonoPropellant",
            "Ore",
            "AblativeShielding",
            // BRP Resources
            "BRPAntimatterDetonationPotential",
            "BRPWarpDriveEMLossPotential",
            "BRPScience",
            "BRPScienceLabData",
            "BRPSpaceObjectMass",
            "BRPCryoTankBoiloff",
            "BRPELWorkHours",
            // CRP Resources
            "MetalOre",
            "Metal",
            "CarbonDioxide",
            "Water",
            "Carbon",
            "CarbonMonoxide",
            "Hydrogen",
            "LqdHydrogen",
            "DepletedFuel",
            "EnrichedUranium",
            // USI-LS
            "Supplies",
            "Mulch",
            "Fertilizer",
        ]
    );

    public static string GetResourceName(int resourceId)
    {
        var prl = PartResourceLibrary.Instance;
        if (prl is not null)
        {
            var def = prl.GetDefinition(resourceId);
            if (def is not null)
                return def.name;
        }

        if (ResourceList.TryGetValue(resourceId, out var name))
            return name;

        return resourceId.ToString();
    }

    private static Dictionary<int, string> MakeResourceNameList(IEnumerable<string> resources)
    {
        Dictionary<int, string> dict = [];
        foreach (var resource in resources)
            dict.TryAddExt(resource.GetHashCode(), resource);
        return dict;
    }
}
