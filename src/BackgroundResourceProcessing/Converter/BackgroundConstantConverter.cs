using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BackgroundResourceProcessing.Behaviour;
using BackgroundResourceProcessing.Collections;
using BackgroundResourceProcessing.Utils;

namespace BackgroundResourceProcessing.Converter;

/// <summary>
/// A background converter that has explicitly specified resources.
/// </summary>
public class BackgroundConstantConverter : BackgroundConverter
{
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
    private List<LinkExpression> links = [];
    private MemberAccessor<double>? lastUpdateField = null;

    public override ModuleBehaviour GetBehaviour(PartModule module)
    {
        if (!ActiveCondition.Evaluate(module))
            return null;

        var inputs = this.inputs.TrySelect(
            (ResourceRatioExpression input, out ResourceRatio value) =>
                input.Evaluate(module, out value)
        );
        var outputs = this.outputs.TrySelect(
            (ResourceRatioExpression output, out ResourceRatio value) =>
                output.Evaluate(module, out value)
        );
        var required = this.required.TrySelect(
            (ResourceConstraintExpression req, out ResourceConstraint value) =>
                req.Evaluate(module, out value)
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

        foreach (var link in links)
            link.Evaluate(module, behaviour);

        return behaviour;
    }

    public override void OnRestore(PartModule module, ResourceConverter converter)
    {
        lastUpdateField?.SetValue(module, Planetarium.GetUniversalTime());
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
        links = LinkExpression.LoadList(target, node);

        inputs.AddRange(ResourceRatioExpression.LoadInputs(target, node));
        outputs.AddRange(ResourceRatioExpression.LoadOutputs(target, node));
        required.AddRange(ResourceConstraintExpression.LoadRequirements(target, node));

        if (LastUpdateField != null)
            lastUpdateField = new(target, LastUpdateField, MemberAccessor<double>.Access.Write);
    }
}
