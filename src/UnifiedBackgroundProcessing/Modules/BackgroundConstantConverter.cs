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

        [KSPField]
        public double multiplier = 1.0;

        public override ConverterBehaviour GetBehaviour()
        {
            if (multiplier == 1.0)
                return new ConstantConverter(inputs, outputs, required);
            if (multiplier == 0.0)
                return null;

            return new ConstantConverter(
                [.. inputs.Select(res => res.WithMultiplier(multiplier))],
                [.. outputs.Select(res => res.WithMultiplier(multiplier))],
                required
            );
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
