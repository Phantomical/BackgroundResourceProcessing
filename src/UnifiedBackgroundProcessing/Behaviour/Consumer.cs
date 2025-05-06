using System.Collections.Generic;

namespace UnifiedBackgroundProcessing.Behaviour
{
    public abstract class ConsumerBehaviour(int priority = 0) : BaseBehaviour
    {
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
        public int Priority = priority;

        /// <summary>
        /// Get the current list of resources that are being consumed by this
        /// part and the rates at which they are being consumed.
        /// </summary>
        ///
        /// <param name="state">State information about the vessel.</param>
        /// <returns>
        ///   A list of resources and the rates at which they are being consumed.
        /// </returns>
        ///
        /// <remarks>
        /// This should be the "steady-state" rate at which this consumer will
        /// consume resources. It should assume that the resources are available.
        /// Insufficient stored resources will be handled by the background
        /// processing solver.
        /// </remarks>
        public abstract List<ResourceRatio> GetInputs(VesselState state);

        public override void Load(ConfigNode node)
        {
            base.Load(node);
            if (!node.TryGetValue("priority", ref Priority))
                Priority = 0;
        }

        public override void Save(ConfigNode node)
        {
            base.Load(node);
            node.AddValue("priority", Priority);
        }
    }

    /// <summary>
    /// A consumer that consumes resources at a fixed rate.
    /// </summary>
    [Behaviour(typeof(ConstantConsumer))]
    public class ConstantConsumer : ConsumerBehaviour
    {
        private List<ResourceRatio> inputs = [];

        public ConstantConsumer() { }

        public ConstantConsumer(List<ResourceRatio> inputs)
        {
            this.inputs = inputs;
        }

        public override List<ResourceRatio> GetInputs(VesselState state)
        {
            return inputs;
        }

        public override void Load(ConfigNode node)
        {
            base.Load(node);
            inputs = new(ConfigUtil.LoadInputResources(node));
        }

        public override void Save(ConfigNode node)
        {
            base.Save(node);
            ConfigUtil.SaveInputResources(node, inputs);
        }
    }
}
