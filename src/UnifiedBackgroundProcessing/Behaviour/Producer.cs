using System;
using System.Collections.Generic;
using UnifiedBackgroundProcessing.Behaviour;
using UnityEngine;

namespace UnifiedBackgroundProcessing.Behaviour
{
    public abstract class ProducerBehaviour : BaseBehaviour
    {
        /// <summary>
        /// Get the list of resources that are being produced by this part and
        /// the rates at which they are being produced.
        /// </summary>
        ///
        /// <param name="state">State information about the vessel.</param>
        /// <returns>
        ///   A list of resources and the rates at which they are being produced.
        /// </returns>
        ///
        /// <remarks>
        /// <para>
        ///   This should be the "steady-state" rate at which this producer will
        ///   produce more resources. It should assume that there is room to
        ///   store the resources - resources filling up will be handled by
        ///   the background processing solver.
        /// </para>
        /// </remarks>
        public abstract List<ResourceRatio> GetOutputs(VesselState state);
    }

    /// <summary>
    /// A producer that produces resources at a fixed rate.
    /// </summary>
    [Behaviour(typeof(ConstantProducer))]
    public class ConstantProducer : ProducerBehaviour
    {
        private List<ResourceRatio> outputs = [];

        public ConstantProducer() { }

        public ConstantProducer(List<ResourceRatio> outputs)
        {
            this.outputs = outputs;
        }

        public override List<ResourceRatio> GetOutputs(VesselState state)
        {
            return outputs;
        }

        public override void Load(ConfigNode node)
        {
            base.Load(node);
            outputs = new(ConfigUtil.LoadOutputResources(node));
        }

        public override void Save(ConfigNode node)
        {
            base.Save(node);
            ConfigUtil.SaveOutputResources(node, outputs);
        }
    }
}
