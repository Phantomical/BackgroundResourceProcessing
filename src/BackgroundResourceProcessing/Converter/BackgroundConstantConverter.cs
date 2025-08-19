using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BackgroundResourceProcessing.Behaviour;
using BackgroundResourceProcessing.Utils;

namespace BackgroundResourceProcessing.Converter;

/// <summary>
/// A background converter that has explicitly specified resources.
/// </summary>
public class BackgroundConstantConverter : BackgroundConverter
{
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    public List<ResourceRatioExpression> inputs = [];
    public List<ResourceRatioExpression> outputs = [];
    public List<ResourceConstraintExpression> required = [];

    public ConditionalExpression ActiveCondition = ConditionalExpression.Always;

    [KSPField]
    public bool ConvertByMass = false;

    [KSPField]
    public bool PushToLocalBackgroundInventory = false;

    [KSPField]
    public bool PullFromLocalBackgroundInventory = false;

    [KSPField]
    public string LastUpdateField = null;

    public FieldExpression<object> InputList = new(_ => null, "null");
    public FieldExpression<object> OutputList = new(_ => null, "null");

    private List<ConverterMultiplier> multipliers = [];
    private FieldInfo lastUpdateField = null;

    public override ModuleBehaviour GetBehaviour(PartModule module)
    {
        if (!ActiveCondition.Evaluate(module))
            return null;

        IEnumerable<ResourceRatio> inputs = this.inputs.Select(input => input.Evaluate(module));
        IEnumerable<ResourceRatio> outputs = this.outputs.Select(output => output.Evaluate(module));
        IEnumerable<ResourceConstraint> required = this.required.Select(req =>
            req.Evaluate(module)
        );

        inputs = inputs.Concat(GetResourceList(module, InputList) ?? []);
        outputs = outputs.Concat(GetResourceList(module, OutputList) ?? []);

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
            new ConstantConverter([.. inputs], [.. outputs], [.. required])
        );

        if (PushToLocalBackgroundInventory)
            behaviour.AddPushModule(module);
        if (PullFromLocalBackgroundInventory)
            behaviour.AddPullModule(module);

        lastUpdateField?.SetValue(module, Planetarium.GetUniversalTime());

        return behaviour;
    }

    private IEnumerable<ResourceRatio> GetResourceList(
        PartModule module,
        FieldExpression<object> expr
    )
    {
        if (!expr.TryEvaluate(module, out var value))
            return null;

        return value switch
        {
            IEnumerable<ResourceRatio> ratios => ratios,
            IEnumerable<ModuleResource> resources => resources.Select(
                resource => new ResourceRatio()
                {
                    ResourceName = resource.name,
                    Ratio = resource.rate,
                    DumpExcess = false,
                    FlowMode = resource.flowMode,
                }
            ),
            null => null,
            _ => throw new NotImplementedException(
                $"Unable to read a rate list from a value of type {value.GetType().Name}"
            ),
        };
    }

    protected override void OnLoad(ConfigNode node)
    {
        base.OnLoad(node);

        var target = GetTargetType(node);

        node.TryGetCondition(nameof(ActiveCondition), target, ref ActiveCondition);
        node.TryGetExpression(nameof(InputList), target, ref InputList);
        node.TryGetExpression(nameof(OutputList), target, ref OutputList);

        multipliers = ConverterMultiplier.LoadAll(target, node);

        inputs.AddRange(ResourceRatioExpression.LoadInputs(target, node));
        outputs.AddRange(ResourceRatioExpression.LoadOutputs(target, node));
        required.AddRange(ResourceConstraintExpression.LoadRequirements(target, node));

        if (LastUpdateField != null)
        {
            var field = target.GetField(LastUpdateField, Flags);
            if (field.FieldType == typeof(double))
                lastUpdateField = field;
            else
            {
                LogUtil.Error(
                    $"{target.Name}.{field.Name} has unsupported type {field.FieldType.Name} (expected double instead)"
                );
            }
        }
    }
}
