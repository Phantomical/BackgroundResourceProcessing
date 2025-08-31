using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using BackgroundResourceProcessing.Addons;
using BackgroundResourceProcessing.Collections;
using BackgroundResourceProcessing.Converter;
using BackgroundResourceProcessing.Core;
using BackgroundResourceProcessing.Tracing;
using Shadow = BackgroundResourceProcessing.ShadowState;

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

    private bool IsDirty = false;

    private bool ImmediateChangepointRequested = false;

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
    ///
    /// <remarks>
    /// This event usually fires before unity has a chance to call <c>Start</c>
    /// methods. You will want to subscribe to it in <c>Awake</c> instead.
    /// </remarks>
    public static readonly EventData<BackgroundResourceProcessor> onVesselRestore = new(
        "onVesselRestore"
    );
    #endregion

    /// <summary>
    /// The current state of the vessel WRT to being in the planet's shadow.
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
    /// Suppress the error that no progress has been made with the current
    /// changepoint.
    /// </summary>
    ///
    /// <remarks>
    /// This does nothing if called outside of evaluation of a changepoint.
    /// It is provided as an escape hatch in case you end up modifying behaviours
    /// in an <see cref="onVesselChangepoint"/> callback.
    /// </remarks>
    public void SuppressNoProgressError()
    {
        ImmediateChangepointRequested = true;
    }

    /// <summary>
    /// Indicate that a change has been made that might change the current
    /// changepoint or simulation behaviour.
    /// </summary>
    ///
    /// <remarks>
    /// You should call this any time you directly modify inventories or
    /// converters owned by this <see cref="BackgroundResourceProcessor"/>.
    /// This method is cheap and updates to determine if a changepoint needs
    /// to be run will be deferred until LateUpdate.
    /// </remarks>
    public void MarkDirty()
    {
        if (IsDirty)
            return;

        EventDispatcher.RegisterDirty(this);
        IsDirty = true;
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
        using var span = new TraceSpan("BackgroundResourceProcessor.GetSimulator");
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
        return GetResourceState(resourceName.GetHashCode());
    }

    /// <summary>
    /// Get a summary for a single resource.
    /// </summary>
    public InventoryState GetResourceState(int resourceId)
    {
        InventoryState state = default;
        foreach (var inventory in processor.inventories)
        {
            if (inventory.ResourceId == resourceId)
                state = state.Merge(inventory.State);
        }
        return state;
    }

    public VesselState GetVesselState() => GetVesselState(Planetarium.GetUniversalTime());

    /// <summary>
    /// Get the current <see cref="VesselState"/> for this processor.
    /// </summary>
    /// <param name="UT"></param>
    /// <returns></returns>
    public VesselState GetVesselState(double UT)
    {
        ShadowState ??= Shadow.GetShadowState(vessel);
        return new VesselState(UT) { Processor = this, ShadowState = ShadowState.Value };
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
        converter.Behaviour.Vessel = Vessel;

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
        MarkDirty();
        return index;
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
            throw new ArgumentException(nameof(amount), "amount added was NaN");

        if (vessel.loaded)
            return 0.0;

        UpdateBackgroundState();

        if (amount == 0.0)
            return 0.0;

        var resourceId = resourceName.GetHashCode();
        var added = AddResourceImpl(resourceName, resourceId, amount, includeModuleInventories);

        if (added != 0.0)
            MarkDirty();

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

    internal void StressTest(int count)
    {
        using var span = new TraceSpan("BackgroundResourceProcessor.StressTest");

        for (int i = 0; i < count; ++i)
            processor.ComputeRates();
    }
    #endregion

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ResourceInventoryEnumerator GetResourceEnumerator(
        int resourceId,
        bool includeModuleInventories = false
    )
    {
        return new ResourceInventoryEnumerator(this, resourceId, includeModuleInventories);
    }

    [method: MethodImpl(MethodImplOptions.AggressiveInlining)]
    private struct ResourceInventoryEnumerator(
        BackgroundResourceProcessor processor,
        int resourceId,
        bool includeModuleInventories
    ) : IEnumerator<ResourceInventory>
    {
        List<ResourceInventory>.Enumerator enumerator = processor.Inventories.GetEnumerator();
        readonly int resourceId = resourceId;
        readonly bool includeModuleInventories = includeModuleInventories;

        public ResourceInventory Current
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get { return enumerator.Current; }
        }
        object IEnumerator.Current => Current;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool MoveNext()
        {
            while (enumerator.MoveNext())
            {
                var inventory = enumerator.Current;
                if (inventory.ResourceId != resourceId)
                    continue;
                if (!includeModuleInventories && inventory.ModuleId is not null)
                    continue;

                return true;
            }

            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Reset()
        {
            CallReset(ref enumerator);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Dispose()
        {
            enumerator.Dispose();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void CallReset<T>(ref T value)
            where T : struct, IEnumerator<ResourceInventory>
        {
            value.Reset();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly ResourceInventoryEnumerator GetEnumerator()
        {
            return this;
        }
    }
}
