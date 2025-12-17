using System;
using BackgroundResourceProcessing.Utils;

namespace BackgroundResourceProcessing;

/// <summary>
/// A summary of the current state of the vessel.
/// </summary>
///
/// <remarks>
/// This is used to give converter behaviours access to shared state and also
/// as a place to cache common state that will be used by multiple instances
/// of the same behaviour across the vessel.
/// </remarks>
public class VesselState(double CurrentTime)
{
    /// <summary>
    /// The time at which we are getting the rate.
    /// </summary>
    ///
    /// <remarks>
    /// Note that this may not correspond with the current game time, though
    /// it should usually be close enough that using other properties
    /// associated with the vessel (e.g. position) should be fine.
    /// </remarks>
    public double CurrentTime { get; } = CurrentTime;

    public ShadowState ShadowState { get; set; } = ShadowState.AlwaysInShadow();

    public IResourceProcessor Processor { get; set; } = null;

    /// <summary>
    /// Create a <c><see cref="VesselState"/></c> with the current time.
    /// </summary>
    public VesselState()
        : this(Planetarium.GetUniversalTime()) { }
}
