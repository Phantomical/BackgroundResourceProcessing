using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using BackgroundResourceProcessing.Collections;
using BackgroundResourceProcessing.Core;
using BackgroundResourceProcessing.Tracing;
using BackgroundResourceProcessing.Utils;
using TMPro;

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
public sealed class ResourceSimulator
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
    public ReadOnlyList<Core.ResourceInventory> Inventories => new(processor.inventories);

    /// <summary>
    /// Get a read-only view of the available converters.
    /// </summary>
    ///
    /// <remarks>
    /// Note that you can still modify the inventories themselves. You just
    /// cannot add or remove inventories from the set.
    /// </remarks>
    public ReadOnlyList<Core.ResourceConverter> Converters => new(processor.converters);

    private bool Dirty = false;

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
            if (inventory.ResourceName != resourceName)
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
            if (!states.TryGetValue(inventory.ResourceName, out var state))
                state = new();

            states[inventory.ResourceName] = state.Merge(inventory.State);
        }
        return states;
    }

    /// <summary>
    /// Add a new converter that doesn't correspond to any part modules on
    /// the vessel.
    /// </summary>
    /// <returns>The index of the converter within <see cref="Converters"/>.</returns>
    ///
    /// <remarks>
    ///   This allows you to introduce new converters to represent processes
    ///   that are not being simulated by BRP.
    /// </remarks>
    public int AddConverter(Core.ResourceConverter converter, VesselState state) =>
        AddConverter(converter, state, new AddConverterOptions());

    /// <summary>
    /// Add a new converter that doesn't correspond to any part modules on
    /// the vessel.
    /// </summary>
    /// <returns>The index of the converter within <see cref="Converters"/>.</returns>
    ///
    /// <remarks>
    ///   This allows you to introduce new converters to represent processes
    ///   that are not being simulated by BRP.
    /// </remarks>
    public int AddConverter(
        Core.ResourceConverter converter,
        VesselState state,
        AddConverterOptions options
    )
    {
        converter.Refresh(state);
        converter = converter.CloneForSimulator();

        if (options.LinkToAll)
        {
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

        Dirty = true;
        processor.converters.Add(converter);
        processor.UpdateConstraintState(converter);
        return processor.converters.Count - 1;
    }

    /// <summary>
    /// Create a new inventory within the simulator. This inventory will not be
    /// connected to anything, but you can add new converters that make use of it.
    /// </summary>
    /// <param name="inventory"></param>
    /// <returns></returns>
    public int AddInventory(Core.ResourceInventory inventory)
    {
        processor.inventories.Add(inventory);
        return processor.inventories.Count - 1;
    }

    /// <summary>
    /// Update the initial state to take into account any converters that have
    /// been added directly to the simulator.
    /// </summary>
    ///
    /// <remarks>
    /// This will automatically be done when you call <see cref="Steps"/> but it
    /// allows you to inspect the state of the simulator at the current time
    /// instead of whatever time the processor this simulator has been
    /// constructed from was last updated.
    /// </remarks>
    public void ComputeInitialRates() => Step(Planetarium.GetUniversalTime());

    private void Step(double currentTime)
    {
        using var span = new TraceSpan("ResourceSimulator.Step");

        bool changepoint = Dirty;
        if (processor.UpdateState(currentTime, false))
            changepoint = true;
        if (processor.UpdateConstraintState())
            changepoint = true;

        if (changepoint)
        {
            processor.ComputeRates();
            processor.UpdateNextChangepoint(currentTime);
        }

        Dirty = false;
    }

    /// <summary>
    /// Update the simulation up to the requested time.
    /// </summary>
    /// <param name="UT">The time at which to stop the simulation.</param>
    public void Update(double UT)
    {
        while (NextChangepoint < UT)
            Step(NextChangepoint);

        Step(UT);
    }

    /// <summary>
    /// An enumerator over the steps of this simulator. It will advance the
    /// simulator as it goes.
    /// </summary>
    public IEnumerable<double> Steps()
    {
        var currentTime = Planetarium.GetUniversalTime();

        for (int i = 0; i < IterationLimit; ++i)
        {
            Step(currentTime);

            yield return currentTime;

            currentTime = processor.nextChangepoint;
            if (!MathUtil.IsFinite(currentTime))
                break;
        }
    }
}
