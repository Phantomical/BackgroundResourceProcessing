using System.Collections.Generic;

namespace BackgroundResourceProcessing.Modules
{
    /// <summary>
    /// A simple converter that always converts resources at a constant rate.
    /// </summary>
    public class ModuleBackgroundConverter : BackgroundConverterBase
    {
        public List<ResourceRatio> inputs = [];
        public List<ResourceRatio> outputs = [];
        public List<ResourceConstraint> required = [];

        protected override List<ConverterBehaviour> GetConverterBehaviours()
        {
            return [new ConstantConverter(inputs, outputs, required)];
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
