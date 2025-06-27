using System;
using System.Collections.Generic;
using BackgroundResourceProcessing.Utils;
using UnityEngine.Rendering;

namespace BackgroundResourceProcessing.Modules
{
    /// <summary>
    /// A background converter that interacts with another linked module on the
    /// same part.
    /// </summary>
    /// <typeparam name="T">A type that the linked module must be derived from.</typeparam>
    public abstract class BackgroundLinkedConverter<T> : ModuleBackgroundConverter
        where T : PartModule
    {
        /// <summary>
        /// The name of the module that this converter should getting its
        /// efficiency from.
        /// </summary>
        [KSPField]
        public string TargetModule = null;

        /// <summary>
        /// The index of the target converter module along the list of all
        /// converter modules with the same type.
        /// </summary>
        [KSPField]
        public int? TargetIndex = null;

        /// <summary>
        /// A filter expression that can be used to filter which module is
        /// selected.
        /// </summary>
        [KSPField]
        public string TargetFilter = null;

        /// <summary>
        /// The module that this one should be interacting with.
        /// </summary>
        ///
        /// <remarks>
        /// This will be initialized on first access or <see cref="OnStart"/>,
        /// whichever happens first. Future accesses will be cached.
        /// </remarks>
        public T Module
        {
            get
            {
                if (cached)
                    return module;

                module = GetLinkedModule();
                return module;
            }
        }

        // KSP stores part modules as part of a prefab. That uses Unity's serialization
        // system to instantiate a new copy of the module.
        //
        // However, we can only compile the filter with the original ConfigNode.
        // As it is not serializable, this means we need to do some custom tricks
        // in order to actually recover it when the module is recreated.
        //
        // To do so we keep a unique filterID which is used to get the original
        // filter back.

        private bool cached = false;

        private T module = null;

        private Func<PartModule, bool> filter = null;

        // The index of the compiled filter in the global list. This is used to
        // recover the filter when we are reloaded.
        [UnityEngine.SerializeField]
        private int filterID = -1;

        public override void OnStart(StartState state)
        {
            base.OnStart(state);
            var _ = Module;
        }

        public override string GetLabel()
        {
            if (module == null)
                return $"{base.GetLabel()} targeting null";
            return $"{base.GetLabel()} targeting {module.GetType().Name}";
        }

        /// <summary>
        /// Find the linked module.
        /// </summary>
        ///
        /// <remarks>
        /// If you have specific additional constraints on the type of the
        /// module you want to return (beyond what you can specify by setting
        /// <typeparamref name="T"/>) then you can override this. Generally you
        /// will still want to use the base implementation to actually find the
        /// relevant module candidate.
        /// </remarks>
        protected virtual T FindLinkedModule()
        {
            T found = null;
            int index = 0;
            for (int i = 0; i < part.Modules.Count; ++i)
            {
                var module = part.Modules[i] as T;
                if (module == null)
                    continue;

                if (TargetModule != null)
                {
                    var type = module.GetType();
                    if (type.Name != TargetModule)
                        continue;
                }

                var current = index++;
                if (TargetIndex != null)
                {
                    if (current != (int)TargetIndex)
                        continue;
                }

                if (filter != null)
                {
                    try
                    {
                        if (!filter(module))
                            continue;
                    }
                    catch (Exception e)
                    {
                        LogUtil.Error($"Target filter evaluation threw an exception:\n{e}");
                        continue;
                    }
                }

                if (found != null)
                {
                    LogUtil.Warn(
                        $"{GetType().Name}: Multiple modules found on part {part.name} ",
                        "matching filters. Only the first one will be selected."
                    );
                    continue;
                }

                found = module;

                if (TargetIndex != null)
                    break;
            }

            if (TargetIndex != null && index < TargetIndex && TargetModule != null)
            {
                LogUtil.Error(
                    $"{GetType().Name}: Part {part.name} does not have {TargetIndex} modules of type {TargetModule}"
                );
                return null;
            }

            if (found == null)
            {
                if (TargetModule != null)
                {
                    LogUtil.Error(
                        $"{GetType().Name}: No converter module of type {TargetModule} matching filters found on part {part.name}. This module will be disabled."
                    );
                }
                else
                {
                    LogUtil.Error(
                        $"{GetType().Name}: No converter module matching filters found on part {part.name}. This module will be disabled."
                    );
                }
                return null;
            }

            return found;
        }

        private T GetLinkedModule()
        {
            var module = FindLinkedModule();
            cached = true;
            return module;
        }

        public override void OnLoad(ConfigNode node)
        {
            base.OnLoad(node);

            if (TargetFilter != null && filter == null)
                filter = CompileFilter(node);

            if (TargetModule == null && TargetIndex != -1)
            {
                LogUtil.Warn(
                    $"{GetType().Name}: TargetModule not specified but TargetIndex is. Behaviour will be unpredictable."
                );
            }
        }

        public override void OnCopy(PartModule fromModule)
        {
            base.OnCopy(fromModule);

            if (fromModule is not BackgroundLinkedConverter<T> other)
                return;

            cached = false;
            module = null;
            filter = other.filter;
            filterID = other.filterID;
        }

        private static List<Func<PartModule, bool>> CompiledFilters = [];
        private static Func<PartModule, bool> EmptyFilter = _ => false;

        private Func<PartModule, bool> CompileFilter(ConfigNode node)
        {
            if (filter != null)
                return filter;

            if (filterID > 0)
                return CompiledFilters[filterID];

            Func<PartModule, bool> compiled = EmptyFilter;
            try
            {
                compiled = ModuleFilter.Compile(TargetFilter, node);
            }
            catch (ModuleFilterException e)
            {
                LogUtil.Error(
                    $"PART[{part.name}]:MODULE[{GetType().Name}]: Error while compiling TargetFilter:\n{e}"
                );
            }

            var id = CompiledFilters.Count;
            CompiledFilters.Add(compiled);
            filterID = id;

            return compiled;
        }
    }
}
