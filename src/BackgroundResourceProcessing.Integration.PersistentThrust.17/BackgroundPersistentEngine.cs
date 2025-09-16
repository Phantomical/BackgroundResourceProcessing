using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using BackgroundResourceProcessing.Converter;
using PersistentThrust;

namespace BackgroundResourceProcessing.Integration.PersistentThrust;

public class BackgroundPersistentEngine : BackgroundConverter<PersistentEngine>
{
    static readonly FieldInfo AlternatorEngineField = typeof(ModuleAlternator).GetField(
        "engine",
        BindingFlags.Instance | BindingFlags.NonPublic
    );

    public override ModuleBehaviour GetBehaviour(PersistentEngine module)
    {
        var settings = HighLogic.CurrentGame.Parameters.CustomParams<ModIntegrationSettings>();
        if (!settings.EnablePersistentThrustIntegration)
            return null;

        if (!module.isPersistentEngine)
            return null;
        if (!module.HasPersistentThrust)
            return null;

        if (module.persistentThrust == 0.0)
            return null;
        if (module.vesselHeadingVersusManeuverInDegree > module.maneuverToleranceInDegree + 1)
            return null;

        ModuleBehaviour behaviour = new();

        var engines = module.isMultiMode ? [module.currentEngine] : module.moduleEngines;
        var alternators = module.part.FindModulesImplementing<ModuleAlternator>();

        foreach (var current in engines)
        {
            if (current?.engine is null)
                continue;

            var engine = current.engine;
            var engineThrottle = module.persistentThrottle * engine.thrustPercentage * 0.01f;
            var thrust = engineThrottle * engine.maxThrust;
            var demandMass = 0.0;
            if (current.persistentIsp > 0)
            {
                double massFlowRate =
                    engine.currentThrottle
                    * engine.maxThrust
                    / (current.persistentIsp * PhysicsGlobals.GravitationalAcceleration);

                demandMass = massFlowRate / current.averageDensity;
            }
            else
            {
                continue;
            }

            var inputs = new List<ResourceRatio>(current.propellants.Count);
            var outputs = new List<ResourceRatio>();

            for (int i = 0; i < current.propellants.Count; ++i)
            {
                var propellant = current.propellants[i];

                inputs.Add(
                    new ResourceRatio
                    {
                        ResourceName = propellant.propellant.name,
                        Ratio = demandMass * propellant.normalizedRatio,
                        FlowMode = propellant.propellant.GetFlowMode(),
                    }
                );
            }

            foreach (var alternator in alternators)
            {
                var alternatorEngine = AlternatorEngineField.GetValue(alternator);
                if (!ReferenceEquals(alternatorEngine, current.engine))
                    continue;

                foreach (var resource in alternator.resHandler.inputResources)
                {
                    inputs.Add(
                        new ResourceRatio
                        {
                            ResourceName = resource.name,
                            Ratio = resource.rate * engineThrottle,
                            FlowMode = resource.flowMode,
                        }
                    );
                }

                foreach (var resource in alternator.resHandler.outputResources)
                {
                    outputs.Add(
                        new ResourceRatio
                        {
                            ResourceName = resource.name,
                            Ratio = resource.rate * engineThrottle,
                            FlowMode = resource.flowMode,
                            DumpExcess = true,
                        }
                    );
                }
            }

            behaviour.Add(new PersistentEngineBehaviour(inputs, outputs, []) { Thrust = thrust });
        }

        return behaviour;
    }
}
