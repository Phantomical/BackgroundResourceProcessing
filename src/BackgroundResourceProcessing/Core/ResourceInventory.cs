using BackgroundResourceProcessing.Utils;

namespace BackgroundResourceProcessing.Core
{
    public struct InventoryId(uint partId, string resourceName, uint? moduleId = null)
    {
        public uint partId = partId;
        public uint? moduleId = moduleId;
        public string resourceName = resourceName;

        public InventoryId(PartResource resource, uint? moduleId = null)
            : this(resource.part.persistentId, resource.resourceName, moduleId) { }

        public void Load(ConfigNode node)
        {
            node.TryGetValue("partId", ref partId);
            node.TryGetValue("resourceName", ref resourceName);

            uint moduleId = 0;
            if (node.TryGetValue("moduleId", ref moduleId))
                this.moduleId = moduleId;
        }

        public void Save(ConfigNode node)
        {
            node.AddValue("partId", partId);
            node.AddValue("resourceName", resourceName);

            if (moduleId != null)
                node.AddValue("moduleId", (uint)moduleId);
        }

        public override readonly string ToString()
        {
            if (moduleId == null)
                return $"{{resourceName={resourceName},partId={partId}}}";
            return $"{{resourceName={resourceName},partId={partId},moduleId={moduleId}}}";
        }
    }

    /// <summary>
    /// A modelled resource inventory within a part.
    /// </summary>
    public class ResourceInventory
    {
        /// <summary>
        /// The persistent part id. Used to find the part again when the
        /// vessel goes off the rails.
        /// </summary>
        public uint partId;

        /// <summary>
        /// The persistent ID of the module implementing <see cref="IBackgroundPartResource"/>.
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

        public bool Full => maxAmount - amount < 1e-6;
        public bool Empty => amount < 1e-6;

        public InventoryId Id => new(partId, resourceName, moduleId);

        public ResourceInventory() { }

        public ResourceInventory(PartResource resource)
        {
            var part = resource.part;

            partId = part.persistentId;
            resourceName = resource.resourceName;
            amount = resource.amount;
            maxAmount = resource.maxAmount;
        }

        public ResourceInventory(FakePartResource resource, PartModule module)
        {
            var part = module.part;

            partId = part.persistentId;
            moduleId = module.GetPersistentId();
            resourceName = resource.resourceName;
            amount = resource.amount;
            maxAmount = resource.maxAmount;
        }

        public void Save(ConfigNode node)
        {
            node.AddValue("partId", partId);
            if (moduleId != null)
                node.AddValue("moduleId", (uint)moduleId);
            node.AddValue("resourceName", resourceName);
            node.AddValue("amount", amount);
            node.AddValue("maxAmount", maxAmount);
            node.AddValue("rate", rate);
        }

        public void Load(ConfigNode node)
        {
            node.TryGetValue("partId", ref partId);
            node.TryGetValue("resourceName", ref resourceName);
            node.TryGetValue("amount", ref amount);
            node.TryGetDouble("maxAmount", ref maxAmount);
            node.TryGetValue("rate", ref rate);

            uint moduleId = 0;
            if (node.TryGetValue("moduleId", ref moduleId))
                this.moduleId = moduleId;
        }
    }
}
