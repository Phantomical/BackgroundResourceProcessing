using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnifiedBackgroundProcessing.Utils;

namespace UnifiedBackgroundProcessing.Modules
{
    /// <summary>
    /// A background converter that reads its recipe and efficiency multiplier
    /// from a linked <see cref="BaseConverter"/>.
    /// </summary>
    public class ModuleBackgroundResourceConverter : BackgroundConverter
    {
        /// <summary>
        /// The name of the converter to use to find the efficiency of this
        /// converter in the background.
        /// </summary>
        [KSPField]
        public string ConverterName = null;

        /// <summary>
        /// Specify an efficiency bonus to be used instead of the one on the
        /// referenced <see cref="BaseConverter"/> module.
        /// </summary>
        [KSPField]
        public double? EfficiencyBonus = null;

        /// <summary>
        /// Specify the maximum thermal efficiency multiplier to be used
        /// instead of the one on the referenced <see cref="BaseConverter"/>
        /// module.
        /// </summary>
        [KSPField]
        public double? MaximumThermalEfficiency = null;

        /// <summary>
        /// Indicates that the resources have units specified by mass, which
        /// need to be converted to regular units.
        /// </summary>
        [KSPField]
        public bool ConvertByMass = false;

        public BaseConverter Converter { get; private set; } = null;
        private MethodInfo ConverterPrepareRecipe = null;

        private bool emittedWarning = false;

        public override void OnStart(StartState state)
        {
            base.OnStart(state);
            Converter = GetLinkedBaseConverter();
            ConverterPrepareRecipe = typeof(BaseConverter).GetMethod(
                "PrepareRecipe",
                BindingFlags.Instance | BindingFlags.NonPublic
            );
        }

        public override ConverterBehaviour GetBehaviour()
        {
            if (Converter == null)
                return null;
            if (!Converter.ModuleIsActive())
                return null;

            var recipe = GetConversionRecipe();
            if (recipe == null)
                return null;

            var inputs = recipe.Inputs;
            var outputs = recipe.Outputs;
            var required = recipe.Requirements;

            if (ConvertByMass)
            {
                inputs = ConvertToUnits(inputs);
                outputs = ConvertToUnits(outputs);
                required = ConvertToUnits(required);
            }

            var multiplier = GetOptimalEfficiencyBonus();
            if (multiplier != 1.0)
            {
                inputs = [.. inputs.Select(res => res.WithMultiplier(multiplier))];
                outputs = [.. outputs.Select(res => res.WithMultiplier(multiplier))];
            }

            return new ConstantConverter(inputs, outputs, required);
        }

        private ConversionRecipe GetConversionRecipe()
        {
            // PrepareRecipe is protected so we need to call it via reflection
            if (ConverterPrepareRecipe == null)
                return null;

            return ConverterPrepareRecipe.Invoke(Converter, [Planetarium.GetUniversalTime()])
                as ConversionRecipe;
        }

        private double GetOptimalEfficiencyBonus()
        {
            double bonus = 1.0;

            foreach (var (_, modifier) in Converter.EfficiencyModifiers)
                bonus *= modifier;

            bonus *= Converter.GetCrewEfficiencyBonus();
            bonus *= EfficiencyBonus ?? Converter.EfficiencyBonus;
            bonus *= MaximumThermalEfficiency ?? GetMaxThermalEfficiencyBonus();

            return bonus;
        }

        private static List<ResourceRatio> ConvertToUnits(List<ResourceRatio> resources)
        {
            return
            [
                .. resources.Select(resource =>
                {
                    var resourceDef = PartResourceLibrary.Instance.resourceDefinitions[
                        resource.ResourceName
                    ];

                    if (resourceDef.density > 1e-9f)
                    {
                        resource.Ratio /= resourceDef.density;
                    }

                    return resource;
                }),
            ];
        }

        private double GetMaxThermalEfficiencyBonus()
        {
            Converter.ThermalEfficiency.FindMinMaxValue(out var _, out var maxThermalEfficiency);
            return maxThermalEfficiency;
        }

        private BaseConverter GetLinkedBaseConverter()
        {
            var modules = part.FindModulesImplementing<BaseConverter>()
                .Where(module => module.ConverterName == ConverterName)
                .ToList();

            if (modules.Count == 0)
            {
                EmitMissingModuleWarning();
                return null;
            }

            if (modules.Count > 1)
                EmitMultipleMatchingModulesWarning();
            return modules[0];
        }

        private void EmitMissingModuleWarning()
        {
            if (emittedWarning)
                return;

            LogUtil.Warn($"Part has no modules with ConverterName matching {ConverterName}");
            emittedWarning = true;
        }

        private void EmitMultipleMatchingModulesWarning()
        {
            if (emittedWarning)
                return;

            LogUtil.Warn(
                $"Part has multiple modules with ConverterName matching {ConverterName}. The first matching one will be used."
            );
            emittedWarning = true;
        }
    }
}
