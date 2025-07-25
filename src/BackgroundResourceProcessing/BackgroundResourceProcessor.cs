using System.Collections.Generic;
using System.Collections.ObjectModel;
using BackgroundResourceProcessing.Collections;
using BackgroundResourceProcessing.Converter;
using BackgroundResourceProcessing.Core;

namespace BackgroundResourceProcessing;

/// <summary>
/// This is the core vessel module that takes care of updating vessel
/// states in the background.
/// </summary>
///
/// <remarks>
/// If you want to inspect or otherwise interact with the background state
/// of the vessel then this module is where you should start.
/// </remarks>
public sealed partial class BackgroundResourceProcessor : VesselModule
{
    private readonly ResourceProcessor processor = new();

    #region Events
    /// <summary>
    /// This event is fired when a changepoint occurs.
    /// </summary>
    ///
    /// <remarks>
    /// Specifically, it is fired after rates are computed for inventories
    /// but before the rates are used for the next changepoint. This gives
    /// you freedom to adjust the amount of resources stored in any of the
    /// inventories in the vessel.
    /// </remarks>
    public static readonly EventData<
        BackgroundResourceProcessor,
        ChangepointEvent
    > onVesselChangepoint = new("onVesselChangepoint");

    /// <summary>
    /// This event is fired just after a vessel is recorded.
    /// </summary>
    ///
    /// <remarks>
    /// This is meant for cases where you want need to create new converters
    /// or inventories that don't actually correspond to a part module.
    /// </remarks>
    public static readonly EventData<BackgroundResourceProcessor> onVesselRecord = new(
        "onVesselRecord"
    );

    /// <summary>
    /// This event is fired just after a vessel is restored.
    /// </summary>
    public static readonly EventData<BackgroundResourceProcessor> onVesselRestore = new(
        "onVesselRestore"
    );
    #endregion

    /// <summary>
    /// The current the vessel WRT to being in the planet's shadow.
    /// </summary>
    public ShadowState? ShadowState { get; private set; } = null;

    // This is the actual API that is meant to be used by other code for
    // interacting with this module.
    #region Public API
    /// <summary>
    /// Get a read-only view of the available inventories.
    /// </summary>
    ///
    /// <remarks>
    /// Note that you can still modify the inventories themselves. You just
    /// cannot add or remove inventories from the set.
    /// </remarks>
    public ReadOnlyList<ResourceInventory> Inventories => new(processor.inventories);

    /// <summary>
    /// Get a read-only view of the available converters.
    /// </summary>
    ///
    /// <remarks>
    /// Note that you can still modify the inventories themselves. You just
    /// cannot add or remove inventories from the set.
    /// </remarks>
    public ReadOnlyList<Core.ResourceConverter> Converters => new(processor.converters);

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

    public int? GetInventoryIndex(InventoryId id)
    {
        if (processor.inventoryIds.TryGetValue(id, out var index))
            return index;
        return null;
    }

    /// <summary>
    /// Update the vessel's stored inventory state to reflect the current
    /// point in time.
    /// </summary>
    ///
    /// <remarks>
    /// This does nothing if the vessel is currently active. It is also not
    /// necessary to call this during an <see cref="onVesselChangepoint"/>
    /// callback, as it will already have been applied during the callback.
    /// </remarks>
    public void UpdateBackgroundState()
    {
        if (Vessel.loaded)
            return;
        var now = Planetarium.GetUniversalTime();
        if (processor.lastUpdate >= now)
            return;

        processor.UpdateState(now, true);
    }

    /// <summary>
    /// Get a simulator that allows you to model the resource state of the
    /// vessel into the future.
    /// </summary>
    ///
    /// <remarks>
    /// The simulator will <b>not</b> update the behaviours on the ship, so
    /// this will not be an exact representation of what will happen.
    /// </remarks>
    public ResourceSimulator GetSimulator()
    {
        var currentTime = Planetarium.GetUniversalTime();

        if (vessel.loaded)
            SaveVessel();

        var clone = processor.CloneForSimulator();
        clone.UpdateState(currentTime, false);
        clone.UpdateNextChangepoint(currentTime);
        return new(clone);
    }

    /// <summary>
    /// Get a summary of the total resources currently stored within the
    /// vessel.
    /// </summary>
    /// <returns></returns>
    public Dictionary<string, InventoryState> GetResourceStates()
    {
        return processor.GetResourceStates();
    }

    /// <summary>
    /// Get a summary for a single resource.
    /// </summary>
    /// <param name="resourceName"></param>
    /// <returns></returns>
    public InventoryState GetResourceState(string resourceName)
    {
        InventoryState state = default;
        foreach (var inventory in processor.inventories)
        {
            if (inventory.resourceName == resourceName)
                state = state.Merge(inventory.State);
        }
        return state;
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
    public int AddConverter(Core.ResourceConverter converter)
    {
        converter.Behaviour.Vessel = Vessel;

        var index = processor.converters.Count;
        processor.converters.Add(converter);
        return index;
    }
    #endregion

    #region Internal API
    /// <summary>
    /// Find all background processing modules and resources on this vessel
    /// and update the module state accordingly.
    /// </summary>
    ///
    /// <remarks>
    /// The only reason this isn't private is so that the debug UI can use
    /// it to prepare the module for dumping.
    /// </remarks>
    internal void DebugRecordVesselState()
    {
        SaveVessel();
    }
    #endregion
}
