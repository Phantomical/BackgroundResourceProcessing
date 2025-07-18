using System;
using System.Collections.Generic;
using System.Reflection;
using BackgroundResourceProcessing.Behaviour;
using BackgroundResourceProcessing.Collections;
using LifeSupport;
using UnityEngine;

namespace BackgroundResourceProcessing.Integration.USILifeSupport;

[KSPAddon(KSPAddon.Startup.AllGameScenes, false)]
public class EventDispatcher : MonoBehaviour
{
    void Start()
    {
        BackgroundResourceProcessor.onVesselRecord.Add(OnRecord);
        BackgroundResourceProcessor.onVesselRestore.Add(OnRestore);
    }

    void OnDestroy()
    {
        BackgroundResourceProcessor.onVesselRecord.Remove(OnRecord);
        BackgroundResourceProcessor.onVesselRestore.Remove(OnRestore);
    }

    void OnRecord(BackgroundResourceProcessor processor)
    {
        var module =
            processor.Vessel.FindVesselModuleImplementing<ModuleBackgroundUSILifeSupport>();
        if (module == null)
            return;

        module.OnRecord(processor);
    }

    void OnRestore(BackgroundResourceProcessor processor)
    {
        var module =
            processor.Vessel.FindVesselModuleImplementing<ModuleBackgroundUSILifeSupport>();
        if (module == null)
            return;

        module.OnRestore(processor);
    }
}

public class ModuleBackgroundUSILifeSupport : VesselModule
{
    private const BindingFlags Flags =
        BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    private static readonly PropertyInfo SupplyRecipeProperty =
        typeof(ModuleLifeSupportSystem).GetProperty("SupplyRecipe", Flags);
    private static readonly PropertyInfo ECRecipeProperty =
        typeof(ModuleLifeSupportSystem).GetProperty("ECRecipe", Flags);

    [KSPField(isPersistant = true)]
    public int EcConverterIndex = -1;

    [KSPField(isPersistant = true)]
    public int SupplyConverterIndex = -1;

    public ModuleLifeSupportSystem LifeSupport =>
        _lifeSupport ??= vessel.FindVesselModuleImplementing<ModuleLifeSupportSystem>();
    private ModuleLifeSupportSystem _lifeSupport = null;

    internal void OnRecord(BackgroundResourceProcessor processor)
    {
        ClearState();

        if (!IsEnabled())
            return;

        if (LifeSupport == null)
            return;

        // No crew means there's nothing we need to add here.
        if (Vessel.GetCrewCount() == 0)
            return;

        var supplyStatus = GetVesselSupplyStatus();

        var supplyBehaviour = new USILifeSupportBehaviour(GenerateSupplyRecipe(LifeSupport));
        var ecBehaviour = new USILifeSupportBehaviour(GenerateEcRecipe(LifeSupport));

        supplyBehaviour.lastSatisfied = supplyStatus?.LastFeeding;
        ecBehaviour.lastSatisfied = supplyStatus?.LastECCheck;

        // We set the converters at a high priority so that life support gets
        // prioritized over everything else.
        var supplyConverter = new Core.ResourceConverter(supplyBehaviour) { priority = 10 };
        var ecConverter = new Core.ResourceConverter(ecBehaviour) { priority = 10 };

        // Now we need to manually set up which inventories this converter is
        // connected to.
        var inventories = processor.Inventories;
        DynamicBitSet supplies = new(inventories.Count);
        DynamicBitSet mulch = new(inventories.Count);
        DynamicBitSet ec = new(inventories.Count);

        for (int i = 0; i < inventories.Count; ++i)
        {
            var inventory = inventories[i];

            if (inventory.resourceName == "Supplies")
                supplies.Add(i);
            else if (inventory.resourceName == "Mulch")
                mulch.Add(i);
            else if (inventory.resourceName == "ElectricCharge")
                ec.Add(i);
        }

        supplyConverter.Pull.Add("Supplies", supplies);
        supplyConverter.Push.Add("Mulch", mulch);
        ecConverter.Pull.Add("ElectricCharge", ec);

        // And finally, add them to the processor and save the indices for later.
        SupplyConverterIndex = processor.AddConverter(supplyConverter);
        EcConverterIndex = processor.AddConverter(ecConverter);
    }

    internal void OnRestore(BackgroundResourceProcessor processor)
    {
        if (!IsEnabled())
            return;

        if (LifeSupport == null)
            return;

        if (EcConverterIndex < 0 || SupplyConverterIndex < 0)
            return;

        var converters = processor.Converters;

        if (EcConverterIndex >= converters.Count)
            return;
        if (SupplyConverterIndex >= converters.Count)
            return;

        if (converters[EcConverterIndex].Behaviour is not USILifeSupportBehaviour ec)
            return;
        if (converters[SupplyConverterIndex].Behaviour is not USILifeSupportBehaviour supply)
            return;

        ec.OnRestore();
        supply.OnRestore();

        UpdateCrewState(ec, supply);
        ClearState();
    }

