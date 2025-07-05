using System.Collections.Generic;
using System.Linq;
using BackgroundResourceProcessing.Behaviour;
using BackgroundResourceProcessing.Utils;

namespace BackgroundResourceProcessing.Converter;

/// <summary>
/// A background converter that has explicitly specified resources.
/// </summary>
public class BackgroundConstantConverter : BackgroundConverter
{
    public List<ResourceRatio> inputs = [];
    public List<ResourceRatio> outputs = [];
    public List<ResourceConstraint> required = [];

    [KSPField]
    public string ActiveCondition = "true";

    [KSPField]
    public bool ConvertByMass = false;

    [KSPField]
    public bool PushToLocalBackgroundInventory = false;

    [KSPField]
    public bool PullFromLocalBackgroundInventory = false;

    private ModuleFilter activeCondition;
    private List<ConverterMultiplier> multipliers = [];

    public override ModuleBehaviour GetBehaviour(PartModule module)
    {
        if (!activeCondition.Invoke(module))
            return null;

        IEnumerable<ResourceRatio> inputs = this.inputs;
        IEnumerable<ResourceRatio> outputs = this.outputs;
        IEnumerable<ResourceConstraint> required = this.required;

        var mult = 1.0;
        foreach (var field in multipliers)
            mult *= field.Evaluate(module);

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

        var behaviour = new ModuleBehaviour(
            new ConstantConverter(inputs.ToList(), outputs.ToList(), required.ToList())
        );

        if (PushToLocalBackgroundInventory)
            behaviour.AddPushModule(module);
        if (PullFromLocalBackgroundInventory)
            behaviour.AddPullModule(module);

        return behaviour;
    }

    protected override void OnLoad(ConfigNode node)
    {
        base.OnLoad(node);

        var target = GetTargetType(node);
        activeCondition = ModuleFilter.Compile(ActiveCondition, node);
        multipliers = ConverterMultiplier.LoadAll(target, node);

        inputs.AddRange(ConfigUtil.LoadInputResources(node));
        outputs.AddRange(ConfigUtil.LoadOutputResources(node));
        required.AddRange(ConfigUtil.LoadRequiredResources(node));
    }
}
