using System.Linq;
using BackgroundResourceProcessing.Behaviour;
using BackgroundResourceProcessing.Converter;
using SystemHeat;

namespace BackgroundResourceProcessing.Integration.SystemHeat;

internal static class SystemHeatFlux
{
    static readonly string[] LoopNames = Enumerable
        .Range(0, 10)
        .Select(i => $"BRPSystemHeatFlux{i}")
        .ToArray();

    public static string ResourceName(int loopId)
    {
        if ((uint)loopId >= (uint)LoopNames.Length)
            return $"BRPSystemHeatFlux{loopId}";
        return LoopNames[loopId];
    }

    /// <summary>
    /// Appends a heat-flux output to every <see cref="ConstantConverter"/> in
    /// <paramref name="behaviour"/> and registers <paramref name="marker"/> as a
    /// push target. Does nothing if <paramref name="marker"/> or
    /// <paramref name="heatModule"/> is null.
    /// </summary>
    public static void AddFluxOutput(
        ModuleBehaviour behaviour,
        BRPSystemHeatMarker marker,
        ModuleSystemHeat heatModule,
        double fluxRate
    )
    {
        if (marker == null || heatModule == null)
            return;

        var fluxOutput = new ResourceRatio()
        {
            ResourceName = ResourceName(heatModule.currentLoopID),
            Ratio = fluxRate,
            FlowMode = ResourceFlowMode.ALL_VESSEL,
            DumpExcess = true,
        };

        foreach (var converter in behaviour.Converters)
        {
            if (converter is ConstantConverter c)
                c.Outputs.Add(fluxOutput);
        }

        behaviour.AddPushModule(marker);
    }
}
