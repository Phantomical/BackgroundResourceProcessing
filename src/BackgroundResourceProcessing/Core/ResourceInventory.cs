using System;
using System.Diagnostics;
using BackgroundResourceProcessing.Inventory;
using BackgroundResourceProcessing.Utils;

namespace BackgroundResourceProcessing.Core;

/// <summary>
/// An ID that uniquely identifies where a resource inventory came from.
/// </summary>
public struct InventoryId
{
    /// <summary>
    /// The persistent ID of the part this inventory came from.
    /// </summary>
    public uint FlightId;

    /// <summary>
    /// If non-null, this indicates the module that stores this inventory.
    /// </summary>
    public uint? ModuleId;

    /// <summary>
    /// The name of the resource stored in this inventory.
    /// </summary>
    public string ResourceName
    {
        readonly get { return resourceName; }
        set
        {
            ResourceId = value.GetHashCode();
            resourceName = value;
        }
    }
    private string resourceName;

    public int ResourceId { get; private set; }

    public InventoryId(uint flightId, string resourceName, uint? moduleId = null)
    {
        FlightId = flightId;
        ModuleId = moduleId;
        ResourceName = resourceName;
    }

    public InventoryId(ResourceInventory inventory)
    {
        FlightId = inventory.FlightId;
        ModuleId = inventory.ModuleId;
        resourceName = inventory.ResourceName;
        ResourceId = inventory.ResourceId;
    }

    public InventoryId(PartResource resource, uint? moduleId = null)
        : this(resource.part.flightID, resource.resourceName, moduleId) { }

    public InventoryId(PartModule module, string resourceName)
        : this(module.part.flightID, resourceName, module.GetPersistentId()) { }

    public InventoryId(Part part, string resourceName)
        : this(part.flightID, resourceName) { }

    public InventoryId(ProtoPartSnapshot part, string resourceName)
        : this(part.flightID, resourceName) { }

    public void Load(ConfigNode node)
    {
        node.TryGetValue("flightId", ref FlightId);

        string resourceName = null;
        if (node.TryGetValue("resourceName", ref resourceName))
            ResourceName = resourceName;

        uint moduleId = 0;
        if (node.TryGetValue("moduleId", ref moduleId))
            ModuleId = moduleId;
    }

    public readonly void Save(ConfigNode node)
    {
        node.AddValue("flightId", FlightId);
        node.AddValue("resourceName", ResourceName);

        if (ModuleId != null)
            node.AddValue("moduleId", (uint)ModuleId);
    }

    public override readonly string ToString()
    {
        if (ModuleId == null)
            return $"{{resourceName={ResourceName},flightId={FlightId}}}";
        return $"{{resourceName={ResourceName},flightId={FlightId},moduleId={ModuleId}}}";
    }

    public override readonly int GetHashCode()
    {
        HashCode hasher = new();
        SolverHash(ref hasher);
        return hasher.GetHashCode();
    }

