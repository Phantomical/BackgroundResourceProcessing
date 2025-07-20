using System.Collections.Generic;
using System.Linq;

namespace BackgroundResourceProcessing;

/// <summary>
/// A description of the resources used by a converter.
/// </summary>
public struct ConverterResources()
{
    /// <summary>
    /// A list of resources that are consumed by this converter, along
    /// with their rates and flow modes.
    /// </summary>
    public List<ResourceRatio> Inputs = [];

    /// <summary>
    /// A list of resources that are produced by this converter, along
    /// with their rates and flow modes.
    /// </summary>
    public List<ResourceRatio> Outputs = [];

    /// <summary>
    /// A list of constraints on what resources must be present on the
    /// vessel in order for this converter to be active.
    /// </summary>
    public List<ResourceConstraint> Requirements = [];

    /// <summary>
    /// The time at which the resources emitted by this vessel will change next.
    /// </summary>
    ///
    /// <remarks>
    ///   <para>
    ///     This can be used to simulate behaviours that have non-linear
    ///     behaviour by approximating them using a piecewise linear rate
    ///     function. However, adding more changepoints does have a cost so
    ///     it is best to limit updates to at most once per day per vessel.
    ///   </para>
    ///
    ///   <para>
    ///     In cases where there are no future changepoints, you can return
    ///     <c>double.PositiveInfinity</c>. In this case, the behaviour rates
    ///     will not be loaded again due to changepoint timeout. Note that
    ///     refreshes will still happen when the vessel is switched to, or
    ///     when it switches from one SOI to another.
    ///   </para>
    ///
    ///   <para>
    ///     By default, this is <c>double.PositiveInfinity</c>.
    ///   </para>
    /// </remarks>
    public double NextChangepoint = double.PositiveInfinity;

    public ConverterResources(ConversionRecipe recipe)
        : this()
    {
        Inputs.AddRange(recipe.Inputs);
        Outputs.AddRange(recipe.Outputs);
        Requirements.AddRange(recipe.Requirements.Select(req => new ResourceConstraint(req)));
    }
}
