using System;
using System.Collections.Generic;
using System.Reflection;

namespace BackgroundResourceProcessing.Modules
{
    public class ModuleBackgroundScienceConverter
        : ModuleBackgroundResourceConverter,
            IBackgroundPartResource
    {
        [KSPField]
        public string TimePassedResourceName;

        double? savedLastUpdate = null;

        protected ModuleScienceConverter ScienceConverter => (ModuleScienceConverter)Converter;

        protected override double GetOptimalEfficiencyBonus()
        {
            return 1.0;
        }

        public override void OnVesselRestore()
        {
            // We explicitly override OnVesselRestore since we have our own way
            // of setting lastUpdate and we don't want BackgroundResourceConverter
            // messing with that.
            Converter = GetLinkedBaseConverter();
        }

        public override BackgroundResourceSet GetLinkedBackgroundResources()
        {
            BackgroundResourceSet set = new();
            set.AddPushResource(this);
            return set;
        }

        public IEnumerable<FakePartResource> GetResources()
        {
            if (ScienceConverter == null)
                return null;
            if (ScienceConverter.Lab == null)
                return null;

            savedLastUpdate = (double)LastUpdateTimeField.GetValue(ScienceConverter);

            var remaining = ScienceConverter.Lab.dataStorage - ScienceConverter.Lab.dataStorage;
            FakePartResource resource = new()
            {
                resourceName = TimePassedResourceName,
                amount = 0.0,
                maxAmount = ScienceConverter.CalculateResearchTime(remaining),
            };

            return [resource];
        }

        public void UpdateStoredAmount(string resourceName, double amount)
        {
            if (resourceName != TimePassedResourceName)
                return;
            if (ScienceConverter == null)
                return;

            var now = Planetarium.GetUniversalTime();
            var lastUpdate = (double)LastUpdateTimeField.GetValue(ScienceConverter);

            if (savedLastUpdate != null)
            {
                // The amount of time that was simulated without us noticing.
                // This can happen if other mods happen to be doing things to
                // labs in the background.
                //
                // In that case, we try to remain accurate, but this may not be
                // possible depending on how updates have happened.
                var missed = Math.Max((double)savedLastUpdate - lastUpdate, 0.0);

                // If we missed more time than we should have, we just saturate
                // to say that no time has passed.
                amount = Math.Max(amount - missed, 0.0);
            }

            var newUpdate = Math.Min(lastUpdate + amount, now);
            LastUpdateTimeField.SetValue(ScienceConverter, newUpdate);
        }

        protected override BaseConverter GetLinkedBaseConverter()
        {
            return GetLinkedBaseConverterGeneric<ModuleScienceConverter>();
        }

        public override void OnSave(ConfigNode node)
        {
            base.OnSave(node);

            if (savedLastUpdate != null)
                node.AddValue("savedLastUpdate", (double)savedLastUpdate);
        }

        public override void OnLoad(ConfigNode node)
        {
            base.OnLoad(node);

            double savedLastUpdate = 0.0;
            if (node.TryGetValue("savedLastUpdate", ref savedLastUpdate))
                this.savedLastUpdate = savedLastUpdate;
        }
    }
}
