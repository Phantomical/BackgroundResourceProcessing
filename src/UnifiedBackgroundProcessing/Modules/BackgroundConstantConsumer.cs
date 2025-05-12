using System.Collections.Generic;

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
        private List<ResourceRatio> inputs = [];

        public override ConverterBehaviour GetBehaviour()
        {
            return new ConstantConsumer(inputs);
        }

        public override void OnLoad(ConfigNode node)
        {
            base.OnLoad(node);
            inputs = [.. ConfigUtil.LoadInputResources(node)];
        }
    }
}
