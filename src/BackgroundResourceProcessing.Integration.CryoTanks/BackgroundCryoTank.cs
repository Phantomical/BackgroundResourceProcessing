using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BackgroundResourceProcessing.Behaviour;
using BackgroundResourceProcessing.Converter;
using SimpleBoiloff;

namespace BackgroundResourceProcessing.Integration.CryoTanks;

// Modelling cryo tank cooling is a little complicated since it doesn't quite
// behave like a standard converter. What we really want to model is basically
// this:
// - EC is consumed to prevent boiloff
// - if there's no EC then fuel boils off
// - partial EC results in partial cooling
//
// On its own a single converter cannot actually model this. However, we can
// model this by chaining together a few different converters with different
// priorities:
// 1. We'll have one converter that produces a "Boiloff" resource at a
//    a constant rate provided that the cryo resource has an amount >=
//    something small like 1e-5.
// 2. We then have a normal-priority converter that consumes EC and Boiloff.
// 3. Finally we have a low-priority converter that consumes the resource
//    and Boiloff.
//
// This way as long as there is EC available then the cooling converter will
// use up all of the boiloff resource, starving the other one. When there's
// not enough left then the boiloff converter will use up the rest to boil
// off resources as appropriate.

public class BackgroundCryoTank : BackgroundConverter<ModuleCryoTank>
{
    const BindingFlags Flags = BindingFlags.NonPublic | BindingFlags.Instance;
    static readonly FieldInfo FuelsField = typeof(ModuleCryoTank).GetField("fuels", Flags);
    static readonly MethodInfo IsCoolableMethod = typeof(ModuleCryoTank).GetMethod(
        "IsCoolable",
        Flags
    );

    [KSPField]
    public double MaxError = 0.1;

    [KSPField]
    public string BoiloffResource = "BRPCryoTankBoiloff";

    public override ModuleBehaviour GetBehaviour(ModuleCryoTank module)
    {
        if (!module.HasAnyBoiloffResource)
            return null;

        List<BoiloffFuel> fuels = [.. GetFuels(module).Where(fuel => fuel.fuelPresent)];
        List<ConverterBehaviour> behaviours = [];

        foreach (var fuel in fuels)
        {
            string boiloffResource;
            if (fuels.Count == 1)
                boiloffResource = BoiloffResource;
            else
                boiloffResource = $"{BoiloffResource}:{fuel.fuelName}";

            behaviours.Add(
                new ConstantConverter(
                    inputs: [],
                    outputs:
                    [
                        new ResourceRatio()
                        {
                            ResourceName = boiloffResource,
                            Ratio = 1.0,
                            DumpExcess = false,
                            FlowMode = ResourceFlowMode.NO_FLOW,
                        },
                    ],
                    required:
                    [
                        new ResourceConstraint()
                        {
                            ResourceName = fuel.fuelName,
                            Amount = 1e-6,
                            Constraint = Constraint.AT_LEAST,
                            FlowMode = ResourceFlowMode.NO_FLOW,
                        },
                    ]
                )
            );
            behaviours.Add(
                new CryoTankBoiloffBehaviour(fuel.outputs)
                {
                    BoiloffResourceName = boiloffResource,
                    FuelInventoryId = new(module.part, fuel.fuelName),
                    BoiloffRate = fuel.boiloffRateSeconds,
                    MaxError = MaxError,
                    Priority = -10,
                }
            );

            if (module.CoolingEnabled && IsCoolable(module))
            {
                behaviours.Add(
                    new ConstantConsumer(
                        [
                            new ResourceRatio()
                            {
                                ResourceName = boiloffResource,
                                Ratio = 1.0,
                                FlowMode = ResourceFlowMode.NO_FLOW,
                            },
                            new ResourceRatio()
                            {
                                ResourceName = "ElectricCharge",
                                Ratio = fuel.FuelCoolingCost() * fuel.FuelAmountMax() * 0.001,
                                FlowMode = ResourceFlowMode.NULL,
                            },
                        ]
                    )
                );
            }
        }

        var behaviour = new ModuleBehaviour(behaviours);
        behaviour.AddPushModule(module);
        behaviour.AddPullModule(module);
        return behaviour;
    }

    internal static List<BoiloffFuel> GetFuels(ModuleCryoTank module)
    {
        return (List<BoiloffFuel>)FuelsField.GetValue(module) ?? [];
    }

    private static bool IsCoolable(ModuleCryoTank module)
    {
        return (bool)IsCoolableMethod.Invoke(module, []);
    }
}