    internal readonly void SolverHash(ref HashCode hasher)
    {
        hasher.Add(FlightId, ModuleId, ResourceId);
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

    /// <summary>
    /// Get the amount of resource that will be stored in this inventory after
    /// <paramref name="time"/> seconds have passed from the time this inventory
    /// state represents.
    /// </summary>
    public readonly double GetAmountAfterTime(double time)
    {
        return MathUtil.Clamp(amount + rate * time, 0.0, maxAmount);
    }

    public override readonly string ToString()
    {
        return $"{amount:G4}/{maxAmount:G4} (rate {rate:G4})";
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
    public uint FlightId;

    /// <summary>
    /// The persistent ID of the module, if this corresponds to a
    /// <see cref="BackgroundInventory"/>.
    /// </summary>
    public uint? ModuleId;

    /// <summary>
    /// The name of the resource stored in this inventory.
    /// </summary>
    public string ResourceName
    {
        get => _resourceName;
        set
        {
            ResourceId = value.GetHashCode();
            _resourceName = value;
        }
    }
    private string _resourceName;

    /// <summary>
    /// The hash code of <see cref="ResourceName"/>.
    /// </summary>
    public int ResourceId { get; private set; }

    /// <summary>
    /// How many units of resource are stored in this inventory.
    /// </summary>
    public double Amount;

    /// <summary>
    /// The maximum number of units of resource that can be stored in
    /// inventory.
    /// </summary>
    public double MaxAmount;

    /// <summary>
    /// How much space is left in this inventory.
    /// </summary>
    public double Available => MaxAmount - Amount;

    /// <summary>
    /// The rate at which resources are currently being added or removed
    /// from this inventory.
    /// </summary>
    public double Rate = 0.0;

    /// <summary>
    /// The <see cref="Amount" /> value when this resource inventory was
    /// recorded.
    /// </summary>
    ///
    /// <remarks>
    /// This is used to handle cases where resources are changed in the
    /// background without BRP knowing about it.
    /// </remarks>
    public double OriginalAmount;

    /// <summary>
    /// A reference to the <c><see cref="ProtoPartResourceSnapshot"/></c>
    /// that this inventory corresponds to.
    /// </summary>
    ///
    /// <remarks>
    /// It is not guaranteed that this will actually be set in all conditions.
    /// Notably, fake inventory resources will not have it set.
    /// </remarks>
    public ProtoPartResourceSnapshot Snapshot
    {
        get => protoSnapshot as ProtoPartResourceSnapshot;
        internal set => protoSnapshot = value;
    }

    /// <summary>
    /// A reference to the <c><see cref="ProtoPartModuleSnapshot"/></c>
    /// that this inventory corresponds to.
    /// </summary>
    ///
    /// <remarks>
    /// This will only be non-null if this inventory was created from a fake
    /// inventory.
    /// </remarks>
    public ProtoPartModuleSnapshot ModuleSnapshot
    {
        get => protoSnapshot as ProtoPartModuleSnapshot;
        internal set => protoSnapshot = value;
    }

    private object protoSnapshot = null;

    /// <summary>
    /// Is this inventory full?
    /// </summary>
    public bool Full => Available < ResourceProcessor.ResourceEpsilon;

    /// <summary>
    /// Is this inventory empty?
    /// </summary>
    public bool Empty => Amount < ResourceProcessor.ResourceEpsilon;

    /// <summary>
    /// How much time remains until this inventory either fills up or
    /// empties at the current rate of change.
    /// </summary>
    public double RemainingTime
    {
        get
        {
            if (Rate == 0.0)
                return double.PositiveInfinity;

            if (Rate < 0.0)
                return Amount / -Rate;
            return (MaxAmount - Amount) / Rate;
        }
    }

    public InventoryId Id => new(this);
    public InventoryState State => new(Amount, MaxAmount, Rate);

    public ResourceInventory() { }

    public ResourceInventory(PartResource resource)
    {
        var part = resource.part;

        FlightId = part.flightID;
        ResourceName = resource.resourceName;
        Amount = resource.amount;
        MaxAmount = resource.maxAmount;
        OriginalAmount = resource.amount;
    }

    public ResourceInventory(FakePartResource resource, PartModule module)
    {
        var part = module.part;

        FlightId = part.flightID;
        ModuleId = module.GetPersistentId();
        ResourceName = resource.ResourceName;
        Amount = resource.Amount;
        MaxAmount = resource.MaxAmount;
        OriginalAmount = resource.Amount;
    }

    public void Save(ConfigNode node)
    {
        node.AddValue("flightId", FlightId);
        if (ModuleId != null)
            node.AddValue("moduleId", (uint)ModuleId);
        node.AddValue("resourceName", ResourceName);
        node.AddValue("amount", Amount);
        node.AddValue("maxAmount", MaxAmount);
        node.AddValue("rate", Rate);
        node.AddValue("originalAmount", OriginalAmount);
    }

    public void Load(ConfigNode node)
    {
        node.TryGetValue("flightId", ref FlightId);

        uint moduleId = 0;
        if (node.TryGetValue("moduleId", ref moduleId))
            this.ModuleId = moduleId;

        string resourceName = null;
        if (node.TryGetValue("resourceName", ref resourceName))
            this.ResourceName = resourceName;
        node.TryGetValue("amount", ref Amount);
        node.TryGetDouble("maxAmount", ref MaxAmount);
        node.TryGetValue("rate", ref Rate);
        node.TryGetValue("originalAmount", ref OriginalAmount);
    }

    internal BurstSolver.InventoryState GetInventoryState()
    {
        var state = BurstSolver.InventoryState.Unconstrained;

        if (Full)
            state |= BurstSolver.InventoryState.Full;
        if (Empty)
            state |= BurstSolver.InventoryState.Empty;

        return state;
    }

    internal ResourceInventory CloneForSimulator()
    {
        var clone = (ResourceInventory)MemberwiseClone();
        clone.Snapshot = null;
        return clone;
    }

    public override string ToString()
    {
        return $"{ResourceName} {Amount:G6}/{MaxAmount:G6} (rate {Rate:G6})";
    }

    internal void SolverHash(ref HashCode hasher)
    {
        hasher.Add(FlightId, ModuleId, ResourceId, GetInventoryState());
    }
}
