namespace BackgroundResourceProcessing.Core
{
    public struct InventoryId(uint partId, string resourceName)
    {
        public uint partId = partId;
        public string resourceName = resourceName;

        public InventoryId(PartResource resource)
            : this(resource.part.persistentId, resource.resourceName) { }

        public void Load(ConfigNode node)
        {
            node.TryGetValue("partId", ref partId);
            node.TryGetValue("resourceName", ref resourceName);
        }

        public void Save(ConfigNode node)
        {
            node.AddValue("partId", partId);
            node.AddValue("resourceName", resourceName);
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

        public InventoryId Id => new(partId, resourceName);

        public ResourceInventory() { }

        public ResourceInventory(PartResource resource)
        {
            var part = resource.part;

            partId = part.persistentId;
            resourceName = resource.resourceName;
            amount = resource.amount;
            maxAmount = resource.maxAmount;
        }

        public void Save(ConfigNode node)
        {
            node.AddValue("partId", partId);
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
            node.TryGetValue("maxAmount", ref maxAmount);
            node.TryGetValue("rate", ref rate);
        }
    }
}
