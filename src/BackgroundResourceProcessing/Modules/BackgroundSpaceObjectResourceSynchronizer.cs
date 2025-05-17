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

            LogUtil.Debug($"Registering for OnBeforeVesselRecord for vessel {vessel.GetName()}");
            BackgroundResourceProcessor.OnBeforeVesselRecord.Add(OnBeforeVesselRecord);
        }

        void OnDestroy()
        {
            var module = GetLinkedProcessor();
            if (module == null)
                return;

            LogUtil.Debug($"Unregistering for OnBeforeVesselRecord for vessel {vessel.GetName()}");
            BackgroundResourceProcessor.OnBeforeVesselRecord.Remove(OnBeforeVesselRecord);
        }

        public void OnVesselRestore()
        {
            var resource = part.Resources.Get(ResourceName);
            var info = GetLinkedInfo();
            if (info == null || resource == null)
                return;

            var current = info.currentMassVal;
            var threshold = info.massThresholdVal;
            var expected = Math.Max(current - threshold, 0.0);

            if (Math.Abs(expected - resource.maxAmount) > 1e-3)
            {
                if (resource.maxAmount != 0.0)
                {
                    LogUtil.Warn(
                        $"SpaceObject {vessel.GetDisplayName()} mass was not updated because ",
                        $"resource mass was significantly different from expected mass: ",
                        $"(asteroid mass = {expected}, resource maxAmount = {resource.maxAmount})"
                    );
                }

                return;
            }

            info.currentMassVal = threshold + resource.amount;

            resource.amount = 0.0;
            resource.maxAmount = 0.0;
        }

        private void OnBeforeVesselRecord()
        {
            LogUtil.Log($"{GetType().Name}: OnBeforeVesselRecord");
            var resource = part.Resources.Get(ResourceName);
            var info = GetLinkedInfo();
            if (info == null || resource == null)
            {
                if (resource == null)
                    LogUtil.Warn($"{GetType().Name}: SpaceObject has no resource {ResourceName}");
                if (info == null)
                    LogUtil.Warn(
                        $"{GetType().Name}: SpaceObject has no linked ModuleSpaceObjectInfo"
                    );
                return;
            }

            LogUtil.Log($"currentMass:   {info.currentMass}");
            LogUtil.Log($"massThreshold: {info.massThreshold}");

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

            if (vessel == null)
                return null;

            processor = vessel.FindVesselModuleImplementing<BackgroundResourceProcessor>();
            return processor;
        }
    }
}
