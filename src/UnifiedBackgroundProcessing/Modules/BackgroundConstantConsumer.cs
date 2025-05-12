using System.Collections.Generic;
using System.Linq;
using UnifiedBackgroundProcessing.Utils;

namespace UnifiedBackgroundProcessing.Modules
{
    /// <summary>
    /// A part which continuously consumes resources at a constant rate.
    /// </summary>
    ///
    /// <remarks>
    /// To declare the resources that should be produced, add <c>INPUT_RESOURCE</c>
    /// config nodes naming the resource with its <c>Ratio</c> being the rate at
    /// which the resource should be consumed.
    /// </remarks>
    public class ModuleBackgroundConstantConsumer : BackgroundConverter
    {
        /// <summary>
        /// The list of resources that this converter should consume.
        /// </summary>
        public List<ResourceRatio> inputs = [];

        /// <summary>
        /// A multiplier on the number of resources consumed from the list.
        /// </summary>
        [KSPField]
        public double multiplier = 1.0;

        public override ConverterBehaviour GetBehaviour()
        {
            if (multiplier == 1.0)
                return new ConstantConsumer(inputs);
            if (multiplier == 0.0)
                return null;

            return new ConstantConsumer([.. inputs.Select(res => res.WithMultiplier(multiplier))]);
        }

        public override void OnLoad(ConfigNode node)
        {
            base.OnLoad(node);
            inputs = [.. ConfigUtil.LoadInputResources(node)];
        }
    }
}
