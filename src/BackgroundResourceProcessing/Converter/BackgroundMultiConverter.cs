using System;
using System.Collections.Generic;
using BackgroundResourceProcessing.Utils;

namespace BackgroundResourceProcessing.Converter
{
    /// <summary>
    /// A background converter which selects between multiple different
    /// converters at runtime based on their filter expressions.
    /// </summary>
    public sealed class BackgroundMultiConverter : BackgroundConverter
    {
        readonly List<ConverterEntry> options = [];

        public override AdapterBehaviour GetBehaviour(PartModule module)
        {
            foreach (var option in options)
            {
                if (!option.filter.Invoke(module))
                    continue;

                return option.converter.GetBehaviour(module);
            }

            return null;
        }

        protected override void OnLoad(ConfigNode node)
        {
            base.OnLoad(node);

            foreach (var child in node.GetNodes(NodeName))
            {
                try
                {
                    var converter = Load(child);

                    ModuleFilter filter;
                    string filterExpr = null;
                    if (child.TryGetValue("filter", ref filterExpr))
                        filter = ModuleFilter.Compile(filterExpr, child);
                    else
                        filter = ModuleFilter.Always;

                    options.Add(new() { filter = filter, converter = converter });
                }
                catch (Exception e)
                {
                    LogUtil.Error(e);
                }
            }
        }

        private struct ConverterEntry
        {
            public ModuleFilter filter;
            public BackgroundConverter converter;
        }
    }
}
