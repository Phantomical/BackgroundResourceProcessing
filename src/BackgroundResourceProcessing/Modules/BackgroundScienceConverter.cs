using System;
using System.Reflection;

namespace BackgroundResourceProcessing.Modules
{
    public class ModuleBackgroundScienceConverter : ModuleBackgroundResourceConverter
    {
        private static readonly FieldInfo LastUpdateTimeField = typeof(BaseConverter).GetField(
            "lastUpdateTime",
            BindingFlags.Instance | BindingFlags.NonPublic
        );

        [KSPField]
        public string TimePassedResourceName;

        protected ModuleScienceConverter ScienceConverter => (ModuleScienceConverter)Converter;

        protected override double GetOptimalEfficiencyBonus()
        {
            return 1.0;
        }

        public override void OnStart(StartState state)
        {
            base.OnStart(state);

            BackgroundResourceProcessor.OnBeforeVesselRecord.Add(OnBeforeVesselRecord);
        }

        protected void OnDestroy()
        {
            BackgroundResourceProcessor.OnBeforeVesselRecord.Remove(OnBeforeVesselRecord);
        }

        public override void OnVesselRestore()
        {
            base.OnVesselRestore();

            if (ScienceConverter == null)
                return;

            var resource = part.Resources.Get(TimePassedResourceName);
            if (resource == null)
                return;

            var currentTime = Planetarium.GetUniversalTime();
            var lastUpdate = (double)LastUpdateTimeField.GetValue(ScienceConverter);

            var deltaT = Math.Max(currentTime - lastUpdate, 0.0);
            var activeT = Math.Min(resource.amount, deltaT);
            resource.amount -= activeT;

            var newLastUpdate = lastUpdate + activeT;
            LastUpdateTimeField.SetValue(ScienceConverter, newLastUpdate);
        }

        private void OnBeforeVesselRecord()
        {
            var resource = part.Resources.Get(TimePassedResourceName);
            if (resource == null)
                return;

            resource.amount = 0.0;
        }

        protected override BaseConverter GetLinkedBaseConverter()
        {
            return GetLinkedBaseConverterGeneric<ModuleScienceConverter>();
        }
    }
}
