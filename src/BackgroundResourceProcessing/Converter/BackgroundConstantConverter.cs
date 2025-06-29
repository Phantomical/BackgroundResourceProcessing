using System.Collections.Generic;
using System.Linq;
using BackgroundResourceProcessing.Utils;

namespace BackgroundResourceProcessing.Converter
{
    /// <summary>
    /// A background converter that has explicitly specified resources.
    /// </summary>
    public class BackgroundConstantConverter : BackgroundConverter
    {
        public List<ResourceRatio> inputs = [];
        public List<ResourceRatio> outputs = [];
        public List<ResourceConstraint> required = [];

        [KSPField]
        public string ActiveCondition = null;

        [KSPField]
        public double Multiplier = 1.0;

        [KSPField]
        public string MultiplierField = null;

        [KSPField]
        public bool ConvertByMass = false;

        private ModuleFilter activeCondition;
        private FieldExtractor<double> multiplierField;

        public override ModuleBehaviour GetBehaviour(PartModule module)
        {
            if (!activeCondition.Invoke(module))
                return null;

            IEnumerable<ResourceRatio> inputs = this.inputs;
            IEnumerable<ResourceRatio> outputs = this.outputs;
            IEnumerable<ResourceConstraint> required = this.required;

            var mult = Multiplier * multiplierField.GetValue(module);
            if (mult != 1.0)
            {
                inputs = inputs.Select(input => input.WithMultiplier(mult));
                outputs = outputs.Select(output => output.WithMultiplier(mult));
            }

            if (ConvertByMass)
            {
                inputs = BackgroundResourceConverter.ConvertRecipeToUnits(inputs);
                outputs = BackgroundResourceConverter.ConvertRecipeToUnits(outputs);
                required = BackgroundResourceConverter.ConvertConstraintToUnits(required);
            }

            return new(new ConstantConverter(inputs.ToList(), outputs.ToList(), required.ToList()));
        }

        protected override void OnLoad(ConfigNode node)
        {
            base.OnLoad(node);

            var target = GetTargetType(node);
            multiplierField = new(target, MultiplierField, 1.0);

            if (ActiveCondition != null)
                activeCondition = ModuleFilter.Compile(ActiveCondition, node);
            else
                activeCondition = ModuleFilter.Always;
        }
    }
}
