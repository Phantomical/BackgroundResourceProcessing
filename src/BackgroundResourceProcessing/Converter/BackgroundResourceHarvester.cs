using System;
using BackgroundResourceProcessing.Behaviour;

namespace BackgroundResourceProcessing.Converter;

public class BackgroundResourceHarvester : BackgroundResourceConverter<ModuleResourceHarvester>
{
    protected override ConstantConverter GetBaseRecipe(ModuleResourceHarvester module)
    {
        var recipe = base.GetBaseRecipe(module);
        if (UsePreparedRecipe.Evaluate(module))
            return recipe;

        var vessel = module.vessel;
        var type = (HarvestTypes)module.HarvesterType;

        var request = new AbundanceRequest()
        {
            Altitude = vessel.altitude,
            BodyId = vessel.mainBody.flightGlobalsIndex,
            CheckForLock = false,
            Latitude = vessel.latitude,
            Longitude = vessel.longitude,
            ResourceType = type,
            ResourceName = module.ResourceName,
        };

        if (ResourceMap.Instance == null)
            throw new NullReferenceException("ResourceMap.Instance was null");

        double rate = ResourceMap.Instance.GetAbundance(request) * module.Efficiency;
        if (type == HarvestTypes.Atmospheric)
            rate *= GetIntakeMultiplier(module);

        recipe.outputs.Add(
            new ResourceRatio()
            {
                ResourceName = module.ResourceName,
                Ratio = rate,
                DumpExcess = type == HarvestTypes.Atmospheric,
                FlowMode = ResourceFlowMode.NULL,
            }
        );

        return recipe;
    }

    private double GetIntakeMultiplier(ModuleResourceHarvester module)
    {
        // We never have to deal with resource harvesters flying through the
        // atmosphere so this is simpler than the code in ModuleResourceHarvester.
        var type = (HarvestTypes)module.HarvesterType;
        double mult = module.vessel.atmDensity;
        if (type == HarvestTypes.Exospheric)
            mult = 1.0;
        return mult * module.airSpeedStatic;
    }
}
