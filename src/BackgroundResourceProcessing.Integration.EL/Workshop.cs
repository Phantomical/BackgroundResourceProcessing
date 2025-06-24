using System.Collections.Generic;
using System.Linq;
using BackgroundResourceProcessing.Modules;
using ExtraplanetaryLaunchpads;

namespace BackgroundResourceProcessing.Integration.EL
{
    public interface IBackgroundELWorkSink : IBackgroundPartResource { }

    public class ModuleBackgroundELWorkshop : ModuleBackgroundConverter
    {
        private ELWorkshop module;

        [KSPField(isPersistant = true)]
        private uint cachedPersistentModuleId;

        protected override List<ConverterBehaviour> GetConverterBehaviours()
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

            return [new ConstantConverter(inputs.ToList(), outputs.ToList(), required)];
        }

        private ELWorkshop GetLinkedWorkshop()
        {
            if (module != null)
                return module;

            var workshop = LinkedModuleUtil.GetLinkedModule<ELWorkshop>(
                this,
                ref cachedPersistentModuleId
            );
            module = workshop;
            return workshop;
        }
    }
}
