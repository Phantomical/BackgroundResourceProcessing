using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Smooth.Collections;
using UnityEngine;

namespace UnifiedBackgroundProcessing.Modules
{
    public abstract class BackgroundConverter : PartModule
    {
        private int priority = 0;

        /// <summary>
        /// The priority with which this converter will consume produced resources.
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
        /// Get the <see cref="ConverterBehaviour"/> that describes the
        /// resources consumed, produced, and required by this part.
        /// </summary>
        public abstract ConverterBehaviour GetBehaviour();

        public override void OnLoad(ConfigNode node)
        {
            base.OnLoad(node);
            node.TryGetValue("priority", ref priority);
        }
    }

    /// <summary>
    /// A simple converter that always converts resources at a constant rate.
    /// </summary>
    public class BackgroundConstantConverterModule : BackgroundConverter
    {
        public List<ResourceRatio> inputs = [];
        public List<ResourceRatio> outputs = [];
        public List<ResourceRatio> required = [];

        public override ConverterBehaviour GetBehaviour()
        {
            return new ConstantConverter(inputs, outputs, required);
        }

        public override void OnLoad(ConfigNode node)
        {
            base.OnLoad(node);
            inputs = new(ConfigUtil.LoadInputResources(node));
            outputs = new(ConfigUtil.LoadOutputResources(node));
            required = new(ConfigUtil.LoadRequiredResources(node));
        }

        public override void OnCopy(PartModule fromModule)
        {
            base.OnCopy(fromModule);

            if (fromModule as BackgroundConstantConverterModule) { }
        }
    }

    /// <summary>
    /// A constant converter that multiplies its rate by the efficiency multiplier
    /// of a converter module on the same part.
    /// </summary>
    public class BackgroundEfficiencyConstantConverterModule : BackgroundConstantConverterModule
    {
        // Attempting to find the base converter module is expensive and
        // possibly unreliable. This field saves the persistentId for the
        // part module so we can use that to perform lookups in the future.
        [KSPField(isPersistant = true)]
        uint? converterId = null;

        uint? converterIndex = null;
        string converterName = null;

        BaseConverter converter = null;

        public override ConverterBehaviour GetBehaviour()
        {
            if (converter == null)
                return null;
            if (!converter.IsActivated)
                return null;

            var behaviour = new ConstantConverter(inputs, outputs, required);
            var efficiency = converter.GetEfficiencyMultiplier();

            for (var i = 0; i < behaviour.inputs.Count; ++i)
            {
                var input = behaviour.inputs[i];
                input.Ratio *= efficiency;
                behaviour.inputs[i] = input;
            }

            for (var i = 0; i < behaviour.outputs.Count; ++i)
            {
                var output = behaviour.outputs[i];
                output.Ratio *= efficiency;
                behaviour.outputs[i] = output;
            }

            return behaviour;
        }

        public override void OnStart(StartState state)
        {
            base.OnStart(state);
            converter = FindMatchingConverter();
        }

        public override void OnLoad(ConfigNode node)
        {
            base.OnLoad(node);
            node.TryGetValue("ConverterName", ref converterName);

            uint converterIndex = 0;
            if (node.TryGetValue("ConverterIndex", ref converterIndex))
                this.converterIndex = converterIndex;
        }

        private BaseConverter FindMatchingConverter()
        {
            var converters = part.FindModulesImplementing<BaseConverter>();
            if (converterId != null)
            {
                var converterId = (uint)this.converterId;
                var converter = converters.FirstOrDefault(converter =>
                    converter.GetPersistentId() == converterId
                );

                if (converter)
                    return converter;
            }

            if (converterIndex != null)
            {
                var index = (uint)converterIndex;
                if (index > converters.Count)
                {
                    Debug.LogError("ConverterIndex was out of bounds on part " + this);
                    return null;
                }

                return converters[(int)index];
            }

            if (converterName != null)
            {
                var filtered = converters
                    .Where(converter => converter.ConverterName == converterName)
                    .ToArray();

                if (filtered.Length == 1)
                {
                    return filtered[0];
                }

                var partName = part.partName;
                if (filtered.Length > 1)
                {
                    Debug.LogError(
                        $"ConverterName '{converterName}' matched multiple modules on part {partName}"
                    );
                }
                else
                {
                    Debug.LogError(
                        $"ConverterName '{converterName}' matched 0 modules on part {partName}"
                    );
                }

                return null;
            }

            Debug.LogWarning(
                string.Concat(
                    "Neither ConvertIndex nor ConvertName are specified for ",
                    $"BackgroundEfficiencyConstantConverterModule on part {part.partName}. ",
                    "Efficiency multipliers will not be taken into account on this part."
                )
            );

            return null;
        }
    }
}
