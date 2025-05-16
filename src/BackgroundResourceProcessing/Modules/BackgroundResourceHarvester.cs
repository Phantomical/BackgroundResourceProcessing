using System;
using System.Collections.Generic;
using System.Linq;

namespace BackgroundResourceProcessing.Modules
{
    public class ModuleBackgroundResourceHarvester : ModuleBackgroundResourceConverter
    {
        [KSPField]
        public string ResourceName;

        [KSPField]
        public int HarvesterType = 0;

        [KSPField]
        public double airSpeedStatic;

        [KSPField]
        public double Efficiency = 1.0;

        protected ModuleResourceHarvester Harvester => (ModuleResourceHarvester)Converter;

        protected override ConverterBehaviour GetConverterBehaviour()
        {
            if (Converter == null)
                return null;
            if (!IsConverterEnabled())
                return null;

            List<ResourceRatio> outputs = [.. this.outputs];

            var type = (HarvestTypes)HarvesterType;
            var request = new AbundanceRequest()
            {
                Altitude = vessel.altitude,
                BodyId = FlightGlobals.currentMainBody.flightGlobalsIndex,
                CheckForLock = false,
                Latitude = vessel.latitude,
                Longitude = vessel.longitude,
                ResourceType = type,
                ResourceName = ResourceName,
            };

            double rate = ResourceMap.Instance.GetAbundance(request);
            rate *= Efficiency;
            rate *= GetOptimalEfficiencyBonus();
            if (type == HarvestTypes.Atmospheric)
                rate *= GetIntakeMultiplier();

            outputs.Add(
                new ResourceRatio()
                {
                    ResourceName = ResourceName,
                    Ratio = rate,
                    DumpExcess = type == HarvestTypes.Atmospheric,
                    FlowMode = ResourceFlowMode.NULL,
                }
            );

            return new ConstantConverter(inputs, outputs, required);
        }

        private double GetIntakeMultiplier()
        {
            // We never have to deal with resource harvesters flying through the
            // atmosphere so this is simpler than the code in ModuleResourceHarvester.
            var type = (HarvestTypes)HarvesterType;
            double mult = vessel.atmDensity;
            if (type == HarvestTypes.Exospheric)
                mult = 1.0;
            return mult * airSpeedStatic;
        }
    }
}
