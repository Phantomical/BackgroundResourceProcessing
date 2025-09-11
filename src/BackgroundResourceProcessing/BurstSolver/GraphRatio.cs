using System.Diagnostics;

namespace BackgroundResourceProcessing.BurstSolver;

/// <summary>
/// <see cref="ResourceRatio"/> but burst-compatible.
/// </summary>
[DebuggerDisplay("{ResourceName} {Ratio}/s")]
public struct GraphRatio
{
    public int ResourceId;
    public double Ratio;
    public bool DumpExcess;
    public ResourceFlowMode FlowMode;

    private readonly string ResourceName => ResourceNames.GetResourceName(ResourceId);

    public GraphRatio() { }

    public GraphRatio(ResourceRatio ratio)
    {
        Ratio = ratio.Ratio;
        DumpExcess = ratio.DumpExcess;
        FlowMode = ratio.FlowMode;
        ResourceId = ratio.ResourceName.GetHashCode();
    }
}
