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
        public int TargetIndex = -1;

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

        private bool cached = false;
        private T module = null;

        [KSPField]
        private uint cachedPersistentModuleId = 0;

        public override void OnStart(StartState state)
        {
            base.OnStart(state);
            var _ = Module;
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
            var cached = FindLinkedModuleCached();
            if (cached != null)
                return cached;

            cachedPersistentModuleId = 0;

            T found = null;
            int index = 0;
            for (int i = 0; i < part.Modules.Count; ++i)
            {
                var module = part.Modules[i] as T;
                if (module == null)
                    continue;

                var type = module.GetType();
                if (type.Name != TargetModule)
                    continue;

                var current = index++;
                if (TargetIndex >= 0 && current != TargetIndex)
                    continue;

                if (found != null)
                {
                    LogUtil.Warn(
                        $"{GetType().Name}: Multiple modules of type {TargetModule} found on part {part.name}",
                        " but TargetIndex was not specified. Consider specifying TargetIndex. Only the first module found ",
                        "will be used."
                    );
                    continue;
                }

                found = module;
            }

            if (index < TargetIndex)
            {
                LogUtil.Error(
                    $"{GetType().Name}: Part {part.name} does not have {TargetIndex} modules of type {TargetModule}"
                );
                return null;
            }

            if (found == null)
            {
                LogUtil.Error(
                    $"{GetType().Name}: No converter module of type {TargetModule} with index {TargetIndex} found on part {part.name}. This module will be disabled."
                );
                return null;
            }

            return found;
        }

        private T GetLinkedModule()
        {
            var module = FindLinkedModule();

            if (module != null)
                cachedPersistentModuleId = module.GetPersistentId();

            cached = true;
            return module;
        }

        private T FindLinkedModuleCached()
        {
            if (cachedPersistentModuleId == 0)
                return null;

            var module = part.Modules[cachedPersistentModuleId];
            if (module == null)
                return null;

            var type = module.GetType();
            if (type.Name != TargetModule)
                return null;

            return module as T;
        }
    }
}
