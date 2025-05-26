using System.Collections.Generic;
using System.Linq;
using BackgroundResourceProcessing.Modules;
using ExtraplanetaryLaunchpads;
using HarmonyLib;

namespace BackgroundResourceProcessing.Integration.EL
{
    public class ModuleBackgroundELWorkshop : ModuleBackgroundConstantConverter
    {
        private ELWorkshop module;

        [KSPField(isPersistant = true)]
        private uint? cachedPersistentModuleId;

        protected override ConverterBehaviour GetConverterBehaviour()
        {
            var workshop = GetLinkedWorkshop();
            if (workshop == null)
                return null;
            if (!workshop.isActive)
                return null;

            var productivity = workshop.Productivity;
            if (productivity == 0.0)
                return null;

            IEnumerable<ResourceRatio> inputs = this.inputs;
            IEnumerable<ResourceRatio> outputs = this.outputs;

            if (productivity != 1.0)
            {
                inputs = inputs.Select(input => input.WithMultiplier(productivity));
                outputs = outputs.Select(output => output.WithMultiplier(productivity));
            }

            return new ConstantConverter(inputs.ToList(), outputs.ToList(), required);
        }

        private ELWorkshop GetLinkedWorkshop()
        {
            if (module != null)
                return module;

            var workshop = GetCachedLinkedWorkshop() ?? part.FindModuleImplementing<ELWorkshop>();
            module = workshop;
            cachedPersistentModuleId = module.PersistentId;
            return workshop;
        }

        private ELWorkshop GetCachedLinkedWorkshop()
        {
            if (cachedPersistentModuleId == null)
                return null;

            var moduleId = (uint)cachedPersistentModuleId;
            var module = part.Modules[moduleId];
            if (module is not ELWorkshop workshop)
                return null;

            return workshop;
        }
    }

    public class ModuleBackgroundELLaunchpad : ModuleBackgroundConstantConverter
    {
        // This is the amount of work hours that
        [KSPField(isPersistant = true)]
        private double bankedWorkHours = 0.0;
    }

    [HarmonyPatch(typeof(ELWorkshop))]
    static class ELWorkshop_Patch { }

    internal static class ResourceRatioExtension
    {
        public static ResourceRatio WithMultiplier(this ResourceRatio res, double multiplier)
        {
            res.Ratio *= multiplier;
            return res;
        }
    }
}
