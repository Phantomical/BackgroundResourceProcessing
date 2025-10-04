using System.Collections.Generic;
using BackgroundResourceProcessing.Collections;
using BackgroundResourceProcessing.Utils;

namespace BackgroundResourceProcessing.Behaviour;

/// <summary>
/// A behaviour which has rates that are scaled relative to the amount of
/// a resource present in inventories on this vessel.
/// </summary>
public class ResourceRelativeBehaviour : ConverterBehaviour
{
    public struct ScaledRatio() : ConfigUtil.IConfigLoadable
    {
        public ResourceRatio ResourceRatio;

        /// <summary>
        /// Whether this resource rate should not be multiplied by the
        /// resource amount.
        /// </summary>
        public bool Fixed = false;

        public string ResourceName
        {
            readonly get => ResourceRatio.ResourceName;
            set => ResourceRatio.ResourceName = value;
        }

        public double Ratio
        {
            readonly get => ResourceRatio.Ratio;
            set => ResourceRatio.Ratio = value;
        }

        public bool DumpExcess
        {
            readonly get => ResourceRatio.DumpExcess;
            set => ResourceRatio.DumpExcess = value;
        }

        public ResourceFlowMode FlowMode
        {
            readonly get => ResourceRatio.FlowMode;
            set => ResourceRatio.FlowMode = value;
        }

        public void Load(ConfigNode node)
        {
            ResourceRatio.Load(node);
            node.TryGetValue(nameof(Fixed), ref Fixed);
        }

        public void Save(ConfigNode node)
        {
            ResourceRatio.Save(node);
            node.AddValue(nameof(Fixed), Fixed);
        }
    }

    public List<ScaledRatio> Inputs;
    public List<ScaledRatio> Outputs;
    public List<ResourceConstraint> Required;

    /// <summary>
    /// The name of the resource whose amount will be used to multiply the
    /// input and output rates of this converter.
    /// </summary>
    [KSPField(isPersistant = true)]
    public string ResourceName;

    /// <summary>
    /// The maximum relative error that this behaviour will allow before
    /// creating a changepoint.
    /// </summary>
    [KSPField(isPersistant = true)]
    public double MaxError = 0.1;

    /// <summary>
    /// The minimum amount of time between consecutive changepoints for this
    /// converter, in seconds.
    /// </summary>
    ///
    /// <remarks>
    /// This is to prevent infinite changepoints if the resource this behaviour
    /// depends on is decreasing due to other converters.
    /// </remarks>
    [KSPField(isPersistant = true)]
    public double MinChangepointDelta = 600.0;

    /// <summary>
    /// The amount value the last time <see cref="GetResources"/> was called.
    /// </summary>
    [KSPField(isPersistant = true)]
    private double LastAmount = 0.0;

    [KSPField(isPersistant = true)]
    private double LastChangepoint = 0.0;

    /// <summary>
    /// The set of inventories that are taken into account when determining the
    /// amount of available resource.
    /// </summary>
    public DynamicBitSet Connected;

    public override ConverterResources GetResources(VesselState state)
    {
        var inventories = state.Processor.Inventories;
        var resourceId = ResourceName.GetHashCode();
        double amount = 0.0;
        foreach (var invId in Connected)
        {
            if (invId >= inventories.Count)
                continue;

            var inventory = inventories[invId];
            if (inventory.ResourceId != resourceId)
                continue;

            amount += inventory.Amount;
        }

        LastAmount = amount;
        LastChangepoint = state.CurrentTime;
        return new ConverterResources()
        {
            Inputs = MultiplyRatios(Inputs, amount),
            Outputs = MultiplyRatios(Outputs, amount),
            Requirements = Required,
        };
    }

    public override void OnRatesComputed(
        BackgroundResourceProcessor processor,
        Core.ResourceConverter converter,
        RateCalculatedEvent evt
    )
    {
        var inventories = processor.Inventories;
        var resourceId = ResourceName.GetHashCode();
        double amount = 0.0;
        double rate = 0.0;
        foreach (var invId in Connected)
        {
            if (invId >= inventories.Count)
                continue;

            var inventory = inventories[invId];
            if (inventory.ResourceId != resourceId)
                continue;

            amount += inventory.Amount;
            rate += inventory.Rate;
        }

        if (rate == 0.0)
        {
            converter.NextChangepoint = double.PositiveInfinity;
            return;
        }

        double hi = LastAmount * (1 + MaxError);
        double lo = LastAmount * (1 - MaxError);

        // Someone manually updated the resources within the inventories we
        // care about. We unfortunately need to perform another changepoint
        // immediately.
        if (amount <= lo || hi <= amount)
        {
            converter.NextChangepoint = evt.CurrentTime;
            processor.SuppressNoProgressError();
            return;
        }

        double bound = rate < 0.0 ? lo : hi;
        double dt = (bound - amount) / rate * MaxError;
        if (dt < 1.0)
            dt = 1.0;

        double next = evt.CurrentTime + dt;
        if (next < LastChangepoint + MinChangepointDelta)
            next = LastChangepoint + MinChangepointDelta;

        converter.NextChangepoint = next;
    }

    protected override void OnLoad(ConfigNode node)
    {
        base.OnLoad(node);

        Inputs = ConfigUtil.LoadNodeList<ScaledRatio>(node, "INPUT_RESOURCE");
        Outputs = ConfigUtil.LoadNodeList<ScaledRatio>(node, "OUTPUT_RESOURCE");
        Required = ConfigUtil.LoadNodeList<ResourceConstraint>(node, "REQUIRED_RESOURCE");

        Connected = new(8);

        var values = node.GetValues(nameof(Connected));
        for (int i = values.Length - 1; i >= 0; --i)
        {
            if (uint.TryParse(values[i], out var index))
                Connected.Add(index);
        }
    }

    protected override void OnSave(ConfigNode node)
    {
        base.OnSave(node);

        ConfigUtil.SaveNodeList(node, "INPUT_RESOURCE", Inputs);
        ConfigUtil.SaveNodeList(node, "OUTPUT_RESOURCE", Outputs);
        ConfigUtil.SaveNodeList(node, "REQUIRED_RESOURCE", Required);

        foreach (var index in Connected)
            node.AddValue(nameof(Connected), index);
    }

    private static List<ResourceRatio> MultiplyRatios(List<ScaledRatio> ratios, double amount)
    {
        List<ResourceRatio> results = new(ratios.Count);
        foreach (var _ratio in ratios)
        {
            var ratio = _ratio;
            if (!ratio.Fixed)
                ratio.Ratio *= amount;
            results.Add(ratio.ResourceRatio);
        }

        return results;
    }
}
