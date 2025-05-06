using System.Collections.Generic;
using System.Diagnostics;

namespace UnifiedBackgroundProcessing.Behaviour
{
    public abstract class ConverterBehaviour(int priority = 0) : BaseBehaviour
    {
        public struct ConverterResources()
        {
            public List<ResourceRatio> Inputs = [];
            public List<ResourceRatio> Outputs = [];

            /// <summary>
            /// A list of resources that are required in order for this converter
            /// to work.
            /// </summary>
            ///
            /// <remarks>
            /// Note that the <c>Ratio</c> field for these is actually the
            /// minimum amount of resources that must be available to the
            /// converter in order for it to run.
            /// </remarks>
            public List<ResourceRatio> Requirements = [];
        }

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
        /// Get the list of input, output, and required resources and their
        /// rates.
        /// </summary>
        ///
        /// <param name="state">State information about the vessel.</param>
        /// <returns>A <c>ConverterResources</c> with the relevant resources</returns>
        ///
        /// <remarks>
        /// This should be the "steady-state" rate at which this converter will
        /// consume or produce resources.
        /// </remarks>
        public abstract ConverterResources GetResources(VesselState state);
    }

    /// <summary>
    /// A converter that converts a set of resources into another set of
    /// resources at a constant rate.
    /// </summary>
    [Behaviour(typeof(ConstantConverter))]
    public class ConstantConverter : ConverterBehaviour
    {
        public List<ResourceRatio> inputs = [];
        public List<ResourceRatio> outputs = [];
        public List<ResourceRatio> required = [];

        public ConstantConverter() { }

        public ConstantConverter(List<ResourceRatio> inputs, List<ResourceRatio> outputs)
        {
            this.inputs = inputs;
            this.outputs = outputs;
        }

        public ConstantConverter(
            List<ResourceRatio> inputs,
            List<ResourceRatio> outputs,
            List<ResourceRatio> required
        )
        {
            this.inputs = inputs;
            this.outputs = outputs;
            this.required = required;
        }

        public override ConverterResources GetResources(VesselState state)
        {
            ConverterResources resources = default;
            resources.Inputs = inputs;
            resources.Outputs = outputs;
            resources.Requirements = required;
            return resources;
        }

        public override void Load(ConfigNode node)
        {
            base.Load(node);
            inputs = new(ConfigUtil.LoadInputResources(node));
            outputs = new(ConfigUtil.LoadOutputResources(node));
            required = new(ConfigUtil.LoadRequiredResources(node));
        }

        public override void Save(ConfigNode node)
        {
            base.Save(node);
            ConfigUtil.SaveInputResources(node, inputs);
            ConfigUtil.SaveOutputResources(node, outputs);
            ConfigUtil.SaveRequiredResources(node, required);
        }
    }
}
