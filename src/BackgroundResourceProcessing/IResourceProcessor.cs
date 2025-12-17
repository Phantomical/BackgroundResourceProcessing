using System.Collections.Generic;
using BackgroundResourceProcessing.Collections;
using BackgroundResourceProcessing.Core;

namespace BackgroundResourceProcessing;

/// <summary>
/// The common set of accessors for a background resource processor.
/// </summary>
public interface IResourceProcessor
{
    /// <summary>
    /// Get a read-only view of the available inventories.
    /// </summary>
    ///
    /// <remarks>
    /// Note that you can still modify the inventories themselves. You just
    /// cannot add or remove inventories from the set.
    /// </remarks>
    public StableListView<ResourceInventory> Inventories { get; }

    /// <summary>
    /// Get a read-only view of the available converters.
    /// </summary>
    ///
    /// <remarks>
    /// Note that you can still modify the inventories themselves. You just
    /// cannot add or remove inventories from the set.
    /// </remarks>
    public StableListView<Core.ResourceConverter> Converters { get; }

    /// <summary>
    /// The time at which the rates for this processor are next expected to
    /// change.
    /// </summary>
    public double NextChangepoint { get; }

    /// <summary>
    /// The last time that this processor was updated.
    /// </summary>
    public double LastChangepoint { get; }

    /// <summary>
    /// Get an inventory directly from its <c><see cref="InventoryId"/></c>.
    /// </summary>
    /// <returns>
    /// The <c><see cref="ResourceInventory"/></c>, or <c>null</c> if there is
    /// no resource inventory with the requested id.
    /// </returns>
    public ResourceInventory GetInventoryById(InventoryId id);

    /// <summary>
    /// Get an inventory from its index. This is somewhat faster than using
    /// <see cref="Inventories" /> as it does not require an allocation.
    /// </summary>
    /// <param name="index"></param>
    /// <returns>
    /// The <see cref="ResourceInventory"/>, or <c>null</c> if the index is
    /// out of bounds.
    /// </returns>
    public ResourceInventory GetInventory(int index);

    /// <summary>
    /// Get the index of the inventory with the requested id, or null if there
    /// is no such inventory within this resource processor.
    /// </summary>
    /// <param name="id"></param>
    /// <returns></returns>
    public int? GetInventoryIndex(InventoryId id);

    /// <summary>
    /// Suppress the error that no progress has been made with the current
    /// changepoint.
    /// </summary>
    ///
    /// <remarks>
    /// This does nothing if called outside of evaluation of a changepoint.
    /// It is provided as an escape hatch in case you end up modifying behaviours
    /// in an <see cref="BackgroundResourceProcessor.onVesselChangepoint"/> callback.
    /// </remarks>
    public void SuppressNoProgressError();

    /// <summary>
    /// Get a summary of the total resources currently stored within the
    /// vessel.
    /// </summary>
    /// <returns></returns>
    public Dictionary<string, InventoryState> GetResourceStates();

    /// <summary>
    /// Get a summary for a single resource.
    /// </summary>
    /// <param name="resourceName"></param>
    /// <returns></returns>
    public InventoryState GetResourceState(string resourceName);

    /// <summary>
    /// Get a summary for a single resource.
    /// </summary>
    public InventoryState GetResourceState(int resourceId);

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
    public InventoryState GetWetMass();
}

internal static class IResourceProcessorDefaults
{
    internal static InventoryState GetResourceState(IResourceProcessor processor, int resourceId)
    {
        InventoryState state = default;
        foreach (var inventory in processor.Inventories)
        {
            if (inventory.ResourceId == resourceId)
                state = state.Merge(inventory.State);
        }
        return state;
    }

    internal static InventoryState GetWetMass(IResourceProcessor processor)
    {
        double mass = 0.0;
        double rate = 0.0;
        double total = 0.0;

        foreach (var inventory in processor.Inventories)
        {
            var definition = PartResourceLibrary.Instance.resourceDefinitions[inventory.ResourceId];
            if (definition == null)
                continue;

            var density = definition.density;
            var maxAmount = inventory.MaxAmount * density;

            mass += inventory.Amount * density;
            rate += inventory.Rate * density;

            // The mass for an asteroid is already included if the vessel's dry
            // mass so what we actually need to do is subtract what's missing
            // from the wet mass.
            if (inventory.MassIncludedInDryMass)
                mass -= maxAmount;
            else
                total += maxAmount;
        }

        return new()
        {
            amount = mass,
            maxAmount = total,
            rate = rate,
        };
    }
}
