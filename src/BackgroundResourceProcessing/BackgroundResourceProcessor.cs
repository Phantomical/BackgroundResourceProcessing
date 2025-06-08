using System;
using BackgroundResourceProcessing.Addons;
using BackgroundResourceProcessing.Core;
using BackgroundResourceProcessing.Modules;

namespace BackgroundResourceProcessing
{
    public sealed class BackgroundResourceProcessor : VesselModule
    {
        private ResourceProcessor processor = new();

        /// <summary>
        /// A vessel-scoped event that is fired just before the vessel state is
        /// recorded.
        /// </summary>
        public static EventVoid OnBeforeVesselRecord { get; } = new("onBeforeVesselRecord");

        /// <summary>
        /// Whether background processing is actively running on this module.
        /// </summary>
        ///
        /// <remarks>
        /// Generally, this will be true when the vessel is unloaded, and false
        /// otherwise.
        /// </remarks>
        public bool BackgroundProcessingActive { get; private set; } = false;

        public override Activation GetActivation()
        {
            return Activation.LoadedOrUnloaded;
        }

        public override bool ShouldBeActive()
        {
            switch (HighLogic.LoadedScene)
            {
                case GameScenes.SPACECENTER:
                case GameScenes.TRACKSTATION:
                case GameScenes.PSYSTEM:
                case GameScenes.FLIGHT:
                    break;
                default:
                    return false;
            }

            return true;
        }

        public override int GetOrder()
        {
            // We want this to run before most other modules.
            //
            // The default order is 999, so this should be early enough to still
            // allow other modules to go first if they really want to.
            return 100;
        }

        protected override void OnStart()
        {
            RegisterCallbacks();

            if (!BackgroundProcessingActive)
                return;

            if (vessel.loaded)
            {
                LoadVessel();
            }
            else
            {
                EventDispatcher.RegisterChangepointCallback(this, processor.nextChangepoint);
            }
        }

        void OnDestroy()
        {
            if (BackgroundProcessingActive)
            {
                EventDispatcher.UnregisterChangepointCallbacks(this);
            }
            else if (vessel.loaded)
            {
                // SaveVessel();
            }

            UnregisterCallbacks();
        }

        public override void OnLoadVessel()
        {
            if (!BackgroundProcessingActive)
                return;

            LoadVessel();

            GameEvents.onGameStateSave.Add(OnGameStateSave);
        }

        public override void OnUnloadVessel()
        {
            if (BackgroundProcessingActive)
                return;

            SaveVessel();

            GameEvents.onGameStateSave.Remove(OnGameStateSave);
            EventDispatcher.RegisterChangepointCallback(this, processor.nextChangepoint);
        }

        private void LoadVessel()
        {
            var currentTime = Planetarium.GetUniversalTime();

            processor.UpdateInventories(currentTime);
            processor.ApplyInventories(Vessel);
            processor.ClearVesselState();

            NotifyOnVesselRestore();

            BackgroundProcessingActive = false;
        }

        private void SaveVessel()
        {
            var currentTime = Planetarium.GetUniversalTime();
            var state = new VesselState() { CurrentTime = currentTime, Vessel = Vessel };

            OnBeforeVesselRecord.Fire();
            processor.RecordVesselState(vessel, currentTime);
            processor.ForceUpdateBehaviours(state);
            processor.ComputeRates();
            processor.UpdateNextChangepoint(currentTime);

            BackgroundProcessingActive = true;
        }

        internal void OnChangepoint(double changepoint)
        {
            // We do nothing for active vessels.
            if (!BackgroundProcessingActive)
                return;

            LogUtil.Debug(() =>
                $"Updating vessel {vessel.GetDisplayName()} at changepoint {changepoint}"
            );

            var state = new VesselState { CurrentTime = changepoint, Vessel = Vessel };

            processor.UpdateInventories(changepoint);
            processor.UpdateBehaviours(state);
            processor.ComputeRates();
            processor.UpdateNextChangepoint(changepoint);

            EventDispatcher.RegisterChangepointCallback(this, processor.nextChangepoint);
        }

        // The EventDispatcher module takes care of calling this for only the
        // vessel that is actually switching.
        internal void OnVesselSOIChanged(GameEvents.HostedFromToAction<Vessel, CelestialBody> evt)
        {
            // We do nothing for active vessels.
            if (!BackgroundProcessingActive)
                return;
            if (!ReferenceEquals(vessel, evt.host))
                return;

            var state = new VesselState
            {
                CurrentTime = Planetarium.GetUniversalTime(),
                Vessel = Vessel,
            };

            processor.ForceUpdateBehaviours(state);
            processor.ComputeRates();
            processor.UpdateNextChangepoint(state.CurrentTime);

            EventDispatcher.UnregisterChangepointCallbacks(this);
            EventDispatcher.RegisterChangepointCallback(this, processor.nextChangepoint);
        }

        // We use this event to save the vessel state _before_ we actually get saved.
        private void OnGameStateSave(ConfigNode _)
        {
            if (!vessel.loaded)
                return;

            SaveVessel();
        }

        private void RegisterCallbacks()
        {
            if (vessel.loaded)
                GameEvents.onGameStateSave.Add(OnGameStateSave);
        }

        private void UnregisterCallbacks()
        {
            if (vessel.loaded)
                GameEvents.onGameStateSave.Remove(OnGameStateSave);
        }

        /// <summary>
        /// Find all background processing modules and resources on this vessel
        /// and update the module state accordingly.
        /// </summary>
        ///
        /// <remarks>
        /// The only reason this isn't private is so that the debug UI can use
        /// it to prepare the module for dumping.
        /// </remarks>
        internal void DebugRecordVesselState()
        {
            OnBeforeVesselRecord.Fire();
            processor.RecordVesselState(Vessel, Planetarium.GetUniversalTime());
            processor.ComputeRates();
            processor.UpdateNextChangepoint(Planetarium.GetUniversalTime());
        }

        internal void DebugClearVesselState()
        {
            processor.ClearVesselState();
        }

        private void NotifyOnVesselRestore()
        {
            foreach (var part in Vessel.Parts)
            {
                foreach (var module in part.Modules)
                {
                    if (module is not IBackgroundVesselRestoreHandler handler)
                        continue;

                    try
                    {
                        handler.OnVesselRestore();
                    }
                    catch (Exception e)
                    {
                        var typeName = handler.GetType().FullName;

                        LogUtil.Error(
                            $"OnVesselRestore handler for type {typeName} threw an excecption: {e}"
                        );
                    }
                }
            }
        }

        protected override void OnSave(ConfigNode node)
        {
            base.OnSave(node);
            processor.Save(node);

            node.AddValue("BackgroundProcessingActive", BackgroundProcessingActive);
        }

        protected override void OnLoad(ConfigNode node)
        {
            base.OnLoad(node);
            processor.Load(node);

            bool active = false;
            if (node.TryGetValue("BackgroundProcessingActive", ref active))
                BackgroundProcessingActive = active;
        }
    }
}
