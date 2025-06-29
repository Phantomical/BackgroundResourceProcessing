using BackgroundResourceProcessing.Inventory;
using BackgroundResourceProcessing.Solver;
using BackgroundResourceProcessing.Utils;

namespace BackgroundResourceProcessing.Core
{
    /// <summary>
    /// An ID that uniquely identifies where a resource inventory came from.
    /// </summary>
    public struct InventoryId(uint flightId, string resourceName, uint? moduleId = null)
    {
        /// <summary>
        /// The persistent ID of the part this inventory came from.
        /// </summary>
        public uint flightId = flightId;

        /// <summary>
        /// If non-null, this indicates the module that stores this inventory.
        /// </summary>
        public uint? moduleId = moduleId;

        /// <summary>
        /// The name of the resource stored in this inventory.
        /// </summary>
        public string resourceName = resourceName;

        public InventoryId(PartResource resource, uint? moduleId = null)
            : this(resource.part.flightID, resource.resourceName, moduleId) { }

        public InventoryId(PartModule module, string resourceName)
            : this(module.part.flightID, resourceName, module.GetPersistentId()) { }

        public void Load(ConfigNode node)
        {
            node.TryGetValue("flightId", ref flightId);
            node.TryGetValue("resourceName", ref resourceName);

            uint moduleId = 0;
            if (node.TryGetValue("moduleId", ref moduleId))
                this.moduleId = moduleId;
        }

        public void Save(ConfigNode node)
        {
            node.AddValue("flightId", flightId);
            node.AddValue("resourceName", resourceName);

            if (moduleId != null)
                node.AddValue("moduleId", (uint)moduleId);
        }

        public override readonly string ToString()
        {
            if (moduleId == null)
                return $"{{resourceName={resourceName},flightId={flightId}}}";
            return $"{{resourceName={resourceName},flightId={flightId},moduleId={moduleId}}}";
        }
    }

    public struct InventoryState(double amount = 0.0, double maxAmount = 0.0, double rate = 0.0)
    {
        /// <summary>
        /// The amount of resource stored in this inventory.
        /// </summary>
        public double amount = amount;

        /// <summary>
        /// The maximum amount of resource that can be stored in this inventory.
        /// </summary>
        public double maxAmount = maxAmount;

        /// <summary>
        /// The rate at which the stored resources within this inventory are
        /// changing.
        /// </summary>
        public double rate = rate;

        public InventoryState Merge(InventoryState other)
        {
            return new()
            {
                amount = amount + other.amount,
                maxAmount = maxAmount + other.maxAmount,
                rate = rate + other.rate,
            };
        }
    }

    /// <summary>
    /// A resource inventory within a part.
    /// </summary>
    ///
    /// <remarks>
    /// This all the state that is known by <see cref="BackgroundResourceProcessor"/>
    /// about any individual resource inventory.
    /// </remarks>
    public class ResourceInventory
    {
        /// <summary>
        /// The persistent part id. Used to find the part again when the
        /// vessel goes off the rails.
        /// </summary>
        public uint flightId;

        /// <summary>
        /// The persistent ID of the module, if this corresponds to a
        /// <see cref="BackgroundInventory"/>.
        /// </summary>
        public uint? moduleId;

        /// <summary>
        /// The name of the resource stored in this inventory.
        /// </summary>
        public string resourceName;

        /// <summary>
        /// How many units of resource are stored in this inventory.
        /// </summary>
        public double amount;

        /// <summary>
        /// The maximum number of units of resource that can be stored in
        /// inventory.
        /// </summary>
        public double maxAmount;

        /// <summary>
        /// The rate at which resources are currently being added or removed
        /// from this inventory.
        /// </summary>
        public double rate = 0.0;

        /// <summary>
        /// The <see cref="amount" /> value when this resource inventory was
        /// recorded.
        /// </summary>
        ///
        /// <remarks>
        /// This is used to handle cases where resources are changed in the
        /// background without BRP knowing about it.
        /// </remarks>
        public double originalAmount;

        /// <summary>
        /// A reference to the <c><see cref="ProtoPartResourceSnapshot"/></c>
        /// that this inventory corresponds to.
        /// </summary>
        ///
        /// <remarks>
        /// It is not guaranteed that this will actually be set in all conditions.
        /// Notably, fake inventory resources will not have it set.
        /// </remarks>
        public ProtoPartResourceSnapshot Snapshot { get; internal set; } = null;

        /// <summary>
        /// Is this inventory full?
        /// </summary>
        public bool Full => maxAmount - amount < ResourceProcessor.ResourceEpsilon;

        /// <summary>
        /// Is this inventory empty?
        /// </summary>
        public bool Empty => amount < ResourceProcessor.ResourceEpsilon;

        /// <summary>
        /// How much time remains until this inventory either fills up or
        /// empties at the current rate of change.
        /// </summary>
        public double RemainingTime
        {
            get
            {
                if (rate == 0.0)
                    return double.PositiveInfinity;

                if (rate < 0.0)
                    return amount / -rate;
                return (maxAmount - amount) / rate;
            }
        }

        public InventoryId Id => new(flightId, resourceName, moduleId);
        public InventoryState State => new(amount, maxAmount, rate);

        public ResourceInventory() { }

        public ResourceInventory(PartResource resource)
        {
            var part = resource.part;

            flightId = part.flightID;
            resourceName = resource.resourceName;
            amount = resource.amount;
            maxAmount = resource.maxAmount;
            originalAmount = resource.amount;
        }

        public ResourceInventory(FakePartResource resource, PartModule module)
        {
            var part = module.part;

            flightId = part.flightID;
            moduleId = module.GetPersistentId();
            resourceName = resource.resourceName;
            amount = resource.amount;
            maxAmount = resource.maxAmount;
            originalAmount = resource.amount;
        }

        public void Save(ConfigNode node)
        {
            node.AddValue("flightId", flightId);
            if (moduleId != null)
                node.AddValue("moduleId", (uint)moduleId);
            node.AddValue("resourceName", resourceName);
            node.AddValue("amount", amount);
            node.AddValue("maxAmount", maxAmount);
            node.AddValue("rate", rate);
            node.AddValue("originalAmount", originalAmount);
        }

        public void Load(ConfigNode node)
        {
            node.TryGetValue("flightId", ref flightId);

            uint moduleId = 0;
            if (node.TryGetValue("moduleId", ref moduleId))
                this.moduleId = moduleId;

            node.TryGetValue("resourceName", ref resourceName);
            node.TryGetValue("amount", ref amount);
            node.TryGetDouble("maxAmount", ref maxAmount);
            node.TryGetValue("rate", ref rate);
            node.TryGetValue("originalAmount", ref originalAmount);
        }

        internal Solver.InventoryState GetInventoryState()
        {
            var state = Solver.InventoryState.Unconstrained;

            if (Full)
                state |= Solver.InventoryState.Full;
            if (Empty)
                state |= Solver.InventoryState.Empty;

            return state;
        }

        internal ResourceInventory CloneForSimulator()
        {
            var clone = (ResourceInventory)MemberwiseClone();
            clone.Snapshot = null;
            return clone;
        }
    }
}
