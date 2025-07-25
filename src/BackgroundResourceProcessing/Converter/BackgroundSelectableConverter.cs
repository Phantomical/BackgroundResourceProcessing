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

    public override ModuleBehaviour GetBehaviour(PartModule module)
    {
        for (int i = 0; i < options.Count; ++i)
        {
            var option = options[i];
            if (!option.condition.Evaluate(module))
                continue;

            LogUtil.Debug(() =>
                $"Selecting sub-converter {option.converter.GetType().Name} at index {i}"
            );
            return option.converter.GetBehaviour(module);
        }

        return null;
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

[Obsolete("BackgroundMultiConverter has been renamed to BackgroundSelectableConverter")]
public class BackgroundMultiConverter : BackgroundSelectableConverter { }
