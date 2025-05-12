using System.Collections.Generic;
using System.Linq;
using UnifiedBackgroundProcessing.Utils;

namespace UnifiedBackgroundProcessing.Modules
{
    /// <summary>
    /// A part which continuously produces resources at a constant rate.
    /// </summary>
    ///
    /// <remarks>
    /// To declare the resources that should be produced, add
    /// <c>OUTPUT_RESOURCE</c> config nodes naming the resource and with
    /// <c>Ratio</c> being the rate at which that resource should be produced.
    /// </remarks>
    public class ModuleBackgroundConstantProducer : BackgroundConverter
    {
        public List<ResourceRatio> outputs = [];

        /// <summary>
        /// A multiplier on the number of resources consumed from the list.
        /// </summary>
        [KSPField]
        public double multiplier = 1.0;

        public override ConverterBehaviour GetBehaviour()
        {
            if (multiplier == 1.0)
                return new ConstantProducer(outputs);
            if (multiplier == 0.0)
                return null;
            return new ConstantProducer([.. outputs.Select(res => res.WithMultiplier(multiplier))]);
        }

        public override void OnLoad(ConfigNode node)
        {
            base.OnLoad(node);
            outputs = [.. ConfigUtil.LoadOutputResources(node)];
        }
    }
}
