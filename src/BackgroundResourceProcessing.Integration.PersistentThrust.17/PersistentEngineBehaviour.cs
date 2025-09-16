using System.Collections.Generic;
using BackgroundResourceProcessing.Behaviour;

namespace BackgroundResourceProcessing.Integration.PersistentThrust;

public class PersistentEngineBehaviour : ConstantConverter
{
    /// <summary>
    /// The thrust produced by this engine at full throttle.
    /// </summary>
    ///
    /// <remarks>
    /// Note that this isn't quite the same as the full throttle for the vessel,
    /// if the engine has a target throttle that is not full throttle then this
    /// will reflect that.
    /// </remarks>
    [KSPField(isPersistant = true)]
    public double Thrust = 0.0;

    /// <summary>
    /// The net amount of time that this engine has been active for. If the
    /// engine is only partially active (e.g. due to lack of resources) then
    /// the active time will advance more slowly.
    /// </summary>
    [KSPField(isPersistant = true)]
    public double ActiveTime = 0.0;

    /// <summary>
    /// Whether this behaviour is enabled.
    /// </summary>
    [KSPField(isPersistant = true)]
    public bool Enabled = true;

    public PersistentEngineBehaviour()
        : base() { }

    public PersistentEngineBehaviour(
        List<ResourceRatio> inputs,
        List<ResourceRatio> outputs,
        List<ResourceConstraint> required
    )
        : base(inputs, outputs, required) { }

    public override ConverterResources GetResources(VesselState state)
    {
        if (!Enabled)
            return new();

        return base.GetResources(state);
    }
}
