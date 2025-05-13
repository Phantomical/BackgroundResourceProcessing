using System.Collections.Generic;
using System.Linq;
using UnifiedBackgroundProcessing.Utils;

namespace UnifiedBackgroundProcessing.Modules
{
    /// <summary>
    /// A simple converter that always converts resources at a constant rate.
    /// </summary>
    public class ModuleBackgroundConstantConverter : BackgroundConverter
    {
        public List<ResourceRatio> inputs = [];
        public List<ResourceRatio> outputs = [];
        public List<ResourceRatio> required = [];

        public override ConverterBehaviour GetBehaviour()
        {
            return new ConstantConverter(inputs, outputs, required);
        }

        public override void OnLoad(ConfigNode node)
        {
            base.OnLoad(node);
            inputs = new(ConfigUtil.LoadInputResources(node));
            outputs = new(ConfigUtil.LoadOutputResources(node));
            required = new(ConfigUtil.LoadRequiredResources(node));
        }
    }
}
