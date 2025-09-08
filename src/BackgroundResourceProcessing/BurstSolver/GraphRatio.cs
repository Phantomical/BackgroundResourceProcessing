namespace BackgroundResourceProcessing.BurstSolver;

/// <summary>
/// <see cref="ResourceRatio"/> but burst-compatible.
/// </summary>
public struct GraphRatio
{
    public double Ratio;
    public bool DumpExcess;
    public ResourceFlowMode FlowMode;

    public GraphRatio() { }

    public GraphRatio(ResourceRatio ratio)
    {
        Ratio = ratio.Ratio;
        DumpExcess = ratio.DumpExcess;
        FlowMode = ratio.FlowMode;
    }
}
