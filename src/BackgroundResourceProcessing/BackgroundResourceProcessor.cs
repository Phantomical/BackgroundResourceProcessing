using System;
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
            LogUtil.Debug(() =>
                $"OnStart for BackgroundResourceProcessor on vessel {vessel.GetDisplayName()}"
            );

            if (processor.nextChangepoint == double.PositiveInfinity)
                return;

            Registrar.RegisterChangepointCallback(this, processor.nextChangepoint);
            GameEvents.onVesselSOIChanged.Add(OnVesselSOIChanged);
        }

        void OnDestroy()
        {
            Registrar.UnregisterChangepointCallbacks(this);
            GameEvents.onVesselSOIChanged.Remove(OnVesselSOIChanged);
        }

        public override void OnLoadVessel()
        {
            var currentTime = Planetarium.GetUniversalTime();

            processor.UpdateInventories(currentTime);
            processor.ApplyInventories(Vessel);
            processor.ClearVesselState();

            Registrar.UnregisterChangepointCallbacks(this);

            NotifyOnVesselRestore();
        }

        public override void OnUnloadVessel()
        {
            var currentTime = Planetarium.GetUniversalTime();
            var state = new VesselState() { CurrentTime = currentTime, Vessel = Vessel };

            processor.RecordVesselState(vessel, currentTime);
            processor.ForceUpdateBehaviours(state);
            processor.ComputeRates();
            processor.UpdateNextChangepoint(currentTime);

            Registrar.RegisterChangepointCallback(this, processor.nextChangepoint);
        }

        internal void OnChangepoint(double changepoint)
        {
            // We do nothing for active vessels.
            if (vessel.loaded)
                return;

            var state = new VesselState { CurrentTime = changepoint, Vessel = Vessel };

            processor.UpdateInventories(changepoint);
            processor.UpdateBehaviours(state);
            processor.ComputeRates();
            processor.UpdateNextChangepoint(changepoint);

            Registrar.RegisterChangepointCallback(this, processor.nextChangepoint);
        }

        private void OnVesselSOIChanged(GameEvents.HostedFromToAction<Vessel, CelestialBody> _)
        {
            // We do nothing for active vessels.
            if (vessel.loaded)
                return;

            var state = new VesselState
            {
                CurrentTime = Planetarium.GetUniversalTime(),
                Vessel = Vessel,
            };

            processor.ForceUpdateBehaviours(state);
            processor.ComputeRates();
            processor.UpdateNextChangepoint(state.CurrentTime);

            Registrar.UnregisterChangepointCallbacks(this);
            Registrar.RegisterChangepointCallback(this, processor.nextChangepoint);
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
        internal void RecordVesselState()
        {
            OnBeforeVesselRecord.Fire();
            processor.RecordVesselState(Vessel, Planetarium.GetUniversalTime());
        }

        internal void ClearVesselState()
        {
            processor.ClearVesselState();
        }

        internal void ComputeResourceRates()
        {
            processor.ComputeRates();
        }

        internal void UpdateNextChangepoint()
        {
            processor.UpdateNextChangepoint(Planetarium.GetUniversalTime());
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
        }

        protected override void OnLoad(ConfigNode node)
        {
            base.OnLoad(node);
            processor.Load(node);
        }
    }
}
