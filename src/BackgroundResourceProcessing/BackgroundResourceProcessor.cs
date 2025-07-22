using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using BackgroundResourceProcessing.Addons;
using BackgroundResourceProcessing.Converter;
using BackgroundResourceProcessing.Core;
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
public sealed class BackgroundResourceProcessor : VesselModule
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
            state = state.Merge(inventory.State);
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

    #region Overrides & Event Handlers
    public override Activation GetActivation()
    {
        return Activation.LoadedOrUnloaded;
    }

    public override bool ShouldBeActive()
    {
        return HighLogic.LoadedScene switch
        {
            GameScenes.SPACECENTER
            or GameScenes.TRACKSTATION
            or GameScenes.PSYSTEM
            or GameScenes.FLIGHT => true,
            _ => false,
        };
    }

    public override int GetOrder()
    {
        // We want this to run before most other modules.
        //
        // The default order is 999, so this should be early enough to still
        // allow other modules to go first if they really want to.
        return 100;
    }

    protected override void OnStart()
    {
        RegisterCallbacks();

        if (vessel.loaded)
        {
            LoadVessel();
        }
        else
        {
            EventDispatcher.RegisterChangepointCallback(this, processor.nextChangepoint);
        }
    }

    void OnDestroy()
    {
        // Prevent a nullref exception during vesselmodule enumeration.
        if (vessel == null)
            return;

        EventDispatcher.UnregisterChangepointCallbacks(this);

        UnregisterCallbacks();
    }

    public override void OnLoadVessel()
    {
        LoadVessel();

        GameEvents.onGameStateSave.Add(OnGameStateSave);
    }

    public override void OnUnloadVessel()
    {
        GameEvents.onGameStateSave.Remove(OnGameStateSave);
    }

    protected override void OnSave(ConfigNode node)
    {
        base.OnSave(node);
        processor.Save(node);
        ShadowState?.Save(node.AddNode("SHADOW_STATE"));
    }

    protected override void OnLoad(ConfigNode node)
    {
        base.OnLoad(node);
        processor.Load(node, Vessel);

        ConfigNode shadow = null;
        if (node.TryGetNode("SHADOW_STATE", ref shadow))
            ShadowState = Shadow.Load(shadow);

        foreach (var converter in processor.converters)
            converter.Behaviour?.Vessel = vessel;
    }

    internal void OnChangepoint(double changepoint)
    {
        // We do nothing for active vessels.
        if (vessel.loaded)
            return;

        LogUtil.Debug(() =>
            $"Updating vessel {vessel.GetDisplayName()} at changepoint {changepoint}"
        );

        var state = new VesselState(changepoint);
        state.SetShadowState(ShadowState ??= Shadow.GetShadowState(vessel));

        var recompute = false;

        processor.RecordProtoInventories(vessel);
        if (processor.UpdateState(changepoint, true))
            recompute = true;
        if (processor.UpdateBehaviours(state))
            recompute = true;
        if (recompute)
        {
            processor.ComputeRates();
            DispatchOnRatesComputed(changepoint);
        }

        processor.UpdateNextChangepoint(changepoint);

        EventDispatcher.RegisterChangepointCallback(this, processor.nextChangepoint);
    }

    // The EventDispatcher module takes care of calling this for only the
    // vessel that is actually switching.
    internal void OnVesselSOIChanged(GameEvents.HostedFromToAction<Vessel, CelestialBody> evt)
    {
        // We do nothing for active vessels.
        if (vessel.loaded)
            return;
        if (!ReferenceEquals(vessel, evt.host))
            return;

        ShadowState = Shadow.GetShadowState(vessel);
        var state = new VesselState(Planetarium.GetUniversalTime());
        state.SetShadowState(ShadowState.Value);

        processor.ForceUpdateBehaviours(state);
        processor.ComputeRates();
        DispatchOnRatesComputed(state.CurrentTime);
        processor.UpdateNextChangepoint(state.CurrentTime);

        EventDispatcher.UnregisterChangepointCallbacks(this);
        EventDispatcher.RegisterChangepointCallback(this, processor.nextChangepoint);
    }

    // We use this event to save the vessel state _before_ we actually get saved.
    private void OnGameStateSave(ConfigNode _)
    {
        if (!vessel.loaded)
            return;

        SaveVessel();
    }
    #endregion

    #region Implementation Details
    private void LoadVessel()
    {
        var currentTime = Planetarium.GetUniversalTime();

        processor.UpdateState(currentTime, false);
        processor.ApplyInventories(Vessel);
        onVesselRestore.Fire(this);
        processor.ClearVesselState();

        ShadowState = null;
    }

    private void SaveVessel()
    {
        // If the vessel is getting destroyed then there is no point in recording
        // the vessel state.
        if (vessel == null)
            processor.ClearVesselState();

        var currentTime = Planetarium.GetUniversalTime();
        var state = new VesselState(currentTime);

        // If we have already been saved this frame then there is nothing else
        // we need to do here.
        if (processor.lastUpdate == currentTime)
            return;

        ShadowState = Shadow.GetShadowState(vessel);
        state.SetShadowState((Shadow)ShadowState);

        processor.RecordVesselState(vessel, currentTime);
        onVesselRecord.Fire(this);
        processor.ForceUpdateBehaviours(state);
        processor.ComputeRates();
        DispatchOnRatesComputed(currentTime);
        processor.UpdateNextChangepoint(currentTime);
    }

    private void DispatchOnRatesComputed(double currentTime)
    {
        foreach (var converter in processor.converters)
        {
            converter.Behaviour?.OnRatesComputed(
                this,
                converter,
                new() { CurrentTime = currentTime }
            );
        }
    }

    private void RegisterCallbacks()
    {
        if (vessel.loaded)
            GameEvents.onGameStateSave.Add(OnGameStateSave);
    }

    private void UnregisterCallbacks()
    {
        if (vessel.loaded)
            GameEvents.onGameStateSave.Remove(OnGameStateSave);
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
