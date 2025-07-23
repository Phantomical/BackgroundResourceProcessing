using System;
using BackgroundResourceProcessing.Behaviour;
using BackgroundResourceProcessing.Converter;
using ExtraplanetaryLaunchpads;

namespace BackgroundResourceProcessing.Integration.EL;

public class BackgroundELExtractor : BackgroundResourceConverter<ELExtractor>
{
    protected override ConstantConverter GetBaseRecipe(ELExtractor module)
    {
        var recipe = base.GetBaseRecipe(module);
        if (UsePreparedRecipe.Evaluate(module))
            return recipe;

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

        double rate = ResourceMap.Instance.GetAbundance(request) * module.Rate;

        recipe.Outputs.Add(
            new ResourceRatio()
            {
                ResourceName = module.ResourceName,
                Ratio = rate,
                DumpExcess = false,
                FlowMode = module.flowMode,
            }
        );

        return recipe;
    }

    protected override double GetOptimalEfficiencyBonus(ELExtractor module)
    {
        return base.GetOptimalEfficiencyBonus(module) * module.Efficiency;
    }
}
