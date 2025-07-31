using BackgroundResourceProcessing.Addons;
using BackgroundResourceProcessing.Tracing;
using Shadow = BackgroundResourceProcessing.ShadowState;

namespace BackgroundResourceProcessing;

public sealed partial class BackgroundResourceProcessor
{
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
        using var span = new TraceSpan("BackgroundResourceProcessor.OnLoadVessel");

        LoadVessel();

        GameEvents.onGameStateSave.Add(OnGameStateSave);
    }

    public override void OnUnloadVessel()
    {
        using var span = new TraceSpan("BackgroundResourceProcessor.OnUnloadVessel");

        GameEvents.onGameStateSave.Remove(OnGameStateSave);

        SaveVessel();
        EventDispatcher.RegisterChangepointCallback(this, NextChangepoint);
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

        using var span = new TraceSpan(() =>
            $"BackgroundResourceProcessor.OnChangepoint({vessel.GetDisplayName()})"
        );

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

        using var span = new TraceSpan(() =>
            $"BackgroundResourceProcessor.OnVesselSOIChanged({vessel.GetDisplayName()})"
        );

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

        using var span = new TraceSpan("BackgroundResourceProcessor.OnGameStateSave");

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
        using (var eventspan = new TraceSpan("onVesselRecord"))
            onVesselRecord.Fire(this);
        processor.ForceUpdateBehaviours(state);
        processor.ComputeRates();
        DispatchOnRatesComputed(currentTime);
        processor.UpdateNextChangepoint(currentTime);
    }

    private void DispatchOnRatesComputed(double currentTime)
    {
        using var span = new TraceSpan("BackgroundResourceProcessor.DispatchOnRatesComputed");

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
}
