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
        private static MethodInfo PrepareRecipeMethod = typeof(BaseConverter).GetMethod(
            "PrepareRecipe",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public
        );

        protected static readonly FieldInfo LastUpdateTimeField = typeof(BaseConverter).GetField(
            "lastUpdateTime",
            BindingFlags.Instance | BindingFlags.NonPublic
        );

        private readonly Dictionary<string, RecipeOverride> overrides = [];

        /// <summary>
        /// Ignore the listed elements on the converter and instead use the
        /// recipe as it is returned from <c>PrepareRecipe</c>.
        ///
        /// The recipe will still be overriden by recipe overrides, if specified.
        /// </summary>
        [KSPField]
        public bool UseReturnedRecipe = false;

        /// <summary>
        /// Instruct the configuration to not apply any efficiency bonuses.
        /// </summary>
        [KSPField]
        public bool IgnoreBonus = false;

        private FieldExtractor<double> multiplierExtractor = null;

        public override ModuleBehaviour GetBehaviour(T module)
        {
            if (module == null)
                return null;
            if (!IsConverterEnabled(module))
                return null;

            if (!overrides.TryGetValue(module.ConverterName, out var recipe))
                recipe = null;

            IEnumerable<ResourceRatio> inputs;
            IEnumerable<ResourceRatio> outputs;
            IEnumerable<ResourceConstraint> required;

            double fillAmount = module.FillAmount;
            double takeAmount = module.TakeAmount;

            if (recipe != null)
            {
                inputs = recipe.Inputs;
                outputs = recipe.Outputs;
                required = recipe.Requirements;

                fillAmount = recipe.FillAmount ?? fillAmount;
                takeAmount = recipe.TakeAmount ?? takeAmount;

                if (recipe.ConvertByMass ?? false)
                {
                    inputs = ConvertRecipeToUnits(inputs);
                    outputs = ConvertRecipeToUnits(outputs);
                    required = ConvertConstraintToUnits(required);
                }
            }
            else if (UseReturnedRecipe)
            {
                var cr = InvokePrepareRecipe(module, 1.0);

                inputs = cr.Inputs;
                outputs = cr.Outputs;
                required = cr.Requirements.Select(req => new ResourceConstraint(req));

                fillAmount = cr.FillAmount;
                takeAmount = cr.TakeAmount;
            }
            else
            {
                inputs = module.inputList;
                outputs = module.outputList;
                required = module.reqList.Select(req => new ResourceConstraint(req));

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

            if (!IgnoreBonus)
            {
                var multiplier = GetOptimalEfficiencyBonus(module, recipe);
                if (multiplier != 1.0)
                {
                    inputs = inputs.Select(res => res.WithMultiplier(multiplier));
                    outputs = outputs.Select(res => res.WithMultiplier(multiplier));
                }
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
        protected virtual double GetOptimalEfficiencyBonus(T converter, RecipeOverride recipe)
        {
            double bonus = recipe?.BaseEfficiency ?? 1.0;

            foreach (var (_, modifier) in converter.EfficiencyModifiers)
                bonus *= modifier;

            bonus *= multiplierExtractor?.GetValue(converter) ?? 1.0;
            bonus *= converter.GetCrewEfficiencyBonus();
            bonus *= recipe?.OverrideEfficiencyBonus ?? converter.EfficiencyBonus;
            bonus *= recipe?.OverrideThermalEfficiency ?? GetMaxThermalEfficiencyBonus(converter);

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

            foreach (var inner in node.GetNodes("RECIPE_OVERRIDE"))
            {
                var recipe = RecipeOverride.Load(inner);
                if (recipe == null)
                    continue;

                overrides.Add(recipe.name, recipe);
            }
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
