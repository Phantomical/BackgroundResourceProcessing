using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace BackgroundResourceProcessing.Expr;

internal partial struct FieldExpression
{
    internal class Settings
    {
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

    internal class Builtins
    {
        internal static readonly Builtins Instance = new();

        public Settings Settings => Settings.Instance;
        public MathBuiltins Math => MathBuiltins.Instance;

        public double Infinity => double.PositiveInfinity;
    }
}
