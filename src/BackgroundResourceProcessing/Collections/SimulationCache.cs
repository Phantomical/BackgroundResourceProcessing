using System;
using System.Collections.Generic;
using UnityEngine;

namespace BackgroundResourceProcessing.Collections;

/// <summary>
/// A cache for vessel simulation results. This is designed to be useful for
/// caching simulation results for use cases such as status panels.
/// </summary>
/// <typeparam name="T"></typeparam>
/// <param name="options"></param>
/// <remarks>
///   <para>
///     Unity does not allow generic components. To use this class create a
///     new class type that inherits from it and use that as your cache component
///     type.
///   </para>
///
///   <code>
/// struct MyCacheEntry {}
/// class MySimulationCache() : SimulationCache&lt;MyCacheEntry&gt;() { }
///
/// // Use MySimulationCache here..
///   </code>
/// </remarks>
public abstract class SimulationCache<T>(SimulationCache<T>.Options options) : MonoBehaviour
{
    struct Entry
    {
        public double LastChangepoint;
        public T Value;
    }

    struct LoadedEntry
    {
        public DateTime LastRecorded;
        public T Value;
    }

    /// <summary>
    /// Options configuring the <see cref="SimulationCache{T}"/>.
    /// </summary>
    public struct Options()
    {
        /// <summary>
        /// The length of time for which a simulation result for a loaded
        /// vessel remains valid. This is in real-time seconds, not UT.
        /// The default is 1 second.
        /// </summary>
        public TimeSpan LoadedTTL = new(TimeSpan.TicksPerSecond);
    }

    public class MissingProcessorException()
        : Exception("vessel did not have a BackgroundResourceProcessor module") { }

    public delegate T CacheFunc(BackgroundResourceProcessor processor);

    public Options options = options;
    readonly Dictionary<Guid, Entry> unloaded = [];
    readonly Dictionary<Guid, LoadedEntry> loaded = [];

    private bool Enabled => DebugSettings.Instance?.EnableSolutionCache ?? true;

    public SimulationCache()
        : this(new Options()) { }

    /// <summary>
    /// Get the simulation cache entry for <paramref name="vessel"/>. If one is
    /// not present then it will evaluate <paramref name="func"/> in order to
    /// create one.
    /// </summary>
    /// <exception cref="MissingProcessorException"></exception>
    public T GetVesselEntry(Vessel vessel, CacheFunc func)
    {
        var processor = vessel.FindVesselModuleImplementing<BackgroundResourceProcessor>();
        if (processor == null)
            throw new MissingProcessorException();

        return GetVesselEntry(processor, func);
    }

    /// <summary>
    /// Get the simulation cache entry for the vessel containing
    /// <paramref name="processor"/>. If one is not presetn then it will
    /// evaluate <paramref name="func"/> in order to create one.
    /// </summary>
    /// <exception cref="ArgumentNullException"></exception>
    public T GetVesselEntry(BackgroundResourceProcessor processor, CacheFunc func)
    {
        if (processor == null)
            throw new ArgumentNullException(nameof(processor));

        var vessel = processor.Vessel;

        if (vessel.loaded)
        {
            var now = DateTime.UtcNow;
            if (Enabled && loaded.TryGetValue(vessel.id, out var saved))
            {
                if ((now - saved.LastRecorded) < options.LoadedTTL)
                    return saved.Value;
            }

            saved.Value = func(processor);
            saved.LastRecorded = now;
            loaded[vessel.id] = saved;
            return saved.Value;
        }
        else
        {
            if (Enabled && unloaded.TryGetValue(vessel.id, out var saved))
            {
                if (saved.LastChangepoint == processor.LastChangepoint)
                    return saved.Value;
            }

            saved.Value = func(processor);
            saved.LastChangepoint = processor.LastChangepoint;
            unloaded[vessel.id] = saved;
            return saved.Value;
        }
    }

    /// <summary>
    /// Erase the cached state for <paramref name="vessel"/>.
    /// </summary>
    public void RemoveEntry(Vessel vessel) => ResetCachedState(vessel);

    #region MonoBehaviour Methods
    protected void Awake()
    {
        GameEvents.onVesselDestroy.Add(ResetCachedState);
        GameEvents.onVesselUnloaded.Add(ResetCachedState);
        GameEvents.onVesselPartCountChanged.Add(ResetCachedState);
    }

    protected void OnDestroy()
    {
        GameEvents.onVesselDestroy.Remove(ResetCachedState);
        GameEvents.onVesselUnloaded.Remove(ResetCachedState);
        GameEvents.onVesselPartCountChanged.Remove(ResetCachedState);
    }
    #endregion

    #region Event Handlers
    void ResetCachedState(Vessel vessel)
    {
        unloaded.Remove(vessel.id);
        loaded.Remove(vessel.id);
    }
    #endregion
}
