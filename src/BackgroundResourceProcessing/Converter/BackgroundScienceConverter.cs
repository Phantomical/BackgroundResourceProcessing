using System.Collections.Generic;

namespace BackgroundResourceProcessing.Converter;

public class BackgroundScienceConverter : BackgroundConverter<ModuleScienceConverter>
{
    [KSPField]
    public string TimePassedResourceName = "BRPScienceLabTime";

    public override ModuleBehaviour GetBehaviour(ModuleScienceConverter module)
    {
        List<ResourceRatio> inputs =
        [
            new ResourceRatio
            {
                FlowMode = ResourceFlowMode.ALL_VESSEL,
                Ratio = module.powerRequirement,
                ResourceName = "ElectricCharge",
                DumpExcess = true,
            },
        ];

        List<ResourceRatio> outputs =
        [
            new ResourceRatio
            {
                FlowMode = ResourceFlowMode.NO_FLOW,
                Ratio = 1.0,
                ResourceName = TimePassedResourceName,
            },
        ];

        var behaviour = new ModuleBehaviour(new ConstantConverter(inputs, outputs));
        behaviour.AddPushModule(module);
        return behaviour;
    }
}
