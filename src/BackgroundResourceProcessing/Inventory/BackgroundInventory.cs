using System;
using System.Collections.Generic;
using BackgroundResourceProcessing.Core;
using BackgroundResourceProcessing.Utils;

namespace BackgroundResourceProcessing.Inventory;

/// <summary>
/// A fake <see cref="PartResource"/> that is only present for background
/// processing. See the documentation on <see cref="BackgroundInventory"/>
/// for details on how this is meant to be used.
/// </summary>
public class FakePartResource
{
    /// <summary>
    /// The name of the resource that is being stored within this fake
    /// inventory.
    /// </summary>
    public string ResourceName;

    /// <summary>
    /// The amount of resource that is stored in this inventory.
    /// </summary>
    public double Amount = 0.0;

    /// <summary>
    /// The maximum amount of resource that can be stored in this inventory.
    /// </summary>
    ///
    /// <remarks>
    /// This is permitted to be infinite, but negative or NaN values will
    /// result in this inventory being ignored.
    /// </remarks>
    public double MaxAmount = 0.0;
}

public struct SnapshotUpdate
{
    /// <summary>
    /// The last time that this inventory was updated.
    /// </summary>
    public double LastUpdate;

    /// <summary>
    /// The current time at which this update is being performed.
    /// </summary>
    public double CurrentTime;

    /// <summary>
    /// The net change to the amount of resource stored in the inventory.
    /// </summary>
    public double Delta;
}

/// <summary>
/// An adapter for a part module that defines a "fake" resource inventory
/// related to that module.s
/// </summary>
///
/// <remarks>
/// This is meant to used as a bridge between things that aren't technically
/// resources and the resource that they are modelled as within the solver.
/// </remarks>
public abstract class BackgroundInventory : IRegistryItem
{
    private static readonly TypeRegistry<BackgroundInventory> registry = new(
        "BACKGROUND_INVENTORY"
    );

    private readonly BaseFieldList fields;

    /// <summary>
    /// Whether the contents of this inventory are already included in the dry
    /// mass of the vessel. This ensures that vessel mass calculations take
    /// this into account.
    /// </summary>
    ///
    /// <remarks>
    /// The use cases for this are generally pretty niche. Only set this to true
    /// if it is adapting for an amount that is included in part mass. The only
    /// use for this in stock KSP is for asteroids and comets.
    /// </remarks>
    [KSPField]
    public bool InventoryMassIncludedInDryMass = false;

    public BackgroundInventory()
    {
        fields = new(this);
    }

    /// <summary>
    /// Get a list of <see cref="FakePartResource"/>s that are present on
    /// this part.
    /// </summary>
    ///
    /// <remarks>
    /// Each resource here will create a new inventory in the resource
    /// processor.
    /// </remarks>
    public abstract List<FakePartResource> GetResources(PartModule module);

    /// <summary>
    /// Update this resource with the new state for one of its inventories.
    /// </summary>
    ///
    /// <remarks>
    /// This method will be called once for every resource that was returned
    /// from <see cref="GetResources"/>.
    /// </remarks>
    public abstract void UpdateResource(PartModule module, ResourceInventory inventory);

    /// <summary>
    /// Apply the requested update to the inventory.
    /// </summary>
    ///
    /// <remarks>
    /// <para>
    ///   It is not necessary to implement this method at all. The only reason to
    ///   implement this is for compatibility with other mods that modify resources
    ///   in the background.
    /// </para>
    ///
    /// <para>
    ///   If you do implement this then make sure to update
    ///   <c>inventory.originalAmount</c> to reflect the new amount stored in the
    ///   inventory.
    /// </para>
    /// </remarks>
    public virtual void UpdateSnapshot(
        ProtoPartModuleSnapshot module,
        ResourceInventory inventory,
        SnapshotUpdate update
    )
    {
        inventory.Amount += update.Delta;
    }

    protected virtual void OnLoad(ConfigNode node)
    {
        fields.Load(node);
    }

    public static BackgroundInventory GetInventoryForType(Type type)
    {
        return registry.GetEntryForType(type);
    }

    public static BackgroundInventory GetInventoryForModule(PartModule module)
    {
        return GetInventoryForType(module.GetType());
    }

    public static Type GetTargetType(ConfigNode node)
    {
        return Converter.BackgroundConverter.GetTargetType(node);
    }

    internal static void LoadAll()
    {
        registry.LoadAll();
    }

    void IRegistryItem.OnLoad(ConfigNode node)
    {
        OnLoad(node);
    }
}

public abstract class BackgroundInventory<T> : BackgroundInventory
    where T : PartModule
{
    /// <summary>
    /// Get a list of <see cref="FakePartResource"/>s that are present on
    /// this part.
    /// </summary>
    ///
    /// <remarks>
    /// Each resource here will create a new inventory in the resource
    /// processor.
    /// </remarks>
    public abstract List<FakePartResource> GetResources(T module);

    /// <summary>
    /// Update this resource with the new state for one of its inventories.
    /// </summary>
    ///
    /// <remarks>
    /// This method will be called once for every resource that was returned
    /// from <c>GetResources</c>.
    /// </remarks>
    public abstract void UpdateResource(T module, ResourceInventory inventory);

    public sealed override List<FakePartResource> GetResources(PartModule module)
    {
        if (module == null)
            return GetResources((T)null);

        if (module is not T downcasted)
        {
            LogUnexpectedType(module);
            return null;
        }

        return GetResources(downcasted);
    }

    public sealed override void UpdateResource(PartModule module, ResourceInventory inventory)
    {
        if (module == null)
        {
            UpdateResource((T)null, inventory);
            return;
        }

        if (module is not T downcasted)
        {
            LogUnexpectedType(module);
            return;
        }

        UpdateResource(downcasted, inventory);
    }

    private void LogUnexpectedType(PartModule module)
    {
        LogUtil.Error(
            $"{GetType().Name}: Expected a part module derived from {typeof(T).Name} but got {module.GetType().Name} instead"
        );
    }

    protected override void OnLoad(ConfigNode node)
    {
        base.OnLoad(node);

        var target = GetTargetType(node);
        if (typeof(T).IsAssignableFrom(target))
            return;

        LogUtil.Error(
            $"{GetType().Name}: Adapter expected a type assignable from {typeof(T).Name} but {target.Name} is not"
        );
    }
}
