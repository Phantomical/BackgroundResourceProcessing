using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace BackgroundResourceProcessing.Expr;

internal class Settings
{
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    internal static readonly Settings Instance = new();

    public SectionSettings this[string section]
    {
        get
        {
            var parameters = HighLogic.CurrentGame?.Parameters;
            if (parameters == null)
                return null;

            var selected = GetCustomParams(parameters)
                .Values.Where(node => node.Section == section);

            return new(selected);
        }
    }

    private Settings() { }

    private static readonly FieldInfo CustomParamsField = typeof(GameParameters).GetField(
        "customParams",
        Flags
    );

    private static Dictionary<Type, GameParameters.CustomParameterNode> GetCustomParams(
        GameParameters parameters
    )
    {
        return (Dictionary<Type, GameParameters.CustomParameterNode>)
            CustomParamsField.GetValue(parameters);
    }
};

internal class SectionSettings(IEnumerable<GameParameters.CustomParameterNode> nodes)
{
    internal GameParameters.CustomParameterNode this[string name] =>
        nodes.Where(node => node.GetType().Name == name || node.Title == name).FirstOrDefault();
}
