using System.Collections.Generic;

namespace BackgroundResourceProcessing.Modules
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

        protected override ConverterBehaviour GetConverterBehaviour()
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
