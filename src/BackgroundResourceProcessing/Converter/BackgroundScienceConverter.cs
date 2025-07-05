using System;
using System.Reflection;
using BackgroundResourceProcessing.Behaviour;

namespace BackgroundResourceProcessing.Converter;

public class BackgroundScienceConverter : BackgroundConverter<ModuleScienceConverter>
{
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private static readonly MethodInfo GetScientistsMethod =
        typeof(ModuleScienceConverter).GetMethod("GetScientists", Flags);

    [KSPField]
    public string DataResourceName = "BRPScienceLabData";

    [KSPField]
    public string ScienceResourceName = "BRPScience";

    [KSPField]
    public double MaxError = 0.1;

    public override ModuleBehaviour GetBehaviour(ModuleScienceConverter module)
    {
        var lab = module.Lab;
        if (lab == null)
            return null;

        if (!module.IsActivated)
            return null;

        var converter = new ScienceConverterBehaviour
        {
            DataResourceName = DataResourceName,
            ScienceResourceName = ScienceResourceName,
            LabFlightId = lab.part.flightID,
            LabModuleId = lab.GetPersistentId(),
            PowerRequirement = module.powerRequirement,
            Productivity =
                module.dataProcessingMultiplier
                * GetScientists(module)
                / Math.Pow(10.0, module.researchTime),
            ScienceMultiplier = module.scienceMultiplier,
            MaxError = MaxError,
        };

        var behaviour = new ModuleBehaviour(converter);
        behaviour.AddPushModule(module.Lab);
        behaviour.AddPullModule(module.Lab);
        return behaviour;
    }

    private static float GetScientists(ModuleScienceConverter module)
    {
        return (float)GetScientistsMethod.Invoke(module, []);
    }
}
