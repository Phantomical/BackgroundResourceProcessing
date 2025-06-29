using System.Collections.Generic;
using System.Reflection;
using KSP.Localization;

namespace BackgroundResourceProcessing.Converter
{
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

        public override ModuleBehaviour GetBehaviour(T module)
        {
            var behaviour = base.GetBehaviour(module);
            if (behaviour == null)
                return behaviour;

            var potato = GetDrillPotato(module);
            if (potato == null)
                return behaviour;

            var info = GetInfo(module);
            if (info != null)
                behaviour.AddPullModule(info);

            return behaviour;
        }

        protected override ConverterResources GetAdditionalRecipe(T module)
        {
            var potato = GetDrillPotato(module);
            if (potato == null)
                return default;

            var resources = potato.FindModulesImplementing<ModuleSpaceObjectResource>();
            var massRate = 0.0;
            var outputs = new List<ResourceRatio>();
            var inputs = new List<ResourceRatio>();

            if (!UseReturnedRecipe)
                inputs.Add(GetPowerConsumption(module));

            foreach (var resource in resources)
            {
                var definition = PartResourceLibrary.Instance.GetDefinition(resource.resourceName);
                var ratio = new ResourceRatio
                {
                    ResourceName = resource.resourceName,
                    Ratio = resource.abundance,
                    DumpExcess = false,
                    FlowMode = ResourceFlowMode.NULL,
                };

                if (!UseReturnedRecipe)
                    outputs.Add(ratio);
                massRate += resource.abundance * definition.density;
            }

            inputs.Add(new(MassResourceName, massRate, false, ResourceFlowMode.NO_FLOW));

            return new() { Inputs = inputs, Outputs = outputs };
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

            // We want the drill to be active it is shut down due to resource issues
            // but otherwise we follow along with IsActivated
            return drill.status == NoStorageSpace || drill.status == InsufficientPower;
        }

        protected override double GetOptimalEfficiencyBonus(T converter, RecipeOverride recipe)
        {
            return base.GetOptimalEfficiencyBonus(converter, recipe) * converter.Efficiency;
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
}
