using System.Collections.Generic;
using System.Reflection;
using BackgroundResourceProcessing.Behaviour;
using KSP.Localization;

namespace BackgroundResourceProcessing.Converter;

public abstract class BackgroundSpaceObjectDrill<T> : BackgroundResourceConverter<T>
    where T : BaseDrill
{
    private const BindingFlags Flags =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private static readonly FieldInfo PotatoField = typeof(T).GetField("_potato", Flags);
    private static readonly FieldInfo InfoField = typeof(T).GetField("_info", Flags);

    protected static readonly string NoStorageSpace = Localizer.Format("#autoLOC_258501");
    protected static readonly string InsufficientPower = Localizer.Format("#autoLOC_258451");

    [KSPField]
    public string MassResourceName = "BRPSpaceObjectMass";

    protected override ConstantConverter GetMultipliedRecipe(T module)
    {
        var recipe = GetBaseRecipe(module);

        var potato = GetDrillPotato(module);
        if (potato == null)
            return null;

        var massRate = 0.0;
        var usePreparedRecipe = UsePreparedRecipe.Evaluate(module);
        if (usePreparedRecipe)
        {
            foreach (var output in recipe.Outputs)
            {
                var definition = PartResourceLibrary.Instance.GetDefinition(output.ResourceName);
                if (definition == null)
                    continue;

                massRate += output.Ratio * definition.density;
            }
        }
        else
        {
            var resources = potato.FindModulesImplementing<ModuleSpaceObjectResource>();
            var efficiency = GetConfiguredEfficiencyBonus(module);

            recipe.Inputs.Add(GetPowerConsumption(module));

            // ModuleAsteroidDrill.PrepareRecipe skips resources with
            // negligible abundance and dumps whatever doesn't fit, but only
            // drills while at least one resource has room. We can't express
            // that "any output has room" condition directly, so dump the
            // resources this vessel has no storage for and leave the rest as
            // regular outputs. A vessel with only an Ore tank then still
            // mines Ore from a multi-resource asteroid, and still stops once
            // that tank is full.
            var storable = GetStorableResources(module.vessel);
            bool anyStorable = false;
            foreach (var resource in resources)
            {
                if (resource.abundance <= 1e-9)
                    continue;
                if (storable.Contains(resource.resourceName))
                    anyStorable = true;
            }

            foreach (var resource in resources)
            {
                if (resource.abundance <= 1e-9)
                    continue;

                var definition = PartResourceLibrary.Instance.GetDefinition(resource.resourceName);
                if (definition == null)
                    continue;

                var ratio = new ResourceRatio
                {
                    ResourceName = resource.resourceName,
                    Ratio = resource.abundance * efficiency,
                    DumpExcess = anyStorable && !storable.Contains(resource.resourceName),
                    FlowMode = ResourceFlowMode.NULL,
                };

                recipe.Outputs.Add(ratio);
                massRate += resource.abundance * definition.density * efficiency;
            }
        }

        recipe.Inputs.Add(new(MassResourceName, massRate, false, ResourceFlowMode.NO_FLOW));
        return recipe;
    }

    public override ModuleBehaviour GetBehaviour(T module)
    {
        var behaviour = base.GetBehaviour(module);
        if (behaviour == null)
            return null;

        var info = GetInfo(module);
        if (info != null)
            behaviour.AddPullModule(info);

        return behaviour;
    }

    static HashSet<string> GetStorableResources(Vessel vessel)
    {
        var storable = new HashSet<string>();
        if (vessel == null)
            return storable;

        foreach (var part in vessel.parts)
        {
            foreach (var resource in part.Resources)
            {
                if (resource.maxAmount > 0.0)
                    storable.Add(resource.resourceName);
            }
        }

        return storable;
    }

    protected virtual Part GetDrillPotato(T module)
    {
        return (Part)PotatoField.GetValue(module);
    }

    protected virtual ModuleSpaceObjectInfo GetInfo(T module)
    {
        return (ModuleSpaceObjectInfo)InfoField.GetValue(module);
    }

    protected abstract ResourceRatio GetPowerConsumption(T module);

    protected override bool IsConverterEnabled(T drill)
    {
        if (drill.IsActivated)
            return true;

        // We want the drill to be active but it is shut down due to resource issues
        // but otherwise we follow along with IsActivated
        return drill.status == NoStorageSpace || drill.status == InsufficientPower;
    }

    protected override double GetOptimalEfficiencyBonus(T converter)
    {
        return base.GetOptimalEfficiencyBonus(converter) * converter.Efficiency;
    }
}

public class BackgroundAsteroidDrill : BackgroundSpaceObjectDrill<ModuleAsteroidDrill>
{
    protected override ResourceRatio GetPowerConsumption(ModuleAsteroidDrill module)
    {
        return new("ElectricCharge", module.PowerConsumption, false);
    }
}

public class BackgroundCometDrill : BackgroundSpaceObjectDrill<ModuleCometDrill>
{
    protected override ResourceRatio GetPowerConsumption(ModuleCometDrill module)
    {
        return new("ElectricCharge", module.PowerConsumption, false);
    }
}
