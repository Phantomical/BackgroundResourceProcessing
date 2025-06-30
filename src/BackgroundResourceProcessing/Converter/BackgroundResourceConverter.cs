using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BackgroundResourceProcessing.Collections;
using BackgroundResourceProcessing.Utils;

namespace BackgroundResourceProcessing.Converter
{
    /// <summary>
    /// A background converter module for types implementing <see cref="BaseConverter"/>.
    /// </summary>
    public abstract class BackgroundResourceConverter<T> : BackgroundConverter<T>
        where T : BaseConverter
    {
        private const BindingFlags Flags =
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

        private static readonly MethodInfo PrepareRecipeMethod = typeof(BaseConverter).GetMethod(
            "PrepareRecipe",
            Flags
        );

        private static readonly FieldInfo PreCalculateEfficiencyField =
            typeof(BaseConverter).GetField("_preCalculateEfficiency", Flags);

        protected static readonly FieldInfo LastUpdateTimeField = typeof(BaseConverter).GetField(
            "lastUpdateTime",
            Flags
        );

        /// <summary>
        /// Ignore the listed elements on the converter and instead use the
        /// recipe as it is returned from <c>PrepareRecipe</c>.
        /// </summary>
        [KSPField]
        public bool UsePreparedRecipe = false;

        /// <summary>
        /// Use the current efficiency bonus of the converter instead of trying
        /// to calculate an optimal efficiency bonus.
        /// </summary>
        [KSPField]
        public bool UseCurrentEfficiency = false;

        /// <summary>
        /// Override the maximum thermal efficiency
        /// </summary>
        [KSPField]
        public double? OverrideMaxThermalEfficiency = null;

        /// <summary>
        /// Indicate whether the recipe should use the efficiency bonus as
        /// calculated from the converter.
        ///
        /// Defaults to false if <c>UsePreparedRecipe</c> is true, and true
        /// otherwise.
        /// </summary>
        private ModuleFilter? UseEfficiencyBonus = null;

        private List<ConverterMultiplier> multipliers;

        public override ModuleBehaviour GetBehaviour(T module)
        {
            if (module == null)
                return null;
            if (!IsConverterEnabled(module))
                return null;

            IEnumerable<ResourceRatio> inputs;
            IEnumerable<ResourceRatio> outputs;
            IEnumerable<ResourceConstraint> required;

            double fillAmount;
            double takeAmount;

            bool useEfficiencyBonus;

            if (UsePreparedRecipe)
            {
                var recipe = InvokePrepareRecipe(module, 1.0);

                inputs = recipe.Inputs;
                outputs = recipe.Outputs;
                required = recipe.Requirements.Select(req => new ResourceConstraint(req));

                fillAmount = recipe.FillAmount;
                takeAmount = recipe.TakeAmount;

                useEfficiencyBonus =
                    UseEfficiencyBonus?.Invoke(module)
                    ?? (bool)PreCalculateEfficiencyField.GetValue(module);
            }
            else
            {
                inputs = module.inputList;
                outputs = module.outputList;
                required = module.reqList.Select(req => new ResourceConstraint(req));

                fillAmount = module.FillAmount;
                takeAmount = module.TakeAmount;

                useEfficiencyBonus = UseEfficiencyBonus?.Invoke(module) ?? true;

                if (ConvertByMass(module))
                {
                    inputs = ConvertRecipeToUnits(inputs);
                    outputs = ConvertRecipeToUnits(outputs);
                    required = ConvertConstraintToUnits(required);
                }
            }

            var additional = GetAdditionalRecipe(module);
            if (additional.Inputs != null && additional.Inputs.Count != 0)
                inputs = inputs.Concat(additional.Inputs);
            if (additional.Outputs != null && additional.Outputs.Count != 0)
                outputs = outputs.Concat(additional.Outputs);
            if (additional.Requirements != null && additional.Requirements.Count != 0)
                required = required.Concat(additional.Requirements);

            var bonus = 1.0;
            if (useEfficiencyBonus)
            {
                if (UseCurrentEfficiency)
                    bonus *= module.GetEfficiencyMultiplier();
                else
                    bonus *= GetOptimalEfficiencyBonus(module);
            }

            foreach (var multiplier in multipliers)
                bonus *= multiplier.Evaluate(module);

            if (bonus != 1.0)
            {
                inputs = inputs.Select(res => res.WithMultiplier((double)bonus));
                outputs = outputs.Select(res => res.WithMultiplier((double)bonus));
            }

            var inputList = inputs.ToList();
            var outputList = outputs.ToList();
            var vessel = module.vessel;

            if (fillAmount < 1.0)
            {
                if (fillAmount <= 0.0)
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
                            Amount = maxAmount * fillAmount,
                            Constraint = Constraint.AT_MOST,
                        };
                    })
                );
            }

            if (takeAmount < 1.0)
            {
                if (takeAmount <= 0.0)
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
                            Amount = maxAmount * (1.0 - takeAmount),
                            Constraint = Constraint.AT_LEAST,
                        };
                    })
                );
            }

            return new(new ConstantConverter(inputList, outputList, required.ToList()));
        }

        public override void OnRestore(T module, ResourceConverter converter)
        {
            LastUpdateTimeField.SetValue(module, Planetarium.GetUniversalTime());
        }

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
        ///   again until the next time <c>GetConverterBehaviour</c> is called.
        /// </para>
        /// </remarks>
        protected virtual bool IsConverterEnabled(T converter)
        {
            return converter.ModuleIsActive();
        }

        /// <summary>
        /// Get a set of additional inputs, outputs, and required resources
        /// to add in addition to those listed on the base converter.
        /// </summary>
        ///
        /// <returns>A conversion recipe, or null if there are no changes to be made</returns>
        protected virtual ConverterResources GetAdditionalRecipe(T module)
        {
            return default;
        }

        /// <summary>
        /// Get an additional efficiency multiplier to apply on top of the
        /// default optimal efficiency bonus.
        /// </summary>
        /// <returns></returns>
        protected virtual double GetOptimalEfficiencyBonus(T converter)
        {
            double bonus = 1.0;

            foreach (var (_, modifier) in converter.EfficiencyModifiers)
                bonus *= modifier;

            bonus *= converter.GetCrewEfficiencyBonus();
            bonus *= converter.EfficiencyBonus;
            bonus *= OverrideMaxThermalEfficiency ?? GetMaxThermalEfficiencyBonus(converter);

            return bonus;
        }

        /// <summary>
        /// Indicates whether the resource rates are specified in units of tons.
        /// </summary>
        protected virtual bool ConvertByMass(T converter)
        {
            bool baseline = false;
            if (converter is ModuleResourceConverter rc)
                baseline = rc.ConvertByMass;
            return baseline;
        }

        private double GetMaxThermalEfficiencyBonus(T converter)
        {
            converter.ThermalEfficiency.FindMinMaxValue(out var _, out var maxThermalEfficiency);
            return maxThermalEfficiency;
        }

        private ConversionRecipe InvokePrepareRecipe(T module, double dt)
        {
            return (ConversionRecipe)PrepareRecipeMethod.Invoke(module, [dt]);
        }

        protected override void OnLoad(ConfigNode node)
        {
            base.OnLoad(node);

            string useEfficiencyBonus = null;
            if (node.TryGetValue("UseEfficiencyBonus", ref useEfficiencyBonus))
                UseEfficiencyBonus = ModuleFilter.Compile(useEfficiencyBonus, node);

            var target = GetTargetType(node);
            multipliers = ConverterMultiplier.LoadAll(target, node);
        }

        public static IEnumerable<ResourceRatio> ConvertRecipeToUnits(
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

        public static IEnumerable<ResourceConstraint> ConvertConstraintToUnits(
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

        public class RecipeOverride
        {
            public string name;

            /// <summary>
            /// Limits this converter to only be enabled when all its outputs have
            /// at most this fraction of their total resources filled.
            /// </summary>
            public double? FillAmount = null;

            /// <summary>
            /// Limits this converter to only be enabled when all its inputs have
            /// at least this fraction of their total resources filled.
            /// </summary>
            public double? TakeAmount = null;

            /// <summary>
            /// Specify an efficiency bonus to be used instead of the one on the
            /// referenced <see cref="BaseConverter"/> module.
            /// </summary>
            public double? OverrideEfficiencyBonus = null;

            /// <summary>
            /// Specify the maximum thermal efficiency multiplier to be used
            /// instead of the one on the referenced <see cref="BaseConverter"/>
            /// module.
            /// </summary>
            public double? OverrideThermalEfficiency = null;

            /// <summary>
            /// Specify the default efficency to use for this recipe.
            /// </summary>
            public double BaseEfficiency = 1.0;

            /// <summary>
            /// The input, output, and required units are provided in terms of mass
            /// and will be converted to KSP units on start.
            /// </summary>
            public bool? ConvertByMass = null;

            public List<ResourceRatio> Inputs;
            public List<ResourceRatio> Outputs;
            public List<ResourceConstraint> Requirements;

            public static RecipeOverride Load(ConfigNode node)
            {
                RecipeOverride recipe = new();

                if (!node.TryGetValue("name", ref recipe.name))
                {
                    LogUtil.Error("RECIPE_OVERRIDE config node does not have a name");
                    return null;
                }

                double fillAmount = 0;
                double takeAmount = 0;
                double efficiencyBonus = 0;
                double thermalEfficiency = 0;

                if (node.TryGetValue("FillAmount", ref fillAmount))
                    recipe.FillAmount = fillAmount;
                if (node.TryGetValue("TakeAmount", ref takeAmount))
                    recipe.TakeAmount = takeAmount;
                if (node.TryGetValue("OverrideEfficiencyBonus", ref efficiencyBonus))
                    recipe.OverrideEfficiencyBonus = efficiencyBonus;
                if (node.TryGetValue("OverrideThermalEfficiency", ref thermalEfficiency))
                    recipe.OverrideThermalEfficiency = thermalEfficiency;
                node.TryGetValue("BaseEfficiency", ref recipe.BaseEfficiency);

                recipe.Inputs = [.. ConfigUtil.LoadInputResources(node)];
                recipe.Outputs = [.. ConfigUtil.LoadOutputResources(node)];
                recipe.Requirements = [.. ConfigUtil.LoadRequiredResources(node)];
                return recipe;
            }
        }
    }

    /// <summary>
    /// A background converter module for types implementing <see cref="BaseConverter"/>.
    ///
    /// Don't inherit from this class, use <c>BackgroundTypedResourceConverter</c> instead.
    /// </summary>
    public class BackgroundResourceConverter : BackgroundResourceConverter<BaseConverter> { }
}
