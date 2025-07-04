#pragma warning disable CS0419 // Ambiguous reference in cref attribute

namespace BackgroundResourceProcessing.Utils;

internal static class ConfigNodeExtensions
{
    /// <summary>
    /// A variant on <see cref="ConfigNode.TryGetValue"/> that properly
    /// handles infinite values in the node text.
    /// </summary>
    public static bool TryGetDouble(this ConfigNode node, string name, ref double value)
    {
        string text = "";
        if (!node.TryGetValue(name, ref text))
            return false;

        text = text.Trim();
        switch (text)
        {
            case "Infinity":
            case "infinity":
            case "+Infinity":
            case "+infinity":
                value = double.PositiveInfinity;
                return true;
            case "-Infinity":
            case "-infinity":
                value = double.NegativeInfinity;
                return true;
            default:
                if (double.TryParse(text, out var result))
                {
                    value = result;
                    return true;
                }
                return false;
        }
    }
}
