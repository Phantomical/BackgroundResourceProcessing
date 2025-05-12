using System.Collections.Generic;

namespace UnifiedBackgroundProcessing.Modules
{
    /// <summary>
    /// A module that summarizes all <see cref="ModuleDeployableSolarPanel"/>
    /// modules on the current part as background producers.
    /// </summary>
    public class ModuleBackgroundSolarPanel : BackgroundConverter
    {
        public override ConverterBehaviour GetBehaviour()
        {
            // TODO:
            //  - Support alternating between 0 and flowRate when going into and
            //    out of planet shadows.
            //  - Compute a non-zero background rate when in shadow.
            //  - Be smarter when landed on a planet.
            var resources = new Dictionary<string, ResourceRatio>();
            var panels = GetComponents<ModuleDeployableSolarPanel>();

            foreach (var panel in panels)
            {
                if (!resources.TryGetValue(panel.resourceName, out var ratio))
                {
                    ratio = new()
                    {
                        ResourceName = panel.resourceName,
                        DumpExcess = true,
                        FlowMode = ResourceFlowMode.ALL_VESSEL_BALANCE,
                    };
                }

                ratio.Ratio += panel.flowRate;
                resources[panel.resourceName] = ratio;
            }

            return new ConstantProducer([.. resources.Values]);
        }
    }
}
