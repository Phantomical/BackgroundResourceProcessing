using BackgroundResourceProcessing.Behaviour;
using BackgroundResourceProcessing.Collections;
using Tac;
using UnityEngine;

namespace BackgroundResourceProcessing.Integration.TACLifeSupport
{
    /// <summary>
    /// Results from TAC-LS life support simulation
    /// </summary>
    public struct VesselStats
    {
        public double FoodExhaustedUT;
        public double WaterExhaustedUT;
        public double OxygenExhaustedUT;
        public double ElectricityExhaustedUT;
    }

    /// <summary>
    /// Simulation cache for TAC-LS life support calculations
    /// </summary>
    public class VesselSimulationCache : SimulationCache<VesselStats>
    {
        static VesselSimulationCache Instance = null;

        /// <summary>
        /// Create a cache with default options
        /// </summary>
        public VesselSimulationCache()
            : base(new Options()) { }

        public static VesselSimulationCache GetInstance()
        {
            if (Instance != null)
                return Instance;

            var gameObject = new GameObject("BRP TAC-LS Vessel Simulation Cache");
            return gameObject.AddComponent<VesselSimulationCache>();
        }

        public VesselStats? GetCachedVesselStats(Vessel vessel, VesselInfo info)
        {
            if (info.numCrew == 0)
                return null;

            return GetVesselEntry(vessel, processor => SimulateVessel(processor, info));
        }

        static VesselStats SimulateVessel(BackgroundResourceProcessor processor, VesselInfo info)
        {
            var vessel = processor.Vessel;
            var global = TacStartOnce.Instance.globalSettings;

            // Get resource simulator from processor
            var simulator = processor.GetSimulator();
            var vesselState = processor.GetVesselState();

            var options = new AddConverterOptions { LinkToAll = true };
            var converters = new Converters(vessel, info);

            simulator.AddConverter(converters.ec, vesselState, options);
            if (converters.food is not null)
                simulator.AddConverter(converters.food, vesselState, options);
            if (converters.water is not null)
                simulator.AddConverter(converters.water, vesselState, options);
            if (converters.oxygen is not null)
                simulator.AddConverter(converters.oxygen, vesselState, options);

            var stats = new VesselStats
            {
                FoodExhaustedUT = double.PositiveInfinity,
                WaterExhaustedUT = double.PositiveInfinity,
                OxygenExhaustedUT = double.PositiveInfinity,
                ElectricityExhaustedUT = double.PositiveInfinity,
            };

            bool first = true;
            foreach (var UT in simulator.Steps())
            {
                var food = simulator.GetResourceState(global.Food);
                var water = simulator.GetResourceState(global.Water);
                var oxygen = simulator.GetResourceState(global.Oxygen);
                var ec = simulator.GetResourceState(global.Electricity);

                if (first)
                {
                    if (food.amount == 0.0)
                        stats.FoodExhaustedUT = info.estimatedTimeFoodDepleted;
                    if (water.amount == 0.0)
                        stats.WaterExhaustedUT = info.estimatedTimeWaterDepleted;
                    if (oxygen.amount == 0.0)
                        stats.OxygenExhaustedUT = info.estimatedTimeWaterDepleted;
                    if (ec.amount == 0.0)
                        stats.ElectricityExhaustedUT = info.estimatedTimeElectricityDepleted;

                    first = false;
                }

                if (food.amount <= 0 && stats.FoodExhaustedUT == double.PositiveInfinity)
                    stats.FoodExhaustedUT = UT;
                if (water.amount <= 0 && stats.WaterExhaustedUT == double.PositiveInfinity)
                    stats.WaterExhaustedUT = UT;
                if (oxygen.amount <= 0 && stats.OxygenExhaustedUT == double.PositiveInfinity)
                    stats.OxygenExhaustedUT = UT;
                if (ec.amount <= 0 && stats.ElectricityExhaustedUT == double.PositiveInfinity)
                    stats.ElectricityExhaustedUT = UT;

                // Stop if all resources are exhausted
                if (
                    stats.FoodExhaustedUT != double.PositiveInfinity
                    && stats.WaterExhaustedUT != double.PositiveInfinity
                    && stats.OxygenExhaustedUT != double.PositiveInfinity
                    && stats.ElectricityExhaustedUT != double.PositiveInfinity
                )
                {
                    break;
                }
            }

            return stats;
        }

