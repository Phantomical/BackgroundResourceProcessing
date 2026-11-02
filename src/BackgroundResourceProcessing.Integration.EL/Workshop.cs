using System.Collections.Generic;
using System.Reflection;
using BackgroundResourceProcessing.Behaviour;
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

        // EL sums the productivity of every active work source on the vessel,
        // and individual workshops can have negative productivity (e.g. when
        // crewed by unskilled kerbals). Work only happens when that total is
        // positive. Model this by having the first active source on the
        // vessel produce the clamped total and the others produce nothing.
        // A negative output would otherwise reach the solver as consumption.
        List<ELWorkSource> sources = null;
        if (workshop.workNet)
            sources = (List<ELWorkSource>)SourcesField.GetValue(workshop.workNet);

        double productivity;
        if (sources != null)
        {
            ELWorkSource first = null;
            productivity = 0.0;
            foreach (var source in sources)
            {
                if (source == null || !source.isActive)
                    continue;

                first ??= source;
                productivity += source.Productivity;
            }

            if (!ReferenceEquals(first, workshop))
                return null;
        }
        else
        {
            productivity = workshop.Productivity;
        }

        if (productivity <= 0.0)
            return null;

        var output = new ResourceRatio()
        {
            ResourceName = ResourceName,
            Ratio = productivity * BaseRate,
        };

        var behaviour = new ModuleBehaviour(new ConstantProducer([output]));

        if (sources != null)
        {
            foreach (var source in sources)
            {
                if (source is PartModule partModule)
                    behaviour.AddPushModule(partModule);
            }
        }

        return behaviour;
    }
}
