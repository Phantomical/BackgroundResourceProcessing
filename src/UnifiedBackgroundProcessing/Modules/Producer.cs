using System.Collections.Generic;
using KSP;
using UnifiedBackgroundProcessing.Behaviour;

namespace UnifiedBackgroundProcessing.Modules
{
    public abstract class BackgroundProducer : PartModule
    {
        /// <summary>
        /// Get the <see cref="ProducerBehaviour"/> that describes the resources
        /// produced by this part.
        /// </summary>
        ///
        /// <remarks>
        /// This returned behaviour should generally reflect the "steady-state"
        /// production rate of this part.
        /// </remarks>
        public abstract ProducerBehaviour GetBehaviour();
    }

    /// <summary>
    /// A part which continuously produces resources at a constant rate.
    /// </summary>
    ///
    /// <remarks>
    /// To declare the resources that should be produced, add
    /// <c>OUTPUT_RESOURCE</c> config nodes naming the resource and with
    /// <c>Ratio</c> being the rate at which that resource should be produced.
    /// </remarks>
    public class BackgroundConstantProducerModule : BackgroundProducer
    {
        private List<ResourceRatio> outputs = [];

        public override ProducerBehaviour GetBehaviour()
        {
            return new ConstantProducer(outputs);
        }

        public override void OnLoad(ConfigNode node)
        {
            base.OnLoad(node);
            outputs = new(ConfigUtil.LoadOutputResources(node));
        }
    }
}
