using System.Collections.Generic;
using System.Reflection;
using BackgroundResourceProcessing.Converter;
using ExtraplanetaryLaunchpads;

namespace BackgroundResourceProcessing.Integration.EL;

public class BackgroundELWorkshop : BackgroundConverter<ELWorkshop>
{
    private static readonly FieldInfo SourcesField = typeof(ELVesselWorkNet).GetField(
        "sources",
        BindingFlags.Instance | BindingFlags.NonPublic
    );

    const double BaseRate = 1.0 / 3600.0;

    [KSPField]
    public string ResourceName;

    public override ModuleBehaviour GetBehaviour(ELWorkshop workshop)
    {
        if (!workshop.isActive)
            return null;
        if (ResourceName == null)
            return null;

        var output = new ResourceRatio()
        {
            ResourceName = ResourceName,
            Ratio = workshop.Productivity * BaseRate,
        };

        var behaviour = new ModuleBehaviour(new ConstantProducer([output]));

        if (workshop.workNet)
        {
            var sources = (List<ELWorkSource>)SourcesField.GetValue(workshop.workNet);
            if (sources != null)
            {
                foreach (var source in sources)
                {
                    if (source is PartModule partModule)
                        behaviour.AddPushModule(partModule);
                }
            }
        }

        return behaviour;
    }
}
