using UnifiedBackgroundProcessing.Core;

namespace UnifiedBackgroundProcessing.VesselModules
{
    public class BackgroundResourceProcessor : VesselModule
    {
        private ResourceProcessor processor = new();

        /// <summary>
        /// Whether background processing is actively running on this module.
        /// </summary>
        ///
        /// <remarks>
        /// Generally, this will be true when the vessel is on rails, and false
        /// otherwise.
        /// </remarks>
        public bool BackgroundProcessingActive { get; private set; } = false;

        public override Activation GetActivation()
        {
            return Activation.LoadedOrUnloaded;
        }

        public override int GetOrder()
        {
            return base.GetOrder();
        }

        protected override void OnStart()
        {
            if (processor.nextChangepoint == double.PositiveInfinity)
                return;

            Registrar.RegisterChangepointCallback(
                this,
                processor.nextChangepoint
            );
        }

        protected void OnDestroy()
        {
            Registrar.UnregisterChangepointCallbacks(this);
        }

        public override void OnUnloadVessel()
        {
            if (Registrar.Timer == null)
            {
                LogUtil.Error(
                    "BackgroundProcessorModule.OnUnloadVessel called but there is ",
                    "no active instance of the UnifiedBackgroundProcessing addon"
                );
                return;
            }

            var currentTime = Planetarium.GetUniversalTime();
            var state = new VesselState() { CurrentTime = currentTime, Vessel = Vessel };

            processor.RecordVesselState(vessel, currentTime);
            processor.ForceUpdateBehaviours(state);
            processor.ComputeRates();
            processor.UpdateNextChangepoint(currentTime);

            if (processor.nextChangepoint == double.PositiveInfinity)
                return;

            Registrar.RegisterChangepointCallback(
                this,
                processor.nextChangepoint
            );
        }

        public override void OnLoadVessel()
        {
            var currentTime = Planetarium.GetUniversalTime();
            var name = Vessel.GetDisplayName();

            processor.UpdateInventories(name, currentTime);
            // processor.ApplyInventories();

            Registrar.UnregisterChangepointCallbacks(this);
        }

        internal void OnChangepoint(double changepoint)
        {
            // We do nothing for active vessels.
            if (vessel.loaded)
                return;

            var name = Vessel.GetDisplayName();
            var state = new VesselState { CurrentTime = changepoint, Vessel = Vessel };

            processor.UpdateInventories(name, changepoint);
            processor.UpdateBehaviours(state);
            processor.ComputeRates();
            processor.UpdateNextChangepoint(changepoint);

            Registrar.RegisterChangepointCallback(
                this,
                processor.nextChangepoint
            );
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
            processor.RecordVesselState(Vessel, Planetarium.GetUniversalTime());
        }

        internal void ClearVesselState()
        {
            processor.ClearVesselState();
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
