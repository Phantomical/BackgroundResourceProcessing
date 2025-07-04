using System;
using BackgroundResourceProcessing.Converter;
using ExtraplanetaryLaunchpads;

namespace BackgroundResourceProcessing.Integration.EL;

public class BackgroundELExtractor : BackgroundResourceConverter<ELExtractor>
{
    protected override ConverterResources GetAdditionalRecipe(ELExtractor module)
    {
        if (UsePreparedRecipe)
            return default;

        var vessel = module.vessel;
        var request = new AbundanceRequest()
        {
            Altitude = vessel.altitude,
            BodyId = vessel.mainBody.flightGlobalsIndex,
            CheckForLock = false,
            Latitude = vessel.latitude,
            Longitude = vessel.longitude,
            ResourceType = HarvestTypes.Planetary,
            ResourceName = module.ResourceName,
        };

        if (ResourceMap.Instance == null)
            throw new NullReferenceException("ResourceMap.Instance was null");

        double rate = ResourceMap.Instance.GetAbundance(request);
        rate *= module.Rate * module.Efficiency;

        ConverterResources recipe = default;
        recipe.Outputs =
        [
            new ResourceRatio()
            {
                ResourceName = module.ResourceName,
                Ratio = rate,
                DumpExcess = false,
                FlowMode = module.flowMode,
            },
        ];

        return recipe;
    }
}
