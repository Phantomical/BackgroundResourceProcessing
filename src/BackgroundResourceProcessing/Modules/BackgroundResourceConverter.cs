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
        public List<ResourceConstraint> required = [];

        /// <summary>
        /// The name of the module that this converter should getting its
        /// efficiency from.
        /// </summary>
        [KSPField]
        public string TargetModule = null;

        /// <summary>
        /// The name of the converter to use to find the efficiency of this
        /// converter in the background.
        /// </summary>
        [KSPField]
        public string ConverterName = null;

        /// <summary>
        /// The index of the target converter module along the list of all
        /// converter modules with the same type.
        /// </summary>
        [KSPField]
        public int TargetIndex = -1;

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
            if (type.Name != TargetModule)
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
            int index = 0;
            for (int i = 0; i < part.Modules.Count; ++i)
            {
                var module = part.Modules[i] as BaseConverter;
                if (module == null)
                    continue;

                var type = module.GetType();
                if (type.Name != TargetModule)
                    continue;
                var current = index;
                index += 1;

                if (TargetIndex != -1)
                {
                    if (current != TargetIndex)
                        continue;

                    if (module.ConverterName != null && module.ConverterName != ConverterName)
                    {
                        LogUtil.Error(
                            $"{TargetModule} module at index {TargetIndex} does not have ConverterName '{ConverterName}'"
                        );
                        return null;
                    }
                }
                else
                {
                    if (ConverterName != null && module.ConverterName != ConverterName)
                        continue;
                }

                if (module is not T downcasted)
                {
                    LogUtil.Error(
                        $"{TargetModule} module with ConverterName '{ConverterName}' was not of type {typeof(T).Name}"
                    );
                    return null;
                }

                if (found != null)
                {
                    LogUtil.Warn(
                        $"Multiple modules of type {TargetModule} with ConverterName ",
                        $"{ConverterName ?? "null"}. Only the first one will be used by this {GetType().Name} module."
                    );
                    continue;
                }

                found = downcasted;
            }

            if (index < TargetIndex)
            {
                LogUtil.Error(
                    $"Part {part.name} does not have {TargetIndex} modules of type {TargetModule}"
                );
                return null;
            }

            if (found == null)
            {
                LogUtil.Warn(
                    $"No converter module of type {TargetModule} with ConverterName {ConverterName ?? "null"} ",
                    $"found on part {part.partName}. This {GetType().Name} module will be disabled."
                );
                return null;
            }

            cachedPersistentModuleId = found.GetPersistentId();
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
