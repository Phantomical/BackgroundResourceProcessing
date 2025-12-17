using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using BackgroundResourceProcessing.Collections;
using BackgroundResourceProcessing.Converter;
using BackgroundResourceProcessing.Core;
using BackgroundResourceProcessing.Utils;

namespace BackgroundResourceProcessing;

/// <summary>
/// A standalone resource processor, not attached to any KSP game object.
/// </summary>
///
/// <remarks>
/// Note that you won't necessarily be able to use the usual behaviours with
/// this. Most behaviour
/// </remarks>
public class StandaloneResourceProcessor : IResourceProcessor
{
    private ResourceProcessor processor;

    private bool ImmediateChangepointRequested = false;

    #region Properties
    /// <summary>
    /// Get a read-only view of the available inventories.
    /// </summary>
    ///
    /// <remarks>
    /// Note that you can still modify the inventories themselves. You just
    /// cannot add or remove inventories from the set.
    /// </remarks>
    public StableListView<ResourceInventory> Inventories => processor.inventories.AsView();

    /// <summary>
    /// Get a read-only view of the available converters.
    /// </summary>
    ///
    /// <remarks>
    /// Note that you can still modify the inventories themselves. You just
    /// cannot add or remove inventories from the set.
    /// </remarks>
    public StableListView<Core.ResourceConverter> Converters => processor.converters.AsView();

    /// <summary>
    /// The time at which the rates for this processor are next expected to
    /// change.
    /// </summary>
    public double NextChangepoint => processor.nextChangepoint;

    /// <summary>
    /// The last time that this processor was updated.
    /// </summary>
    public double LastChangepoint => processor.lastUpdate;

    /// <summary>
    /// The current shadow state for this resource processor. This is used to compute
    /// power and changepoint times for solar panels.
    /// </summary>
    public ShadowState ShadowState { get; set; } = new ShadowState(double.PositiveInfinity, false);
    #endregion

    #region Events
    /// <summary>
    /// This event is fired when a changepoint occurs.
    /// </summary>
    public event Action<StandaloneResourceProcessor> OnChangepoint;

    /// <summary>
    /// This event fires after a changepoint completes, after the next
    /// changepoint time has been computed.
    /// </summary>
    public event Action<StandaloneResourceProcessor> OnAfterChangepoint;

    /// <summary>
    /// This event is fired just after the stte is update but before any
    /// changepoint-related computations are done, if any.
    /// </summary>
    ///
    /// <remarks>
    /// This allows you to look at the old rates on the inventories and
    /// converters and use that. This is useful if you are tracking, for example,
    /// total uptime of a converter.
    /// </remarks>
    public event Action<StandaloneResourceProcessor, ChangepointEvent> OnStateUpdate;
    #endregion

    /// <summary>
    /// Get an inventory directly from its <c><see cref="InventoryId"/></c>.
    /// </summary>
    /// <returns>
    /// The <c><see cref="ResourceInventory"/></c>, or <c>null</c> if there is
    /// no resource inventory with the requested id.
    /// </returns>
    public ResourceInventory GetInventoryById(InventoryId id)
    {
        if (!processor.inventoryIds.TryGetValue(id, out var index))
            return null;

        return processor.inventories[index];
    }

    /// <summary>
    /// Get an inventory from its index. This is somewhat faster than using
    /// <see cref="Inventories" /> as it does not require an allocation.
    /// </summary>
    /// <param name="index"></param>
    /// <returns>
    /// The <see cref="ResourceInventory"/>, or <c>null</c> if the index is
    /// out of bounds.
    /// </returns>
    public ResourceInventory GetInventory(int index)
    {
        if (index < 0 || index >= processor.inventories.Count)
            return null;

        return processor.inventories[index];
    }

    /// <summary>
    /// Get the index of the inventory with the requested id, or null if there
    /// is no such inventory within this resource processor.
    /// </summary>
    /// <param name="id"></param>
    /// <returns></returns>
    public int? GetInventoryIndex(InventoryId id)
    {
        if (processor.inventoryIds.TryGetValue(id, out var index))
            return index;
        return null;
    }

    /// <summary>
    /// Get a summary for a single resource.
    /// </summary>
    public InventoryState GetResourceState(string resourceName) =>
        GetResourceState(resourceName.GetHashCode());

    /// <summary>
    /// Get a summary for a single resource.
    /// </summary>
    public InventoryState GetResourceState(int resourceId) =>
        IResourceProcessorDefaults.GetResourceState(this, resourceId);

