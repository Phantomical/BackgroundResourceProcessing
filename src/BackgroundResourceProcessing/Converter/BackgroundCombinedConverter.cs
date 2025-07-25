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

    public override ModuleBehaviour GetBehaviour(PartModule module)
    {
        ModuleBehaviour combined = null;
        foreach (var option in options)
        {
            if (!option.condition.Evaluate(module))
                continue;

            int priority = option.converter.GetModulePriority(module);
            var behaviour = option.converter.GetBehaviour(module);
            foreach (var converter in behaviour.Converters ?? [])
                converter.Priority ??= priority;

            MergeBehaviours(ref combined, behaviour);
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

        MergeLists(ref dest.Converters, src.Converters);
        MergeLists(ref dest.Push, src.Push);
        MergeLists(ref dest.Pull, src.Pull);
        MergeLists(ref dest.Constraint, src.Constraint);
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
