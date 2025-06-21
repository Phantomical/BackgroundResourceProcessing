using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BackgroundResourceProcessing.Collections;
using BackgroundResourceProcessing.Utils;

namespace BackgroundResourceProcessing.Modules
{
    /// <summary>
    /// A background converter that reads its recipe and efficiency multiplier
    /// from a linked <see cref="BaseConverter"/>.
    /// </summary>
    public class ModuleBackgroundResourceConverter
        : BackgroundLinkedConverter<BaseConverter>,
            IBackgroundVesselRestoreHandler
    {
        protected static readonly FieldInfo LastUpdateTimeField = typeof(BaseConverter).GetField(
            "lastUpdateTime",
            BindingFlags.Instance | BindingFlags.NonPublic
        );

        /// <summary>
        /// Limits this converter to only be enabled when all its outputs have
        /// at most this fraction of their total resources filled.
        /// </summary>
        [KSPField]
        public double FillAmount = 1.0;

        /// <summary>
        /// Limits this converter to only be enabled when all its inputs have
        /// at least this fraction of their total resources filled.
        /// </summary>
        [KSPField]
        public double TakeAmount = 1.0;

        /// <summary>
        /// Specify an efficiency bonus to be used instead of the one on the
        /// referenced <see cref="BaseConverter"/> module.
        /// </summary>
        [KSPField]
        public double? OverrideEfficiencyBonus = null;

        /// <summary>
        /// Specify the maximum thermal efficiency multiplier to be used
        /// instead of the one on the referenced <see cref="BaseConverter"/>
        /// module.
        /// </summary>
        [KSPField]
        public double? OverrideThermalEfficiency = null;

        /// <summary>
        /// An additional efficiency multiplier to be used on top of the
        /// existing efficiency multipliers.
        /// </summary>
        [KSPField]
        public double EfficiencyMultiplier = 1.0;

        /// <summary>
        /// The input, output, and required units are provided in terms of mass
        /// and will be converted to KSP units on start.
        /// </summary>
        [KSPField]
        public bool ConvertByMass = false;

        public BaseConverter Converter => Module;

        /// <summary>
        /// Whether this converter is enabled for background processing.
        /// </summary>
        ///
        /// <remarks>
        /// <para>
        ///   For <c>ModuleResourceConverter</c> <c>Converter.ModuleIsActive</c>
        ///   is accurate and will work as expected. For other modules (e.g.
        ///   drills) you may find that the module will deactivate itself in
        ///   cases where we really want the background converter to be running.
        ///   In those cases you can override this to customize when the
        ///   background converter is considered to be enabled.
        /// </para>
        ///
        /// <para>
        ///   Note that this is only checked when the behaviour is being
        ///   created (i.e. at vessel unload time) and will not be called
        ///   again until the next <c>GetConverterBehaviour</c> is called.
        /// </para>
        /// </remarks>
        protected virtual bool IsConverterEnabled()
        {
            return Converter.ModuleIsActive();
        }

        /// <summary>
        /// Get a set of additional inputs, outputs, and required resources
        /// to add in addition to those listed in the module definition.
        /// </summary>
        ///
        /// <returns>A conversion recipe, or null if there are no changes to be made</returns>
        protected virtual ConverterResources GetAdditionalRecipe()
        {
            return default;
        }

        /// <summary>
        /// Get an additional efficiency multiplier to apply on top of the
        /// default optimal efficiency bonus.
        /// </summary>
        /// <returns></returns>
        protected virtual double GetOptimalEfficiencyBonus()
        {
            double bonus = EfficiencyMultiplier;

            foreach (var (_, modifier) in Converter.EfficiencyModifiers)
                bonus *= modifier;

            bonus *= Converter.GetCrewEfficiencyBonus();
            bonus *= OverrideEfficiencyBonus ?? Converter.EfficiencyBonus;
            bonus *= OverrideThermalEfficiency ?? GetMaxThermalEfficiencyBonus();

            return bonus;
        }

        protected override ConverterBehaviour GetConverterBehaviour()
        {
            if (Converter == null)
                return null;
            if (!IsConverterEnabled())
                return null;

            IEnumerable<ResourceRatio> inputs = this.inputs;
            IEnumerable<ResourceRatio> outputs = this.outputs;
            IEnumerable<ResourceConstraint> required = this.required;

            var additional = GetAdditionalRecipe();
            if (additional.Inputs != null && additional.Inputs.Count != 0)
                inputs = inputs.Concat(additional.Inputs);
            if (additional.Outputs != null && additional.Outputs.Count != 0)
                outputs = outputs.Concat(additional.Outputs);
            if (additional.Requirements != null && additional.Requirements.Count != 0)
                required = required.Concat(additional.Requirements);

            if (ConvertByMass)
            {
                inputs = ConvertRecipeToUnits(inputs);
                outputs = ConvertRecipeToUnits(outputs);
                required = ConvertConstraintToUnits(required);
            }

            var multiplier = GetOptimalEfficiencyBonus();
            if (multiplier != 1.0)
            {
                inputs = inputs.Select(res => res.WithMultiplier(multiplier));
                outputs = outputs.Select(res => res.WithMultiplier(multiplier));
            }

            var inputList = inputs.ToList();
            var outputList = outputs.ToList();

            if (FillAmount < 1.0)
            {
                if (FillAmount <= 0.0)
                    return null;

                required = required.Concat(
                    outputList.Select(output =>
                    {
                        vessel.resourcePartSet.GetConnectedResourceTotals(
                            output.ResourceName.GetHashCode(),
                            out double _,
                            out double maxAmount,
                            false
                        );

                        return new ResourceConstraint()
                        {
                            ResourceName = output.ResourceName,
                            Amount = maxAmount * FillAmount,
                            Constraint = Constraint.AT_MOST,
                        };
                    })
                );
            }

            if (TakeAmount < 1.0)
            {
                if (TakeAmount <= 0.0)
                    return null;

                required = required.Concat(
                    inputList.Select(input =>
                    {
                        vessel.resourcePartSet.GetConnectedResourceTotals(
                            input.ResourceName.GetHashCode(),
                            out double _,
                            out double maxAmount,
                            true
                        );

                        return new ResourceConstraint()
                        {
                            ResourceName = input.ResourceName,
                            Amount = maxAmount * (1.0 - TakeAmount),
                            Constraint = Constraint.AT_LEAST,
                        };
                    })
                );
            }

            return new ConstantConverter(inputList, outputList, required.ToList());
        }

        public virtual void OnVesselRestore()
        {
            if (!Converter)
                return;

            LastUpdateTimeField.SetValue(Converter, Planetarium.GetUniversalTime());
        }

        private double GetMaxThermalEfficiencyBonus()
        {
            Converter.ThermalEfficiency.FindMinMaxValue(out var _, out var maxThermalEfficiency);
            return maxThermalEfficiency;
        }

        protected static IEnumerable<ResourceRatio> ConvertRecipeToUnits(
            IEnumerable<ResourceRatio> resources
        )
        {
            return resources.Select(resource =>
            {
                var definition = PartResourceLibrary.Instance.resourceDefinitions[
                    resource.ResourceName
                ];
                if (definition.density > 1e-9)
                    resource.Ratio /= definition.density;

                return resource;
            });
        }

        protected static IEnumerable<ResourceConstraint> ConvertConstraintToUnits(
            IEnumerable<ResourceConstraint> resources
        )
        {
            return resources.Select(resource =>
            {
                var definition = PartResourceLibrary.Instance.resourceDefinitions[
                    resource.ResourceName
                ];
                if (definition.density > 1e-9)
                    resource.Amount /= definition.density;

                return resource;
            });
        }
    }
}
