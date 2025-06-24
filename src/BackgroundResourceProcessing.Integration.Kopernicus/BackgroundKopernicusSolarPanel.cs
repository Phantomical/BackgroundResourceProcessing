using System.Collections.Generic;
using BackgroundResourceProcessing.Modules;
using Kopernicus.Components;

namespace BackgroundResourceProcessing.Integration.Kopernicus
{
    public class ModuleBackgroundKopernicusSolarPanel : BackgroundConverterBase
    {
        protected override List<ConverterBehaviour> GetConverterBehaviours()
        {
            // TODO:
            //  - Support alternating between 0 and flowRate when going into and
            //    out of planet shadows.
            //  - Compute a non-zero background rate when in shadow.
            //  - Be smarter when landed on a planet.
            var netEC = 0.0;
            var panels = GetComponents<KopernicusSolarPanel>();

            foreach (var panel in panels)
                netEC += panel.currentOutput;

            var ratio = new ResourceRatio()
            {
                Ratio = netEC,
                ResourceName = "ElectricCharge",
                FlowMode = ResourceFlowMode.ALL_VESSEL_BALANCE,
            };

            if (netEC == 0.0)
                return null;

            return [new ConstantProducer([ratio])];
        }
    }
}
