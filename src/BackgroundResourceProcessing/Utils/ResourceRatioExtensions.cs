namespace BackgroundResourceProcessing.Utils;

internal static class ResourceRatioExtension
{
    public static ResourceRatio WithMultiplier(this ResourceRatio res, double multiplier)
    {
        res.Ratio *= multiplier;
        return res;
    }
}
