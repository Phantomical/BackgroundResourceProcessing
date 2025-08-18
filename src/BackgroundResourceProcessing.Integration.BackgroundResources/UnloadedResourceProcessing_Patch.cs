using BackgroundResources;
using HarmonyLib;

namespace BackgroundResourceProcessing.Integration.BackgroundResources;

// Here we patch the UnloadedResourceProcessing class to instead talk
// to BackgroundResourceProcessor.
//
// This isn't exactly the most efficient way to do this, btu

[HarmonyPatch(typeof(UnloadedResourceProcessing))]
[HarmonyPatch(nameof(UnloadedResourceProcessing.GetResourceTotals))]
internal static class UnloadedResourceProcessing_GetResourceTotals_Patch
{
    static bool Prefix(
        ProtoVessel vessel,
        string resourceName,
        out double amount,
        out double maxAmount
    )
    {
        amount = 0.0;
        maxAmount = 0.0;

        var processor =
            vessel?.vesselRef?.FindVesselModuleImplementing<BackgroundResourceProcessor>();
        if (processor == null)
            return false;

        var state = processor.GetResourceState(resourceName);
        amount = state.amount;
        maxAmount = state.maxAmount;
        return true;
    }
}

[HarmonyPatch(typeof(UnloadedResourceProcessing))]
[HarmonyPatch(nameof(UnloadedResourceProcessing.RequestResource))]
internal static class UnloadedResourceProcessing_RequestResource_Patch
{
    static bool RequestResource(
        ProtoVessel vessel,
        string resourceName,
        double amount,
        out double amountReceived,
        bool pushing
    )
    {
        amountReceived = 0.0;
        var processor =
            vessel?.vesselRef?.FindVesselModuleImplementing<BackgroundResourceProcessor>();
        if (processor == null)
            return false;

        if (pushing)
            amountReceived = processor.AddResource(resourceName, amount);
        else
            amountReceived = processor.RemoveResource(resourceName, amount);

        return true;
    }
}
