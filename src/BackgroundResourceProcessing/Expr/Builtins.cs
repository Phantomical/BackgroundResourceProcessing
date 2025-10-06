namespace BackgroundResourceProcessing.Expr;

internal readonly struct Builtins
{
    internal static readonly Builtins Instance = new();

    internal Settings Settings => Settings.Instance;
    internal MathBuiltins Math => MathBuiltins.Instance;

    internal double Infinity => double.PositiveInfinity;
}