    /// <summary>
    /// Get a summary of the total resources currently stored within the
    /// vessel.
    /// </summary>
    /// <returns></returns>
    public Dictionary<string, InventoryState> GetResourceStates() => processor.GetResourceStates();

    /// <summary>
    /// Get the total amount of wet mass that is stored within the simulation,
    /// along with its rate and maximum storable wet mass.
    /// </summary>
    ///
    /// <remarks>
    /// Due to how BRP ends up representing asteroid mass, this can actually
    /// end up being negative if the mass removed from the asteroid outweighs
    /// all other resources.
    /// </remarks>
    public InventoryState GetWetMass() => IResourceProcessorDefaults.GetWetMass(this);

    public void SuppressNoProgressError()
    {
        ImmediateChangepointRequested = true;
    }

    /// <summary>
    /// Add a new converter that doesn't correspond to any part modules on the
    /// vessel.
    /// </summary>
    /// <returns>The index of the converter within <see cref="Converters"/>.</returns>
    ///
    /// <remarks>
    /// <para>
    /// This is meant to allow integrating external resource consumers/producers.
    /// If you can tie it to a specific part module you are much better off using
    /// a <see cref="BackgroundConverter"/> instead.
    /// </para>
    ///
    /// <para>
    /// Note that the sets of connected inventories will not be automatically
    /// initialized for this converter. You will have to do so yourself.
    /// </para>
    /// </remarks>
    public int AddConverter(Core.ResourceConverter converter) =>
        AddConverter(converter, new AddConverterOptions());

    /// <summary>
    /// Add a new converter that doesn't correspond to any part modules on the
    /// vessel.
    /// </summary>
    /// <returns>The index of the converter within <see cref="Converters"/>.</returns>
    ///
    /// <remarks>
    /// <para>
    /// This is meant to allow integrating external resource consumers/producers.
    /// If you can tie it to a specific part module you are much better off using
    /// a <see cref="BackgroundConverter"/> instead.
    /// </para>
    ///
    /// <para>
    /// Note that the sets of connected inventories will not be automatically
    /// initialized for this converter. You will have to do so yourself.
    /// </para>
    /// </remarks>
    public int AddConverter(Core.ResourceConverter converter, AddConverterOptions options)
    {
        if (options.LinkToAll)
        {
            converter.Refresh(GetVesselState());

            var count = Inventories.Count;
            for (int i = count - 1; i >= 0; --i)
            {
                var inventory = Inventories[i];
                if (inventory.ModuleId is not null)
                    continue;

                if (converter.Inputs.ContainsKey(inventory.ResourceId))
                    converter.Pull.Add(i);
                if (converter.Outputs.ContainsKey(inventory.ResourceId))
                    converter.Push.Add(i);
                if (converter.Required.ContainsKey(inventory.ResourceId))
                    converter.Constraint.Add(i);
            }
        }

        var index = processor.converters.Count;
        processor.converters.Add(converter);
        processor.UpdateConstraintState(converter);
        return index;
    }

    /// <summary>
    /// Remove the resource converter present at <paramref name="index"/>.
    /// </summary>
    /// <param name="index">The converter index.</param>
    ///
    /// <remarks>
    /// This will not change the indices of any other converters contained
    /// within this <see cref="BackgroundResourceProcessor"/>. However, the
    /// slot will be reused if more converters are added in the future.
    /// </remarks>
    public void RemoveConverter(int index)
    {
        processor.converters.RemoveAt(index);
    }

    /// <summary>
    /// Add a new inventory.
    /// </summary>
    /// <param name="inventory">The inventory to add.</param>
    /// <returns>The index of the inventory within <see cref="Inventories"/></returns>
    ///
    /// <remarks>
    /// This allows you to add custom inventories that don't correspond to any
    /// existing part or module on the vessel. However, you will need to
    /// manually connect any other converters to the inventory.
    /// </remarks>
    public int AddInventory(Core.ResourceInventory inventory)
    {
        return processor.inventories.Add(inventory);
    }

    /// <summary>
    /// Remove an existing inventory.
    /// </summary>
    /// <param name="index">The index of the inventory to remove.</param>
    /// <remarks>
    /// This will not change the index of any other inventories contained within
    /// this <see cref="BackgroundResourceProcessor"/>. However, the index may
    /// be reused when inventories are added in the future.
    /// </remarks>
    public void RemoveInventory(int index)
    {
        foreach (var converter in Converters)
        {
            converter.Pull?[index] = false;
            converter.Push?[index] = false;
            converter.Constraint?[index] = false;
        }

        processor.inventories.RemoveAt(index);
    }

