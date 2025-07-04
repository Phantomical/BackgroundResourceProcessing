using System.Collections.ObjectModel;
using BackgroundResourceProcessing.Addons;
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
    #endregion

    /// <summary>
    /// Whether background processing is actively running on this module.
    /// </summary>
    ///
    /// <remarks>
    /// Generally, this will be true when the vessel is unloaded, and false
    /// otherwise.
    /// </remarks>
    public bool BackgroundProcessingActive { get; private set; } = false;

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
        if (!BackgroundProcessingActive)
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
        var clone = processor.CloneForSimulator();
        clone.UpdateState(currentTime, false);
        clone.UpdateNextChangepoint(currentTime);
        return new(clone);
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

        if (!BackgroundProcessingActive)
            return;

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

        if (BackgroundProcessingActive)
        {
            EventDispatcher.UnregisterChangepointCallbacks(this);
        }

        UnregisterCallbacks();
    }

    public override void OnLoadVessel()
    {
        if (!BackgroundProcessingActive)
            return;

        LoadVessel();

        GameEvents.onGameStateSave.Add(OnGameStateSave);
    }

    public override void OnUnloadVessel()
    {
        GameEvents.onGameStateSave.Remove(OnGameStateSave);

        if (vessel == null)
            return;
        if (!BackgroundProcessingActive)
            LogUtil.Warn(
                "BackgroundResourceProcessor being destroyed but background processing has not been activated"
            );
    }

    protected override void OnSave(ConfigNode node)
    {
        base.OnSave(node);
        processor.Save(node);

        node.AddValue("BackgroundProcessingActive", BackgroundProcessingActive);
    }

    protected override void OnLoad(ConfigNode node)
    {
        base.OnLoad(node);
        processor.Load(node, Vessel);

        bool active = false;
        if (node.TryGetValue("BackgroundProcessingActive", ref active))
            BackgroundProcessingActive = active;

        foreach (var converter in processor.converters)
            converter.Behaviour?.Vessel = vessel;
    }

    internal void OnChangepoint(double changepoint)
    {
        // We do nothing for active vessels.
        if (!BackgroundProcessingActive)
            return;

        LogUtil.Debug(() =>
            $"Updating vessel {vessel.GetDisplayName()} at changepoint {changepoint}"
        );

        var state = new VesselState(changepoint);
        var recompute = false;

        processor.RecordProtoInventories(vessel);
        if (processor.UpdateState(changepoint, true))
            recompute = true;
        if (processor.UpdateBehaviours(state))
            recompute = true;
        if (recompute)
            processor.ComputeRates();
        processor.UpdateNextChangepoint(changepoint);

        EventDispatcher.RegisterChangepointCallback(this, processor.nextChangepoint);
    }

    // The EventDispatcher module takes care of calling this for only the
    // vessel that is actually switching.
    internal void OnVesselSOIChanged(GameEvents.HostedFromToAction<Vessel, CelestialBody> evt)
    {
        // We do nothing for active vessels.
        if (!BackgroundProcessingActive)
            return;
        if (!ReferenceEquals(vessel, evt.host))
            return;

        var state = new VesselState(Planetarium.GetUniversalTime());

        processor.ForceUpdateBehaviours(state);
        processor.ComputeRates();
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
        processor.ClearVesselState();

        BackgroundProcessingActive = false;
    }

    private void SaveVessel()
    {
        var currentTime = Planetarium.GetUniversalTime();
        var state = new VesselState(currentTime);

        processor.RecordVesselState(vessel, currentTime);
        processor.ForceUpdateBehaviours(state);
        processor.ComputeRates();
        processor.UpdateNextChangepoint(currentTime);

        BackgroundProcessingActive = true;
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
        processor.RecordVesselState(Vessel, Planetarium.GetUniversalTime());
        processor.ComputeRates();
        processor.UpdateNextChangepoint(Planetarium.GetUniversalTime());
    }

    internal void DebugClearVesselState()
    {
        processor.ClearVesselState();
    }
    #endregion
}
