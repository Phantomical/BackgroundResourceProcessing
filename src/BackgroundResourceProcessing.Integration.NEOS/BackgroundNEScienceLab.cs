using System;
using System.Collections.Generic;
using System.Reflection;
using BackgroundResourceProcessing.Behaviour;
using BackgroundResourceProcessing.Converter;
using NE_Science;

namespace BackgroundResourceProcessing.Integration.NEOS;

public class BackgroundNEScienceLab : BackgroundConverter<Lab>
{
    private const BindingFlags Flags =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private static readonly FieldInfo GeneratorsField = typeof(Lab).GetField("generators", Flags);
    private static readonly MethodInfo IsActiveMethod = typeof(Lab).GetMethod("isActive", Flags);

    public override ModuleBehaviour GetBehaviour(Lab module)
    {
        if (!IsActive(module))
            return null;

        var generators = GetGenerators(module);
        if (generators == null || generators.Count == 0)
            return null;

        var behaviour = new ModuleBehaviour();
        foreach (var generator in generators)
        {
            if (generator.rates.Count == 0)
                continue;

            List<ResourceRatio> inputs = [];
            List<ResourceRatio> outputs = [];

            foreach (var rate in generator.rates.Values)
            {
                // None of the current experiments seem to use this so we don't
                // end up supporting it.
                if (rate.isScience)
                    return null;

                var normalizedRate = rate.ratePerSecond;
                var ratio = new ResourceRatio()
                {
                    ResourceName = rate.resource,
                    Ratio = Math.Abs(normalizedRate),
                    DumpExcess = false,
                    FlowMode = ResourceFlowMode.NULL,
                };

                if (normalizedRate < 0.0)
                    outputs.Add(ratio);
                else
                    inputs.Add(ratio);
            }

            behaviour.Add(new ConstantConverter(inputs, outputs));
        }

        return behaviour;
    }

    public override void OnRestore(Lab module, ResourceConverter converter) =>
        module.LastActive = Planetarium.GetUniversalTime();

    private static bool IsActive(Lab lab) => (bool)IsActiveMethod.Invoke(lab, []);

    private static List<Generator> GetGenerators(Lab lab) =>
        (List<Generator>)GeneratorsField.GetValue(lab);
}
