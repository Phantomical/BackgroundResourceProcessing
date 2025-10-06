using System;

namespace BackgroundResourceProcessing.Expr;

/// <summary>
/// A builtin object that exposes values within the Math class for use
/// in field expressions.
/// </summary>
internal class MathBuiltins
{
    internal static readonly MathBuiltins Instance = new();

    public double PI => Math.PI;
    public double E => Math.E;

    public double Abs(double v) => Math.Abs(v);

    public double Sign(double v) => Math.Sign(v);

    public double Ceiling(double x) => Math.Ceiling(x);

    public double Floor(double x) => Math.Floor(x);

    public double Round(double x) => Math.Round(x);

    public double Truncate(double x) => Math.Truncate(x);

    public double Acos(double d) => Math.Acos(d);

    public double Asin(double d) => Math.Asin(d);

    public double Atan(double d) => Math.Atan(d);

    public double Atan2(double y, double x) => Math.Atan2(y, x);

    public double Sin(double x) => Math.Sin(x);

    public double Cos(double x) => Math.Cos(x);

    public double Tan(double x) => Math.Tan(x);

    public double Cosh(double x) => Math.Cosh(x);

    public double Sinh(double x) => Math.Sinh(x);

    public double Tanh(double x) => Math.Tanh(x);

    public double Sqrt(double x) => Math.Sqrt(x);

    public double Pow(double x, double y) => Math.Pow(x, y);

    public double Exp(double x) => Math.Exp(x);

    public double Log(double x) => Math.Log(x);

    public double Log10(double x) => Math.Log10(x);

    public double Max(double a, double b) => Math.Max(a, b);

    public double Min(double a, double b) => Math.Min(a, b);
}
