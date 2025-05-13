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

        public override ConverterBehaviour GetBehaviour()
        {
            return new ConstantProducer(outputs);
        }

        public override void OnLoad(ConfigNode node)
        {
            base.OnLoad(node);
            outputs = [.. ConfigUtil.LoadOutputResources(node)];
        }
    }
}
