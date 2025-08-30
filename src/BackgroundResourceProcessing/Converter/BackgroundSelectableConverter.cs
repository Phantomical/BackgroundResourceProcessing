using System;
using System.Collections.Generic;
using BackgroundResourceProcessing.Utils;

namespace BackgroundResourceProcessing.Converter;

/// <summary>
/// A background converter which selects between multiple different
/// converters at runtime based on their filter expressions.
/// </summary>
public class BackgroundSelectableConverter : BackgroundConverter
{
    readonly List<ConverterEntry> options = [];

    /// <summary>
    /// A condition to evaluate to determine whether this converter should
    /// be active. This can be any target filter expression.
    /// </summary>
    public ConditionalExpression ActiveCondition = ConditionalExpression.Always;

    private List<LinkExpression> links = [];

    public override ModuleBehaviour GetBehaviour(PartModule module)
    {
        if (!ActiveCondition.Evaluate(module))
            return null;

        ModuleBehaviour behaviour = null;
        for (int i = 0; i < options.Count; ++i)
        {
            var option = options[i];
            if (!option.condition.Evaluate(module))
                continue;

            if (DebugSettings.Instance.DebugLogging)
                LogUtil.Debug(() =>
                    $"Selecting sub-converter {option.converter.GetType().Name} at index {i}"
                );
            behaviour = option.converter.GetBehaviour(module);
            break;
        }

        if (behaviour != null)
        {
            foreach (var link in links)
                link.Evaluate(module, behaviour);
        }

        return behaviour;
    }

    protected override void OnLoad(ConfigNode node)
    {
        base.OnLoad(node);

        var name = node.GetValue("name");
        var target = GetTargetType(node);

        node.TryGetCondition(nameof(ActiveCondition), target, ref ActiveCondition);
        links = LinkExpression.LoadList(target, node);

        foreach (var child in node.GetNodes(NodeName))
        {
            try
            {
                child.SetValue("name", name, createIfNotFound: true);

                ConditionalExpression filter = ConditionalExpression.Always;
                string condition = null;
                if (child.TryGetValue("condition", ref condition))
                    filter = ConditionalExpression.Compile(condition, child, target);

                var converter = Load(child);

                options.Add(new() { condition = filter, converter = converter });
            }
            catch (Exception e)
            {
                LogUtil.Error(e);
            }
        }
    }

    private struct ConverterEntry
    {
        public ConditionalExpression condition;
        public BackgroundConverter converter;
    }
}

[Obsolete("BackgroundMultiConverter has been renamed to BackgroundSelectableConverter")]
public class BackgroundMultiConverter : BackgroundSelectableConverter { }

public class SelectFirst : BackgroundSelectableConverter { }
