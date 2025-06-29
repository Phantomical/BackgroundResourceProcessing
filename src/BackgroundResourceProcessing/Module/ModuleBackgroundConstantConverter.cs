using System.Collections.Generic;
using BackgroundResourceProcessing.Converter;

namespace BackgroundResourceProcessing.Module
{
    /// <summary>
    /// A converter that always runs a constant rate.
    /// </summary>
    public class ModuleBackgroundConstantConverter : PartModule, IBackgroundConverter
    {
        public List<ResourceRatio> inputs = [];
        public List<ResourceRatio> outputs = [];
        public List<ResourceConstraint> required = [];

        public virtual ModuleBehaviour GetBehaviour()
        {
            return new(new ConstantConverter(inputs, outputs, required));
        }

        public virtual void OnRestore(ResourceConverter converter) { }

        public override void OnLoad(ConfigNode node)
        {
            base.OnLoad(node);

            inputs.AddRange(ConfigUtil.LoadInputResources(node));
            outputs.AddRange(ConfigUtil.LoadOutputResources(node));
            required.AddRange(ConfigUtil.LoadRequiredResources(node));
        }
    }
}
