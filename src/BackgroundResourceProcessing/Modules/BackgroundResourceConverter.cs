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
        : BackgroundConverter,
            IBackgroundVesselRestoreHandler
    {
        protected static readonly FieldInfo LastUpdateTimeField = typeof(BaseConverter).GetField(
            "lastUpdateTime",
            BindingFlags.Instance | BindingFlags.NonPublic
        );

        public List<ResourceRatio> inputs = [];
        public List<ResourceRatio> outputs = [];
        public List<ResourceRatio> required = [];

        /// <summary>
        /// The name of the module that this converter should getting its
        /// efficiency from.
        /// </summary>
        [KSPField]
        public string ConverterModule = null;

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

        public BaseConverter Converter { get; protected set; } = null;

        private uint? cachedPersistentModuleId = null;

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
        protected virtual ConversionRecipe GetAdditionalRecipe()
        {
            return null;
        }

        /// <summary>
        /// Get an additional efficiency multiplier to apply on top of the
        /// default optimal efficiency bonus.
        /// </summary>
        /// <returns></returns>
        protected virtual double GetOptimalEfficiencyBonus()
        {
            double bonus = EfficiencyMultiplier;

            foreach (var (_, modifier) in Converter.EfficiencyModifiers.KSPEnumerate())
                bonus *= modifier;

            bonus *= Converter.GetCrewEfficiencyBonus();
            bonus *= EfficiencyBonus ?? Converter.EfficiencyBonus;
            bonus *= MaximumThermalEfficiency ?? GetMaxThermalEfficiencyBonus();

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
            IEnumerable<ResourceRatio> required = this.required;

            var additional = GetAdditionalRecipe();
            if (additional != null)
            {
                if (additional.Inputs.Count != 0)
                    inputs = [.. inputs, .. additional.Inputs];
                if (additional.Outputs.Count != 0)
                    outputs = [.. outputs, .. additional.Outputs];
                if (additional.Requirements.Count != 0)
                    required = [.. required, .. additional.Requirements];
            }

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
        }

        public virtual void OnVesselRestore()
        {
            Converter = GetLinkedBaseConverter();
            if (!Converter)
                return;

            LastUpdateTimeField.SetValue(Converter, Planetarium.GetUniversalTime());
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

        private T GetLinkedBaseConverterCached<T>()
            where T : BaseConverter
        {
            var existing = Converter as T;
            if (existing != null)
                return existing;

            if (cachedPersistentModuleId == null)
                return null;
            var persistentId = (uint)cachedPersistentModuleId;
            var module = part.Modules[persistentId];
            if (module == null)
                return null;

            var type = module.GetType();
            if (type.Name != ConverterModule)
                return null;

            var downcasted = module as T;
            if (downcasted != null)
                return null;

            if (downcasted.ConverterName != ConverterName)
                return null;

            return downcasted;
        }

        protected T GetLinkedBaseConverterGeneric<T>()
            where T : BaseConverter
        {
            var cached = GetLinkedBaseConverterCached<T>();
            if (cached != null)
                return cached;
            cachedPersistentModuleId = null;

            T found = null;
            for (int i = 0; i < part.Modules.Count; ++i)
            {
                var module = part.Modules[i] as BaseConverter;
                if (module == null)
                    continue;

                var type = module.GetType();
                if (type.Name != ConverterModule)
                    continue;

                if (ConverterName != null && module.ConverterName != ConverterName)
                    continue;

                if (module is not T downcasted)
                    continue;

                if (found != null)
                {
                    LogUtil.Warn(
                        $"Multiple modules of type {ConverterModule} with ConverterName ",
                        $"{ConverterName ?? "null"}. Only the first one will be used by this {GetType().Name} module."
                    );
                    continue;
                }

                found = downcasted;
            }

            if (found == null)
            {
                LogUtil.Warn(
                    $"No converter module of type {ConverterModule} with ConverterName {ConverterName ?? "null"} ",
                    $"found on part {part.partName}. This {GetType().Name} module will be disabled."
                );
                return null;
            }

            cachedPersistentModuleId = found.PersistentId;
            return found;
        }

        public override void OnLoad(ConfigNode node)
        {
            base.OnLoad(node);

            inputs.AddRange(ConfigUtil.LoadInputResources(node));
            outputs.AddRange(ConfigUtil.LoadOutputResources(node));
            required.AddRange(ConfigUtil.LoadRequiredResources(node));

            ConfigUtil.TryGetModuleId(
                node,
                "cachedPersistentModuleId",
                out cachedPersistentModuleId
            );
        }

        public override void OnSave(ConfigNode node)
        {
            base.OnSave(node);

            ConfigUtil.AddModuleId(node, "cachedPersistentModuleId", cachedPersistentModuleId);
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
