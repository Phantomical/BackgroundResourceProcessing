using System.Collections.Generic;
using UnifiedBackgroundProcessing.Behaviour;

namespace UnifiedBackgroundProcessing.Modules
{
    public abstract class BackgroundConsumer : PartModule
    {
        private int priority = 0;

        /// <summary>
        /// The priority with which this consumer will consume produced resources.
        /// </summary>
        ///
        /// <remarks>
        ///   This is used to determine which parts will continue to be
        ///   supplied with resources when there are not enough being produced
        ///   to satisfy all consumers/converters. Higher priorities will
        ///   consume resources first. The default is 0, and generally you can
        ///   leave the priority at that.
        /// </remarks>
        public int Priority => priority;

        /// <summary>
        /// Get the <see cref="ConsumerBehaviour"/> that describes the resources
        /// consumed by this part.
        /// </summary>
        public abstract ConsumerBehaviour GetBehaviour();

        public override void OnLoad(ConfigNode node)
        {
            base.OnLoad(node);
            node.TryGetValue("priority", ref priority);
        }
    }

    /// <summary>
    /// A part which continuously consumes resources at a constant rate.
    /// </summary>
    ///
    /// <remarks>
    /// To declare the resources that should be produced, add <c>INPUT_RESOURCE</c>
    /// config nodes naming the resource with its <c>Ratio</c> being the rate at
    /// which the resource should be consumed.
    /// </remarks>
    public class BackgroundConstantConsumeModule : BackgroundConsumer
    {
        private List<ResourceRatio> inputs = [];

        public override ConsumerBehaviour GetBehaviour()
        {
            return new ConstantConsumer(inputs);
        }

        public override void OnLoad(ConfigNode node)
        {
            base.OnLoad(node);
            inputs = new(ConfigUtil.LoadInputResources(node));
        }
    }
}