        private new void Awake()
        {
            base.Awake();
            Instance ??= this;
        }

        private new void OnDestroy()
        {
            base.OnDestroy();
            if (ReferenceEquals(Instance, this))
                Instance = null;
        }

        struct Converters
        {
            public Core.ResourceConverter food = null;
            public Core.ResourceConverter water = null;
            public Core.ResourceConverter oxygen = null;
            public Core.ResourceConverter ec = null;

            public Converters(Vessel vessel, VesselInfo info)
            {
                var global = TacStartOnce.Instance.globalSettings;
                var numCrew = info.numCrew;

                if (vessel.situation != Vessel.Situations.PRELAUNCH)
                {
                    food = new Core.ResourceConverter(GetFoodConverter(numCrew));
                    water = new Core.ResourceConverter(GetWaterConverter(numCrew));
                    oxygen = new Core.ResourceConverter(GetOxygenConverter(numCrew));
                }

                ec = new Core.ResourceConverter(
                    new ConstantConsumer(
                        [
                            new()
                            {
                                ResourceName = global.Electricity,
                                Ratio = info.estimatedElectricityConsumptionRate,
                                FlowMode = ResourceFlowMode.ALL_VESSEL,
                            },
                        ]
                    )
                );
            }

            private static ConstantConverter GetFoodConverter(int numCrew)
            {
                var global = TacStartOnce.Instance.globalSettings;
                var settings =
                    HighLogic.CurrentGame.Parameters.CustomParams<TAC_SettingsParms_Sec2>();

                return new ConstantConverter()
                {
                    Inputs =
                    [
                        new()
                        {
                            ResourceName = global.Food,
                            Ratio = settings.FoodConsumptionRate * numCrew,
                            FlowMode = ResourceFlowMode.ALL_VESSEL,
                        },
                    ],
                    Outputs =
                    [
                        new()
                        {
                            ResourceName = global.Waste,
                            Ratio = global.WasteProductionRate * numCrew,
                            FlowMode = ResourceFlowMode.ALL_VESSEL,
                            DumpExcess = true,
                        },
                    ],
                };
            }

            private static ConstantConverter GetWaterConverter(int numCrew)
            {
                var global = TacStartOnce.Instance.globalSettings;
                var settings =
                    HighLogic.CurrentGame.Parameters.CustomParams<TAC_SettingsParms_Sec2>();

                return new ConstantConverter()
                {
                    Inputs =
                    [
                        new()
                        {
                            ResourceName = global.Water,
                            Ratio = settings.WaterConsumptionRate * numCrew,
                            FlowMode = ResourceFlowMode.ALL_VESSEL,
                        },
                    ],
                    Outputs =
                    [
                        new()
                        {
                            ResourceName = global.WasteWater,
                            Ratio = settings.WasteWaterProductionRate * numCrew,
                            FlowMode = ResourceFlowMode.ALL_VESSEL,
                            DumpExcess = true,
                        },
                    ],
                };
            }

            private static ConstantConverter GetOxygenConverter(int numCrew)
            {
                var global = TacStartOnce.Instance.globalSettings;
                var settings =
                    HighLogic.CurrentGame.Parameters.CustomParams<TAC_SettingsParms_Sec2>();

                return new ConstantConverter()
                {
                    Inputs =
                    [
                        new()
                        {
                            ResourceName = global.Oxygen,
                            Ratio = settings.OxygenConsumptionRate * numCrew,
                            FlowMode = ResourceFlowMode.ALL_VESSEL,
                        },
                    ],
                    Outputs =
                    [
                        new()
                        {
                            ResourceName = global.CO2,
                            Ratio = settings.CO2ProductionRate * numCrew,
                            FlowMode = ResourceFlowMode.ALL_VESSEL,
                            DumpExcess = true,
                        },
                    ],
                };
            }
        }
    }
}
