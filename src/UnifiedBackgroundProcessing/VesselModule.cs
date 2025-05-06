using System;
using System.Collections.Generic;
using KSP;

/*
namespace UnifiedBackgroundProcessing
{
    /// <summary>
    /// A resource inventory that
    /// </summary>
    public struct Inventory
    {
        /// <summary>
        /// The name of the resource that is stored within this inventory.
        /// </summary>
        public string ResourceName;

        /// <summary>
        /// The amount of the resource that is currently stored within this inventory.
        /// </summary>
        public double Amount;

        /// <summary>
        /// The total amount of resource that can be stored within this inventory.
        /// </summary>
        public double Capacity;

        /// <summary>
        /// The persistent ID that uniquely identifies the part on the vessel.
        /// </summary>
        public int PartId;

        public readonly bool Full => Capacity - Amount < 1e-6;
        public readonly bool Empty => Amount < 1e-6;

        public override int GetHashCode()
        {
            return ResourceName.GetHashCode() ^ PartId.GetHashCode();
        }
    }

    /// <summary>
    /// A resource converter.
    /// </summary>
    public class Converter
    {
        /// <summary>
        /// The persistent ID that uniquely identifies the part on the vessel.
        /// </summary>
        public int PartId;

        /// <summary>
        /// The priority used to determine whether this converter will satisfy
        /// requests once the output is maxed out.
        /// </summary>
        public int Priority;

        /// <summary>
        /// An efficiency multiplier on the resource conversion rate of this converter.
        /// </summary>
        public double Efficiency;

        /// <summary>
        /// The recipe that this converter is performing.
        /// </summary>
        public ConversionRecipe Recipe;

        /// <summary>
        /// Inputs grouped by their resource name.
        /// </summary>
        public Dictionary<string, List<Inventory>> Inputs;

        /// <summary>
        /// Outputs grouped by their resource name.
        /// </summary>
        public Dictionary<string, List<Inventory>> Outputs;

        /// <summary>
        /// Requirement inventories grouped by their resource names.
        /// </summary>
        public Dictionary<string, List<Inventory>> Requirements;
    }

    /// <summary>
    /// A part which continuously produces resources for free.
    /// </summary>
    public class Producer
    {
        /// <summary>
        /// The persistent ID that uniquely identifies the part on the vessel.
        /// </summary>
        public int PartId;

        /// <summary>
        /// A list of resources that are being produced.
        ///
        /// <c>ResourceRatio.Ratio</c> indicates the rate at which the resource
        /// is being produced.
        /// </summary>
        public List<ResourceRatio> Resources;
    }

    /// <summary>
    ///
    /// </summary>
    public class Consumer { }

    /// <summary>
    /// The state for a vessel for a duration between two changepoints.
    /// </summary>
    public class VesselState
    {
        public struct ChangingInventory(Inventory inventory, double rate)
        {
            public Inventory Inventory = inventory;
            public double Rate = rate;

            public readonly double GetTimeToNextChangepoint()
            {
                if (Rate > 0.0)
                    return (Inventory.Capacity - Inventory.Amount) / Rate;
                if (Rate < 0.0)
                    return Inventory.Amount / -Rate;
                return double.PositiveInfinity;
            }

            public readonly Inventory GetInventoryAtTime(double deltaT)
            {
                var inventory = Inventory;
                inventory.Amount = Math.Min(Math.Max(Rate * deltaT, 0.0), inventory.Capacity);
                return inventory;
            }
        }

        /// <summary>
        /// Inventories that are full and will remain full until the next changepoint.
        /// </summary>
        public Dictionary<int, Inventory> FullInventories = [];

        /// <summary>
        /// Inventories that are empty and will remain empty until the next changepoint.
        /// </summary>
        public Dictionary<int, Inventory> EmptyInventories = [];

        /// <summary>
        /// Inventories that are actively changing and will continue to do so
        /// until the next changepoint.
        /// </summary>
        public Dictionary<int, ChangingInventory> ChangingInventories = [];

        public VesselState()
        {
            FullInventories = [];
            EmptyInventories = [];
            ChangingInventories = [];
        }

        public double GetTimeToNextChangepoint()
        {
            var time = double.PositiveInfinity;
            foreach (var pair in ChangingInventories)
            {
                var inventory = pair.Value;
                time = Math.Min(time, inventory.GetTimeToNextChangepoint());
            }
            return Math.Max(time, 0.0);
        }

        /// <summary>
        /// Compute the next VesselState at the next changepoint, if there is one.
        ///
        /// This does not adjust the rates in the remaining changing inventories.
        /// </summary>
        /// <returns>
        /// A new VesselState, or null if there is no future changepoint.
        /// </returns>
        public VesselState GetNextVesselStateWithoutAdjustedRates()
        {
            double deltaT = GetTimeToNextChangepoint();
            if (deltaT == double.PositiveInfinity)
                return null;

            var next = new VesselState();
            foreach (var pair in FullInventories)
                next.FullInventories.Add(pair.Key, pair.Value);
            foreach (var pair in EmptyInventories)
                next.EmptyInventories.Add(pair.Key, pair.Value);

            foreach (var pair in ChangingInventories)
            {
                var inv = pair.Value.GetInventoryAtTime(deltaT);
                if (inv.Full)
                    next.FullInventories.Add(pair.Key, inv);
                else if (inv.Empty)
                    next.EmptyInventories.Add(pair.Key, inv);
                else
                {
                    next.ChangingInventories.Add(
                        pair.Key,
                        new ChangingInventory(inv, pair.Value.Rate)
                    );
                }
            }

            return next;
        }
    }

    public class BackgroundVessel
    {
        public double CurrentTime;
        public VesselState CurrentState;

        public Dictionary<int, Converter> Converters;
    }

    // public class VesselState
    // {
    //     public Dictionary<int, Inventory> Inventories;
    //     public Dictionary<int, Converter> Converters;
    // }

    public class UBPVesselModule : VesselModule
    {
        public double LastUpdate = double.NaN;

        public Dictionary<int, Producer> Producers = [];
        public Dictionary<int, Converter> Converters = [];

        protected override void OnLoad(ConfigNode node)
        {
            base.OnLoad(node);
        }
    }
}
*/
