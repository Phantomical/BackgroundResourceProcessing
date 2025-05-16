using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BackgroundResourceProcessing.Utils;

namespace BackgroundResourceProcessing.Modules
{
    /// <summary>
    /// A background converter that reads its recipe and efficiency multiplier
    /// from a linked <see cref="BaseConverter"/>.
    /// </summary>
    public class ModuleBackgroundResourceConverter
        : BackgroundConverter,
            IBackgroundVesselRestoreHandler
    {
        private static FieldInfo LastUpdateTimeField = typeof(BaseConverter).GetField(
            "lastUpdateTime",
            BindingFlags.Instance | BindingFlags.NonPublic
        );

        public List<ResourceRatio> inputs = [];
        public List<ResourceRatio> outputs = [];
        public List<ResourceRatio> required = [];

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
        /// The input, output, and required units are provided in terms of mass
        /// and will be converted to KSP units on start.
        /// </summary>
        [KSPField]
        public bool ConvertByMass = false;

        public BaseConverter Converter { get; private set; } = null;

        private bool emittedWarning = false;

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

        protected override ConverterBehaviour GetConverterBehaviour()
        {
            if (Converter == null)
                return null;
            if (!IsConverterEnabled())
                return null;

            IEnumerable<ResourceRatio> inputs = this.inputs;
            IEnumerable<ResourceRatio> outputs = this.outputs;
            IEnumerable<ResourceRatio> required = this.required;

            if (ConvertByMass)
            {
                inputs = ConvertRecipeToUnits(inputs);
                outputs = ConvertRecipeToUnits(outputs);
                required = ConvertRecipeToUnits(required);
            }

            var multiplier = GetOptimalEfficiencyBonus();
            if (multiplier != 1.0)
            {
                inputs = inputs.Select(res => res.WithMultiplier(multiplier));
                outputs = outputs.Select(res => res.WithMultiplier(multiplier));
            }

            return new ConstantConverter(inputs.ToList(), outputs.ToList(), required.ToList());
        }

        public override void OnStart(StartState state)
        {
            base.OnStart(state);
            Converter = GetLinkedBaseConverter();
            if (!Converter)
                return;

            var type = Converter.GetType();

            // We are handling the background updates, so override the lastUpdateTime
            // field on the converter so it doesn't do any catch-up.
            var lastUpdateField = type.GetField(
                "lastUpdateTime",
                BindingFlags.Instance | BindingFlags.NonPublic
            );
            lastUpdateField.SetValue(Converter, Planetarium.GetUniversalTime());
        }

        public virtual void OnVesselRestore()
        {
            Converter = GetLinkedBaseConverter();
            if (!Converter)
                return;

            LastUpdateTimeField.SetValue(Converter, Planetarium.GetUniversalTime());
        }

        protected double GetOptimalEfficiencyBonus()
        {
            double bonus = 1.0;

            foreach (var (_, modifier) in Converter.EfficiencyModifiers)
                bonus *= modifier;

            bonus *= Converter.GetCrewEfficiencyBonus();
            bonus *= EfficiencyBonus ?? Converter.EfficiencyBonus;
            bonus *= MaximumThermalEfficiency ?? GetMaxThermalEfficiencyBonus();

            return bonus;
        }

        private double GetMaxThermalEfficiencyBonus()
        {
            Converter.ThermalEfficiency.FindMinMaxValue(out var _, out var maxThermalEfficiency);
            return maxThermalEfficiency;
        }

        protected virtual BaseConverter GetLinkedBaseConverter()
        {
            return GetLinkedBaseConverterGeneric<BaseConverter>();
        }

        protected T GetLinkedBaseConverterGeneric<T>()
            where T : BaseConverter
        {
            var modules = part.FindModulesImplementing<T>()
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

        public override void OnLoad(ConfigNode node)
        {
            base.OnLoad(node);

            inputs = [.. ConfigUtil.LoadInputResources(node)];
            outputs = [.. ConfigUtil.LoadOutputResources(node)];
            required = [.. ConfigUtil.LoadRequiredResources(node)];
        }

        public override void OnSave(ConfigNode node)
        {
            base.OnSave(node);

            ConfigUtil.SaveInputResources(node, inputs);
            ConfigUtil.SaveOutputResources(node, outputs);
            ConfigUtil.SaveRequiredResources(node, required);
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
    }
}
