using System;
using System.Collections.Generic;
using System.Linq;

namespace BackgroundResourceProcessing.Utils
{
    /// <summary>
    /// A multiplier that can optionally be applied to a converter.
    /// </summary>
    internal struct ConverterMultiplier()
    {
        ModuleFilter condition = ModuleFilter.Always;
        FieldExtractor<double> field;
        double value = 1.0;

        public readonly double Evaluate(PartModule module)
        {
            if (!condition.Invoke(module))
                return 1.0;

            double mult = value;
            if (field != null)
                mult *= field.GetValue(module);

            return mult;
        }

        public static ConverterMultiplier Load(Type target, ConfigNode node)
        {
            ConverterMultiplier mult = new();
            string condition = null;
            string field = null;

            node.TryGetValue("Condition", ref condition);
            node.TryGetValue("Field", ref field);
            node.TryGetValue("Value", ref mult.value);

            if (field != null)
                mult.field = new(target, field, 1.0);
            if (condition != null)
                mult.condition = ModuleFilter.Compile(condition, node);

            return mult;
        }

        public static List<ConverterMultiplier> LoadAll(Type target, ConfigNode node)
        {
            var children = node.GetNodes("MULTIPLIER");
            List<ConverterMultiplier> list = new(children.Length);

            foreach (var child in children)
                list.Add(Load(target, child));
            return list;
        }
    }
}