    private void UpdateCrewState(USILifeSupportBehaviour ec, USILifeSupportBehaviour supply)
    {
        var now = Planetarium.GetUniversalTime();
        var manager = LifeSupportManager.Instance;

        // We specifically update LastUpdateTime but not _lastProcessingTime so
        // that the module goes through the appropriate long-update checks.
        LifeSupport.LastUpdateTime = now;
        LifeSupport.VesselStatus.LastFeeding = supply.lastSatisfied ?? now;
        LifeSupport.VesselStatus.LastECCheck = ec.lastSatisfied ?? now;

        // TODO: We are also tracking the max amount of time the kerbal was
        //       without ec/supplies. We likely want to apply permanent statuses
        //       (e.g. KIA, MIA, wandered back to KSC) even if the supply
        //       situation is OK now.
        foreach (var kerbal in Vessel.GetVesselCrew())
        {
            var trackedKerbal = manager.FetchKerbal(kerbal);

            trackedKerbal.LastEC = ec.lastSatisfied ?? now;
            trackedKerbal.LastMeal = supply.lastSatisfied ?? now;
        }
    }

    private bool IsEnabled()
    {
        var settings = HighLogic.CurrentGame?.Parameters.CustomParams<Settings>();
        if (!(settings?.EnableUSILSIntegration ?? false))
            return false;

        // There should be nothing to model on an EVA and they have different
        // rules for what conditions effect them. We just let USI-LS handle them.
        if (Vessel.isEVA)
            return false;

        return true;
    }

    private void ClearState()
    {
        EcConverterIndex = -1;
        SupplyConverterIndex = -1;
    }

    private VesselSupplyStatus GetVesselSupplyStatus()
    {
        var vesselId = vessel.id.ToString();
        var vessels = LifeSupportManager.Instance.VesselSupplyInfo;

        foreach (var vsi in vessels)
        {
            if (vsi.VesselId == vesselId)
                return vsi;
        }

        return null;
    }

    private static ConversionRecipe GenerateSupplyRecipe(ModuleLifeSupportSystem module)
    {
        return (ConversionRecipe)SupplyRecipeProperty.GetValue(module);
    }

    private static ConversionRecipe GenerateEcRecipe(ModuleLifeSupportSystem module)
    {
        return (ConversionRecipe)ECRecipeProperty.GetValue(module);
    }
}

public class USILifeSupportBehaviour(ConversionRecipe recipe) : ConverterBehaviour
{
    public List<ResourceRatio> Inputs = recipe?.Inputs ?? [];
    public List<ResourceRatio> Outputs = recipe?.Outputs ?? [];

    public double? lastSatisfied = null;

    [KSPField(isPersistant = true)]
    public double maxTimeWithout = 0.0;

    public USILifeSupportBehaviour()
        : this(null) { }

    public override ConverterResources GetResources(VesselState state)
    {
        return new() { Inputs = Inputs, Outputs = Outputs };
    }

    public override void OnRatesComputed(
        BackgroundResourceProcessor processor,
        Core.ResourceConverter converter,
        RateCalculatedEvent evt
    )
    {
        base.OnRatesComputed(processor, converter, evt);

        if (converter.rate < 1.0)
            lastSatisfied ??= evt.CurrentTime;
        else
        {
            if (lastSatisfied != null)
                maxTimeWithout = Math.Max(maxTimeWithout, evt.CurrentTime - (double)lastSatisfied);

            lastSatisfied = null;
        }
    }

    public void OnRestore()
    {
        var now = Planetarium.GetUniversalTime();

        if (lastSatisfied != null)
            maxTimeWithout = Math.Max(maxTimeWithout, now - (double)lastSatisfied);
    }

    protected override void OnLoad(ConfigNode node)
    {
        base.OnLoad(node);

        Inputs.AddRange(ConfigUtil.LoadInputResources(node));
        Outputs.AddRange(ConfigUtil.LoadOutputResources(node));

        double lastSatisfied = 0;
        if (node.TryGetValue("lastSatisfied", ref lastSatisfied))
            this.lastSatisfied = lastSatisfied;
    }

    protected override void OnSave(ConfigNode node)
    {
        base.OnSave(node);

        ConfigUtil.SaveInputResources(node, Inputs);
        ConfigUtil.SaveOutputResources(node, Outputs);

        if (lastSatisfied != null)
            node.AddValue("lastSatisfied", (double)lastSatisfied);
    }
}
