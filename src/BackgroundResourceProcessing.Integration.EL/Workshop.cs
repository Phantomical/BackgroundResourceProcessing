using System.Collections.Generic;
using System.Linq;
using BackgroundResourceProcessing.Converter;
using ExtraplanetaryLaunchpads;

namespace BackgroundResourceProcessing.Integration.EL
{
    /// <summary>
    /// A background version of <see cref="ELWorkshop"/>. It produces
    /// <see cref="WorkHoursResource"/> at a constant rate determined by the
    /// workshop.
    /// </summary>
    ///
    /// <remarks>
    /// It assumes that there is a background inventory on the workshop
    /// module that can store <see cref="WorkHoursResource"/>.
    /// </remarks>
    public class BackgroundELWorkshop : BackgroundConverter<ELWorkshop>
    {
        [KSPField]
        public string WorkHoursResource = "BRPELWorkHours";

        [KSPField]
        public double BaseProductionRate = 1.0 / 3600.0;

        public override AdapterBehaviour GetBehaviour(ELWorkshop workshop)
        {
            if (!workshop.isActive)
                return null;

            var productivity = workshop.Productivity;
            if (productivity == 0.0)
                return null;

            List<ResourceRatio> outputs =
            [
                new()
                {
                    ResourceName = WorkHoursResource,
                    Ratio = BaseProductionRate * productivity,
                    DumpExcess = false,
                    FlowMode = ResourceFlowMode.NO_FLOW,
                },
            ];

            var behaviour = new AdapterBehaviour(new ConstantProducer(outputs));
            behaviour.AddPullModule(workshop);
            return behaviour;
        }
    }
}
