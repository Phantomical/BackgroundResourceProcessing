using System.Collections.Generic;
using System.Collections.ObjectModel;
using BackgroundResourceProcessing.Core;
using BackgroundResourceProcessing.Utils;

namespace BackgroundResourceProcessing;

/// <summary>
/// A simulator for the background processing state of vessel.
/// </summary>
///
/// <remarks>
///   This simulator allows you to step the state of a vessel forward into
///   the future in order to determine what the future state of inventories
///   and converters.
/// </remarks>
public class ResourceSimulator
{
    readonly ResourceProcessor processor;

    /// <summary>
    /// A limit on the number of iterations that this simulator will perform
    /// before it refuses to perfom any more.
    /// </summary>
    ///
    /// <remarks>
    /// This is mostly here to act as a safeguard against infinite loops in
    /// consumers of the API. Normally, vessels should converge to a steady
    /// after a finite (and generally low) number of steps. However, solver
    /// bugs and numerical imprecision can result in a vessel that
    /// </remarks>
    public int IterationLimit = 100;
    private int iteration = 0;

    /// <summary>
    /// The time at which this simulator was last updated.
    /// </summary>
    public double LastUpdate => processor.lastUpdate;

    /// <summary>
    /// The next changepoint for this simulator.
    /// </summary>
    public double NextChangepoint => processor.nextChangepoint;

    /// <summary>
    /// Get a read-only view of the available inventories.
    /// </summary>
    ///
    /// <remarks>
    /// Note that you can still modify the inventories themselves. You just
    /// cannot add or remove inventories from the set.
    /// </remarks>
    public ReadOnlyCollection<ResourceInventory> Inventories => processor.inventories.AsReadOnly();

    /// <summary>
    /// Get a read-only view of the available converters.
    /// </summary>
    ///
    /// <remarks>
    /// Note that you can still modify the inventories themselves. You just
    /// cannot add or remove inventories from the set.
    /// </remarks>
    public ReadOnlyCollection<Core.ResourceConverter> Converters =>
        processor.converters.AsReadOnly();

    internal ResourceSimulator(ResourceProcessor processor)
    {
        this.processor = processor;
    }

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
    /// Get the overall state for a resource at the current point in time.
    /// </summary>
    /// <returns>
    ///   An <c><see cref="InventoryState"/></c> summarizing the states of
    ///   all inventories with the requested resource.
    /// </returns>
    public InventoryState GetResourceState(string resourceName)
    {
        InventoryState state = new();
        foreach (var inventory in processor.inventories)
        {
            if (inventory.resourceName != resourceName)
                continue;

            state = state.Merge(inventory.State);
        }

        return state;
    }

    /// <summary>
    /// Get the overall state for all stored resources at the current step.
    /// </summary>
    public Dictionary<string, InventoryState> GetResourceStates()
    {
        Dictionary<string, InventoryState> states = [];
        foreach (var inventory in processor.inventories)
        {
            if (!states.TryGetValue(inventory.resourceName, out var state))
                state = new();

            states[inventory.resourceName] = state.Merge(inventory.State);
        }
        return states;
    }

    /// <summary>
    /// Step the simulator forward to the next changepoint.
    /// </summary>
    ///
    /// <returns><c>true</c> if the simulation was stepped forward.</returns>
    public bool Step()
    {
        if (!MathUtil.IsFinite(NextChangepoint))
            return false;
        if (iteration >= IterationLimit)
            return false;

        var currentTime = NextChangepoint;
        processor.UpdateState(currentTime, false);
        processor.ComputeRates();
        processor.UpdateNextChangepoint(currentTime);
        iteration += 1;

        return true;
    }
}
