using System;
using System.Collections.Generic;
using BackgroundResourceProcessing.Utils;

namespace BackgroundResourceProcessing.Converter;

/// <summary>
/// A converter that combines together multiple different background converters.
/// Any nested converter within whose entry condition evaluates to <c>true</c>
/// (or is not present) will be merged together into one larger behaviour.
/// </summary>
public class BackgroundCombinedConverter : BackgroundConverter
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

        ModuleBehaviour combined = null;
        for (int i = 0; i < options.Count; ++i)
        {
            var option = options[i];
            if (!option.condition.Evaluate(module))
                continue;

            int priority = option.converter.GetModulePriority(module);
            var behaviour = option.converter.GetBehaviour(module);

            if (DebugSettings.Instance.DebugLogging)
                LogUtil.Debug(() =>
                    $"Selecting sub-converter {option.converter.GetType().Name} at index {i}"
                );

            if (behaviour?.Converters != null)
            {
                foreach (var converter in behaviour.Converters)
                    converter.Priority ??= priority;
            }

            MergeBehaviours(ref combined, behaviour);
        }

        if (combined != null)
        {
            foreach (var link in links)
                link.Evaluate(module, combined);
        }

        return combined;
    }

    void MergeBehaviours(ref ModuleBehaviour dest, ModuleBehaviour src)
    {
        if (src == null)
            return;

        if (dest == null)
        {
            dest = src;
            return;
        }

        MergeLists(ref dest.converters, src.converters);
        MergeLists(ref dest.push, src.push);
        MergeLists(ref dest.pull, src.pull);
        MergeLists(ref dest.constraint, src.constraint);
    }

    void MergeLists<T>(ref List<T> dest, List<T> src)
    {
        if (src == null)
            return;
        if (dest == null)
            dest = src;
        else
            dest.AddRange(src);
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

public class SelectAll : BackgroundCombinedConverter { }
