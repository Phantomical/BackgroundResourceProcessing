using System.Collections.Generic;

namespace BackgroundResourceProcessing.Modules
{
    /// <summary>
    /// A simple converter that always converts resources at a constant rate.
    /// </summary>
    public class ModuleBackgroundConstantConverter : BackgroundConverter
    {
        public List<ResourceRatio> inputs = [];
        public List<ResourceRatio> outputs = [];
        public List<ResourceConstraint> required = [];

        protected override ConverterBehaviour GetConverterBehaviour()
        {
            return new ConstantConverter(inputs, outputs, required);
        }

        public override void OnLoad(ConfigNode node)
        {
            base.OnLoad(node);
            inputs.AddRange(ConfigUtil.LoadInputResources(node));
            outputs.AddRange(ConfigUtil.LoadOutputResources(node));
            required.AddRange(ConfigUtil.LoadRequiredResources(node));
        }
    }
}
