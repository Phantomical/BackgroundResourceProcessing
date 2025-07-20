using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BackgroundResourceProcessing.Behaviour;
using BackgroundResourceProcessing.Collections;
using BackgroundResourceProcessing.Utils;

namespace BackgroundResourceProcessing.Converter;

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

    private static readonly FieldInfo PreCalculateEfficiencyField = typeof(BaseConverter).GetField(
        "_preCalculateEfficiency",
        Flags
    );

    protected static readonly FieldInfo LastUpdateTimeField = typeof(BaseConverter).GetField(
        "lastUpdateTime",
        Flags
    );

    /// <summary>
    /// Override the condition used to determine whether this converter is active.
    /// </summary>
    public ConditionalExpression? ActiveCondition = null;

    /// <summary>
    /// Whether to use the recipe as returned by <c>PrepareRecipe</c> or whether
    /// to instead attempt to compute the steady-state recipe ourselves.
    /// Defaults to <c>true</c>.
    /// </summary>
    public ConditionalExpression UsePreparedRecipe = ConditionalExpression.Always;

    /// <summary>
    /// If false, then we will attempt to calculate an optimal efficiency bonus.
    /// Otherwise, the current efficiency bonus of the converter is used.
    /// </summary>
    public ConditionalExpression UseCurrentEfficiency = ConditionalExpression.Never;

    /// <summary>
    /// Override the computed efficiency for this converter.
    /// </summary>
    ///
    /// <remarks>
    /// This can be useful if you want to compute the efficiency yourself using
    /// multipliers.
    /// </remarks>
    [KSPField]
    public double? OverrideEfficiency = null;

    private List<ConverterMultiplier> multipliers;

    public override ModuleBehaviour GetBehaviour(T module)
    {
        if (module == null)
            return null;
        var enabled = ActiveCondition?.Evaluate(module) ?? IsConverterEnabled(module);
        if (!enabled)
            return null;

        var recipe = GetMultipliedRecipe(module);
        if (recipe == null)
            return null;

        return new(recipe);
    }

    public override void OnRestore(T module, ResourceConverter converter)
    {
        LastUpdateTimeField.SetValue(module, Planetarium.GetUniversalTime());
    }

    /// <summary>
    /// Get the recipe before any efficiency multiplier is applied.
    /// Override this if you want to add resource rates that get multiplied by
    /// efficiency bonuses.
    /// </summary>
    /// <param name="module"></param>
    /// <returns></returns>
    protected virtual ConstantConverter GetBaseRecipe(T module)
    {
        IEnumerable<ResourceRatio> inputs;
        IEnumerable<ResourceRatio> outputs;
        IEnumerable<ResourceConstraint> required;

        double fillAmount;
        double takeAmount;

        if (UsePreparedRecipe.Evaluate(module))
        {
            var recipe = InvokePrepareRecipe(module, 1.0) ?? new();

            inputs = recipe.Inputs;
            outputs = recipe.Outputs;
            required = recipe.Requirements.Select(req => new ResourceConstraint(req));

            fillAmount = recipe.FillAmount;
            takeAmount = recipe.TakeAmount;
        }
        else
        {
            inputs = module.inputList;
            outputs = module.outputList;
            required = module.reqList.Select(req => new ResourceConstraint(req));

            fillAmount = module.FillAmount;
            takeAmount = module.TakeAmount;

            if (ConvertByMass(module))
            {
                inputs = ConvertRecipeToUnits(inputs);
                outputs = ConvertRecipeToUnits(outputs);
                required = ConvertConstraintToUnits(required);
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

        return new(inputList, outputList, [.. required]);
    }

    /// <summary>
    /// Get the recipe after efficiency multipliers are applied.
    /// Override this if you want to add resource rates that are not effected
    /// by efficiency bonuses (or other multipliers).
    /// </summary>
    /// <param name="module"></param>
    /// <returns></returns>
    protected virtual ConstantConverter GetMultipliedRecipe(T module)
    {
        var recipe = GetBaseRecipe(module);
        if (recipe == null)
            return null;

        var bonus = GetEfficiencyMultiplier(module);

        if (bonus != 1.0)
        {
            for (int i = 0; i < recipe.inputs.Count; ++i)
                recipe.inputs[i] = recipe.inputs[i].WithMultiplier(bonus);
            for (int i = 0; i < recipe.outputs.Count; ++i)
                recipe.inputs[i] = recipe.inputs[i].WithMultiplier(bonus);
        }

        return recipe;
    }

    /// <summary>
    /// Compute the total efficiency bonus for the converter, including
    /// multipliers and overrides.
    /// </summary>
    /// <param name="module"></param>
    /// <returns></returns>
    protected virtual double GetEfficiencyMultiplier(T module)
    {
        bool useEfficiencyBonus =
            !UsePreparedRecipe.Evaluate(module)
            && (bool)PreCalculateEfficiencyField.GetValue(module);

        double bonus;
        if (OverrideEfficiency != null)
            bonus = (double)OverrideEfficiency;
        else if (!useEfficiencyBonus)
            bonus = 1.0;
        else if (UseCurrentEfficiency.Evaluate(module))
            bonus = module.GetEfficiencyMultiplier();
        else
            bonus = GetOptimalEfficiencyBonus(module);

        foreach (var multiplier in multipliers)
            bonus *= multiplier.Evaluate(module);

        return bonus;
    }

    /// <summary>
    /// Compute the optimal efficiency bonus for the converter.
    /// </summary>
    /// <param name="module"></param>
    /// <returns></returns>
    protected virtual double GetOptimalEfficiencyBonus(T module)
    {
        double bonus = 1.0;

        foreach (var (_, modifier) in module.EfficiencyModifiers)
            bonus *= modifier;

        bonus *= module.GetCrewEfficiencyBonus();
        bonus *= module.EfficiencyBonus;
        bonus *= GetMaxThermalEfficiencyBonus(module);

        return bonus;
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
    /// Indicates whether the resource rates are specified in units of tons.
    /// </summary>
    protected virtual bool ConvertByMass(T converter)
    {
        if (converter is ModuleResourceConverter rc)
            return rc.ConvertByMass;
        return false;
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

        if (node.TryGetCondition2(nameof(ActiveCondition), out var activeCondition))
            ActiveCondition = activeCondition;

        node.TryGetCondition(nameof(UsePreparedRecipe), ref UsePreparedRecipe);
        node.TryGetCondition(nameof(UseCurrentEfficiency), ref UseCurrentEfficiency);

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
}

/// <summary>
/// A background converter module for types implementing <see cref="BaseConverter"/>.
///
/// Don't inherit from this class, use the generic version instead.
/// </summary>
public class BackgroundResourceConverter : BackgroundResourceConverter<BaseConverter> { }
