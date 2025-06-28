using System.Collections.Generic;
using System.Reflection;
using KSP.Localization;

namespace BackgroundResourceProcessing.Modules
{
    public abstract class ModuleBackgroundPotatoDrill : ModuleBackgroundResourceConverter
    {
        protected static readonly string NoStorageSpace = Localizer.Format("#autoLOC_258501");
        protected static readonly string InsufficientPower = Localizer.Format("#autoLOC_258451");

        protected BaseDrill BaseDrill => (BaseDrill)Converter;

        [KSPField]
        public string MassResourceName = "BRPSpaceObjectMass";

        protected override double GetOptimalEfficiencyBonus()
        {
            return base.GetOptimalEfficiencyBonus() * BaseDrill.Efficiency;
        }

        protected override ConverterResources GetAdditionalRecipe()
        {
            var potato = GetDrillPotato();
            var info = GetDrillInfo();
            if (potato == null || info == null)
                return default;

            var resources = GetPotatoResources(potato);

            var massRate = 0.0;
            var recipe = new ConverterResources();

            foreach (var resource in resources)
            {
                var definition = PartResourceLibrary.Instance.GetDefinition(resource.resourceName);

                var ratio = new ResourceRatio()
                {
                    ResourceName = resource.resourceName,
                    Ratio = resource.abundance,
                    DumpExcess = false,
                    FlowMode = ResourceFlowMode.NULL,
                };

                recipe.Outputs.Add(ratio);
                massRate += resource.abundance * definition.density;
            }

            recipe.Inputs.Add(
                new ResourceRatio()
                {
                    ResourceName = MassResourceName,
                    Ratio = massRate,
                    DumpExcess = false,
                    FlowMode = ResourceFlowMode.STAGE_STACK_FLOW,
                }
            );

            return recipe;
        }

        public override BackgroundResourceSet GetLinkedBackgroundResources()
        {
            var potato = GetDrillPotato();
            if (potato == null)
                return null;

            var synchronizer =
                potato.FindModuleImplementing<ModuleBackgroundSpaceObjectResourceSynchronizer>();
            if (synchronizer == null)
                return null;

            var resourceSet = new BackgroundResourceSet();
            resourceSet.AddPullResource(synchronizer);
            return resourceSet;
        }

        protected override bool IsConverterEnabled()
        {
            var drill = BaseDrill;

            if (drill.IsActivated)
                return true;

            // We want the drill to be active it is shut down due to resource issues
            // but otherwise we follow along with IsActivated
            return drill.status == NoStorageSpace || drill.status == InsufficientPower;
        }

        protected abstract Part GetDrillPotato();
        protected abstract ModuleSpaceObjectInfo GetDrillInfo();

        protected abstract IEnumerable<ModuleSpaceObjectResource> GetPotatoResources(Part potato);
    }

    public class ModuleBackgroundAsteroidDrill : ModuleBackgroundPotatoDrill
    {
        static readonly FieldInfo PotatoField = typeof(ModuleAsteroidDrill).GetField(
            "_potato",
            BindingFlags.Instance | BindingFlags.NonPublic
        );
        static readonly FieldInfo InfoField = typeof(ModuleAsteroidDrill).GetField(
            "_info",
            BindingFlags.Instance | BindingFlags.NonPublic
        );

        protected override BaseConverter FindLinkedModule()
        {
            return FindLinkedModuleAs<ModuleAsteroidDrill>();
        }

        protected override Part GetDrillPotato()
        {
            return (Part)PotatoField.GetValue(Converter);
        }

        protected override ModuleSpaceObjectInfo GetDrillInfo()
        {
            return (ModuleSpaceObjectInfo)InfoField.GetValue(Converter);
        }

        protected override IEnumerable<ModuleSpaceObjectResource> GetPotatoResources(Part potato)
        {
            return potato.FindModulesImplementing<ModuleAsteroidResource>();
        }
    }

    public class ModuleBackgroundCometDrill : ModuleBackgroundPotatoDrill
    {
        static readonly FieldInfo PotatoField = typeof(ModuleCometDrill).GetField(
            "_potato",
            BindingFlags.Instance | BindingFlags.NonPublic
        );
        static readonly FieldInfo InfoField = typeof(ModuleCometDrill).GetField(
            "_info",
            BindingFlags.Instance | BindingFlags.NonPublic
        );

        protected override BaseConverter FindLinkedModule()
        {
            return FindLinkedModuleAs<ModuleCometDrill>();
        }

        protected override Part GetDrillPotato()
        {
            return (Part)PotatoField.GetValue(Converter);
        }

        protected override ModuleSpaceObjectInfo GetDrillInfo()
        {
            return (ModuleSpaceObjectInfo)InfoField.GetValue(Converter);
        }

        protected override IEnumerable<ModuleSpaceObjectResource> GetPotatoResources(Part potato)
        {
            return potato.FindModulesImplementing<ModuleCometResource>();
        }
    }
}
