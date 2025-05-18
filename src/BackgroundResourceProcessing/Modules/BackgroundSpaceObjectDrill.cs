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
        public string MassResourceName;

        protected override double GetOptimalEfficiencyBonus()
        {
            return base.GetOptimalEfficiencyBonus() * BaseDrill.Efficiency;
        }

        protected override ConversionRecipe GetAdditionalRecipe()
        {
            var potato = GetDrillPotato();
            var info = GetDrillInfo();
            if (potato == null || info == null)
                return null;

            var resources = GetPotatoResources(potato);

            var massRate = 0.0;
            var recipe = new ConversionRecipe();
            var outputs = new List<ResourceRatio>();

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

                outputs.Add(ratio);
                massRate += resource.abundance * definition.density;
            }

            recipe.SetInputs(
                [
                    new ResourceRatio()
                    {
                        ResourceName = MassResourceName,
                        Ratio = massRate,
                        DumpExcess = false,
                        FlowMode = ResourceFlowMode.STAGE_STACK_FLOW,
                    },
                ]
            );
            recipe.SetOutputs(outputs);

            // We want to connect drills to only the asteroid mass resource
            // belonging to the asteroid they are connected to.
            UpdatePartCrossfeedSet(potato);

            return recipe;
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

        // We want this drill to be connected to only the asteroid that it is
        // drilling from.
        private void UpdatePartCrossfeedSet(Part potato)
        {
            var asteroidMassResource = potato.Resources.Get(MassResourceName);
            if (asteroidMassResource == null)
                return;

            var resourceId = asteroidMassResource.resourceName.GetHashCode();
            var prioritySet = part.crossfeedPartSet.GetResourceList(
                resourceId,
                pulling: true,
                simulate: false
            );

            if (prioritySet.set.Contains(asteroidMassResource))
                return;

            prioritySet.set.Add(asteroidMassResource);
            if (prioritySet.lists.Count == 0)
                prioritySet.lists.Add([]);
            prioritySet.lists[0].Add(asteroidMassResource);
        }

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

        protected override BaseConverter GetLinkedBaseConverter()
        {
            return GetLinkedBaseConverterGeneric<ModuleAsteroidDrill>();
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

        protected override BaseConverter GetLinkedBaseConverter()
        {
            return GetLinkedBaseConverterGeneric<ModuleCometDrill>();
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
