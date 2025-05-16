using System;

namespace BackgroundResourceProcessing.Modules
{
    /// <summary>
    /// A module that synchronizes the mass of a space object with a resource.
    /// </summary>
    public sealed class ModuleBackgroundSpaceObjectResourceSynchronizer
        : PartModule,
            IBackgroundVesselRestoreHandler
    {
        [KSPField]
        public string ResourceName;

        private ModuleSpaceObjectInfo info;
        private BackgroundResourceProcessor processor;

        public override void OnStart(StartState state)
        {
            base.OnStart(state);

            var module = GetLinkedProcessor();
            if (module == null)
                return;

            module.OnBeforeVesselRecord.Add(OnBeforeVesselRecord);
        }

        void OnDestroy()
        {
            var module = GetLinkedProcessor();
            if (module == null)
                return;

            module.OnBeforeVesselRecord.Remove(OnBeforeVesselRecord);
        }

        public void OnVesselRestore()
        {
            var resource = part.Resources.Get(ResourceName);
            var info = GetLinkedInfo();
            if (info == null || resource == null)
                return;

            info.currentMassVal = info.massThresholdVal + resource.amount;

            resource.amount = 0.0;
            resource.maxAmount = 0.0;
        }

        private void OnBeforeVesselRecord(EmptyEventData _)
        {
            var resource = part.Resources.Get(ResourceName);
            var info = GetLinkedInfo();
            if (info == null || resource == null)
                return;

            resource.amount = Math.Max(info.currentMassVal - info.massThresholdVal, 0.0);
            resource.maxAmount = resource.amount;
        }

        private ModuleSpaceObjectInfo GetLinkedInfo()
        {
            if (info != null)
                return info;

            info = part.FindModuleImplementing<ModuleSpaceObjectInfo>();
            return info;
        }

        private BackgroundResourceProcessor GetLinkedProcessor()
        {
            if (processor != null)
                return processor;

            processor = vessel.FindVesselModuleImplementing<BackgroundResourceProcessor>();
            return processor;
        }
    }
}