    /// <summary>
    /// Add a resource to the inventories within this vessel. This does nothing
    /// if the vessel is currently loaded.
    /// </summary>
    /// <param name="resourceName">The name of the resource to add.</param>
    /// <param name="amount">
    ///   How much resource should be added. This can be positive or negative
    ///   infinity in order to fill/empty all inventories.
    /// </param>
    /// <param name="includeModuleInventories">
    ///   Whether to add resources to inventories associated with part modules.
    ///   This is <c>false</c> by default.
    /// </param>
    /// <returns>
    ///   The amount of resource that was actually added. If <paramref name="amount"/>
    ///   was negative then this will be negative (or zero) as well.
    /// </returns>
    ///
    /// <remarks>
    /// Be careful when using infinite amounts combined with
    /// <c><paramref name="includeModuleInventories"/> = true</c>. It is valid
    /// for background inventories to have an infinite <c>maxAmount</c> but
    /// synchronizing an infinite value back into the <c>PartModule</c> it
    /// represents may result in issues.
    /// </remarks>
    public double AddResource(
        string resourceName,
        double amount,
        bool includeModuleInventories = false
    )
    {
        if (double.IsNaN(amount))
            throw new ArgumentException("amount added was NaN", nameof(amount));

        if (amount == 0.0)
            return 0.0;

        var resourceId = resourceName.GetHashCode();
        var added = AddResourceImpl(resourceName, resourceId, amount, includeModuleInventories);

        return added;
    }

    /// <summary>
    /// Remove a resource from inventories within this vessel. This does nothing
    /// if the vessel is currently loaded.
    /// </summary>
    /// <param name="resourceName">The name of the resource to remove.</param>
    /// <param name="amount">
    ///   How much resource should be removed. This be infinite in order to
    ///   empty all inventories.
    /// </param>
    /// <param name="includeModuleInventories">
    ///   Whether to add resources to inventories associated with part modules.
    ///   This is <c>false</c> by default.
    /// </param>
    /// <returns>
    ///   The amount of resource that was actually removed. If <paramref name="amount"/>
    ///   was negative then this will be negative (or zero) as well.
    /// </returns>
    public double RemoveResource(
        string resourceName,
        double amount,
        bool includeModuleInventories = false
    )
    {
        return -AddResource(resourceName, -amount, includeModuleInventories);
    }

    private double AddResourceImpl(
        string resourceName,
        int resourceId,
        double amount,
        bool includeModuleInventories
    )
    {
        double total = 0.0;
        var inventories = GetResourceEnumerator(resourceId, includeModuleInventories);

        if (double.IsInfinity(amount))
        {
            if (amount > 0)
            {
                foreach (var inventory in inventories)
                {
                    total += inventory.Available;
                    inventory.Amount = inventory.MaxAmount;
                }
            }
            else
            {
                foreach (var inventory in inventories)
                {
                    total -= inventory.Amount;
                    inventory.Amount = 0.0;
                }
            }

            return total;
        }

        double available = 0.0;
        if (amount < 0.0)
        {
            foreach (var inventory in inventories)
                available += inventory.Amount;
        }
        else
        {
            foreach (var inventory in inventories)
                available += inventory.Available;
        }

        if (available == 0.0)
            return 0.0;

        if (double.IsNaN(amount))
            throw new Exception($"total stored amount for resource `{resourceName}` was NaN");

        if (Math.Abs(amount) >= available)
        {
            if (amount < 0.0)
            {
                foreach (var inventory in inventories)
                    inventory.Amount = 0.0;

                return -available;
            }
            else
            {
                foreach (var inventory in inventories)
                    inventory.Amount = inventory.MaxAmount;

                return available;
            }
        }

        if (double.IsInfinity(available))
        {
            foreach (var inventory in inventories)
            {
                if (double.IsInfinity(inventory.MaxAmount))
                {
                    inventory.Amount += amount;
                    break;
                }
            }
        }
        else
        {
            foreach (var inventory in inventories)
            {
                var frac = inventory.Available / available;
                inventory.Amount += frac * amount;
            }
        }

        return amount;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ResourceInventoryEnumerator GetResourceEnumerator(
        int resourceId,
        bool includeModuleInventories = false
    )
    {
        return new ResourceInventoryEnumerator(this, resourceId, includeModuleInventories);
    }

    VesselState GetVesselState()
    {
        return new VesselState(LastChangepoint) { };
    }
}
