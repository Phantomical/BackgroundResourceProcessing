using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace BackgroundResourceProcessing.Maths;

/// <summary>
/// An automatically differentiated double that tracks both a value and
/// its derivative.
/// </summary>
///
/// <remarks>
/// See [0] for how this works.
///
/// [0]: https://en.wikipedia.org/wiki/Automatic_differentiation#Automatic_differentiation_using_dual_numbers
/// </remarks>
[DebuggerDisplay("x={x}, dx={dx}")]
[method: MethodImpl(MethodImplOptions.AggressiveInlining)]
internal struct Dual(double x, double dx = 0.0)
{
    public double x = x;
    public double dx = dx;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Dual Variable(double value)
    {
        return new(value, 1.0);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Dual operator +(Dual lhs, Dual rhs)
    {
        return new(lhs.x + rhs.x, lhs.dx + rhs.dx);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Dual operator -(Dual lhs, Dual rhs)
    {
        return new(lhs.x - rhs.x, lhs.dx - rhs.dx);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Dual operator *(Dual lhs, Dual rhs)
    {
        return new(lhs.x * rhs.x, lhs.x * rhs.dx + rhs.x * lhs.dx);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Dual operator /(Dual lhs, Dual rhs)
    {
        return new(lhs.x / rhs.x, lhs.dx / rhs.x - lhs.x * rhs.dx / (rhs.x * rhs.x));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Dual operator -(Dual x)
    {
        return new(-x.x, -x.dx);
    }

    #region Mixed Operators
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Dual operator +(Dual lhs, double rhs)
    {
        return lhs + new Dual(rhs);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Dual operator +(double lhs, Dual rhs)
    {
        return new Dual(lhs) + rhs;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Dual operator -(Dual lhs, double rhs)
    {
        return lhs - new Dual(rhs);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Dual operator -(double lhs, Dual rhs)
    {
        return new Dual(lhs) - rhs;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Dual operator *(Dual lhs, double rhs)
    {
        return lhs * new Dual(rhs);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Dual operator *(double lhs, Dual rhs)
    {
        return new Dual(lhs) * rhs;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Dual operator /(Dual lhs, double rhs)
    {
        return new(lhs.x / rhs, lhs.dx / rhs);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Dual operator /(double lhs, Dual rhs)
    {
        return new(lhs / rhs.x, -rhs.x * rhs.dx / (rhs.x * rhs.x));
    }
    #endregion

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Dual Sin(Dual v)
    {
        return new(Math.Sin(v.x), v.dx * Math.Cos(v.x));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Dual Cos(Dual v)
    {
        return new(Math.Cos(v.x), -v.dx * Math.Sin(v.x));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static (Dual, Dual) SinCos(Dual v)
    {
        double sinx = Math.Sin(v.x);
        double cosx = Math.Cos(v.x);

        return (new(sinx, cosx * v.dx), new(cosx, -sinx * v.dx));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static (Dual, Dual) SinhCosh(Dual v)
    {
        double sinh = Math.Sinh(v.x);
        double cosh = Math.Cosh(v.x);

        return (new(sinh, cosh * v.dx), new(cosh, sinh * v.dx));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Dual Sinh(Dual v)
    {
        var (sinh, _) = SinhCosh(v);
        return sinh;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Dual Cosh(Dual v)
    {
        var (_, cosh) = SinhCosh(v);
        return cosh;
    }

    public static Dual Atan2(Dual y, Dual x)
    {
        var norm = x.x * x.x + y.x * y.x;

        var v = Math.Atan2(y.x, x.x);
        var dx = (y.x * x.dx - x.x * y.dx) / norm;

        return new(v, dx);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Dual Exp(Dual v)
    {
        var exp = Math.Exp(v.x);
        return new(exp, v.dx * exp);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Dual Log(Dual v)
    {
        return new(Math.Log(v.x), v.dx / v.x);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Dual Pow(Dual x, double p)
    {
        var pm1 = Math.Pow(x.x, p - 1);
        return new(x.x * pm1, x.dx * p * pm1);
    }

    public static Dual Abs(Dual x)
    {
        if (x.x == 0.0)
            return new(0.0, double.PositiveInfinity);

        return new(Math.Abs(x.x), x.dx * Math.Sign(x.x));
    }

    public static Dual Sqrt(Dual x)
    {
        var sqrt = Math.Sqrt(x.x);
        return new(sqrt, 0.5 * x.dx / sqrt);
    }

    public static Dual Sqr(Dual x)
    {
        return new(x.x * x.x, 2 * x.x * x.dx);
    }
}

[DebuggerDisplay("x={x}, dx={dx}, ddx={ddx}")]
[method: MethodImpl(MethodImplOptions.AggressiveInlining)]
internal struct Dual2(double x, double dx = 0.0, double ddx = 0.0)
{
    public double x = x;
    public double dx = dx;
    public double ddx = ddx;

    /// <summary>
    /// Create a new dual number for an initial variable.
    /// </summary>
    /// <param name="value"></param>
    /// <returns></returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Dual2 Variable(double value)
    {
        return new(value, 1.0, 0.0);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Dual2 operator +(Dual2 a, Dual2 b)
    {
        return new(a.x + b.x, a.dx + b.dx, a.ddx + b.ddx);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Dual2 operator -(Dual2 a, Dual2 b)
    {
        return new(a.x - b.x, a.dx - b.dx, a.ddx - b.ddx);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Dual2 operator *(Dual2 a, Dual2 b)
    {
        var x = a.x * b.x;
        var dx = b.x * a.dx + a.x * b.dx;
        var ddx = 2 * a.dx * b.dx + b.x * a.ddx + a.x * b.ddx;

        return new(x, dx, ddx);
    }

    public static Dual2 operator /(Dual2 a, Dual2 b)
    {
        double x = a.x / b.x;
        double dx = (a.dx - x * b.dx) / b.x;
        double ddx = (-2 * b.dx * dx + a.ddx + x * b.ddx) / b.x;

        return new(x, dx, ddx);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Dual2 operator +(Dual2 a, double b)
    {
        return new(a.x + b, a.dx, a.ddx);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Dual2 operator +(double a, Dual2 b)
    {
        return new(a + b.x, b.dx, b.ddx);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Dual2 operator -(Dual2 a, double b)
    {
        return new(a.x - b, a.dx, a.ddx);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Dual2 operator -(double a, Dual2 b)
    {
        return new(a - b.x, b.dx, b.ddx);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Dual2 operator *(Dual2 a, double b)
    {
        return new(a.x * b, a.dx * b, a.ddx * b);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Dual2 operator *(double a, Dual2 b)
    {
        return new(a * b.x, a * b.dx, a * b.ddx);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Dual2 operator /(Dual2 a, double b)
    {
        return new(a.x / b, a.dx / b, a.ddx / b);
    }

    public static Dual2 operator /(double a, Dual2 b)
    {
        double x = a / b.x;
        double dx = -x * b.dx / b.x;
        double ddx = -(2 * dx * b.dx + x * b.ddx) / b.x;

        return new(x, dx, ddx);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Dual2 operator -(Dual2 a)
    {
        return new(-a.x, -a.dx, -a.ddx);
    }

    public static Dual2 Sqrt(Dual2 a)
    {
        var x = Math.Sqrt(a.x);
        var dx = 0.5 * a.dx / x;
        var ddx = (0.5 * a.ddx - dx * dx) / x;

        return new(x, dx, ddx);
    }

    public static Dual2 Abs(Dual2 x)
    {
        if (x.x == 0.0)
            return new(0.0, double.PositiveInfinity, double.PositiveInfinity);

        return new(Math.Abs(x.x), x.dx * Math.Sign(x.x), x.ddx * Math.Sign(x.x));
    }

    public static Dual2 Cos(Dual2 a)
    {
        var cosa = Math.Cos(a.x);
        var sina = Math.Sin(a.x);

        var x = cosa;
        var dx = -sina * a.dx;
        var ddx = -cosa * a.dx * a.dx - sina * a.ddx;

        return new(x, dx, ddx);
    }

    public static Dual2 Sin(Dual2 a)
    {
        var cosa = Math.Cos(a.x);
        var sina = Math.Sin(a.x);

        var x = sina;
        var dx = cosa * a.dx;
        var ddx = -sina * a.dx * a.dx + cosa * a.ddx;

        return new(x, dx, ddx);
    }

    public static (Dual2, Dual2) SinCos(Dual2 a)
    {
        var cosa = Math.Cos(a.x);
        var sina = Math.Sin(a.x);

        var cx = cosa;
        var cdx = -sina * a.dx;
        var cddx = -cosa * a.dx * a.dx - sina * a.ddx;

        var sx = sina;
        var sdx = cosa * a.dx;
        var sddx = -sina * a.dx * a.dx + cosa * a.ddx;

        return (new(sx, sdx, sddx), new(cx, cdx, cddx));
    }

    public static Dual2 Atan2(Dual2 y, Dual2 x)
    {
        var v = Math.Atan2(y.x, x.x);
        var invdenom = 1.0 / (x.x * x.x + y.x * y.x);
        var ddenom = 2.0 * (x.x * x.dx + y.x * y.dx) * invdenom;

        var dv = (y.x * x.dx - x.x * y.dx) * invdenom;
        var ddv = -dv * ddenom + (y.x * x.ddx - x.x * y.ddx) * invdenom;

        return new(v, dv, ddv);
    }

    public static (Dual2, Dual2) SinhCosh(Dual2 v)
    {
        var sinh = Math.Sinh(v.x);
        var cosh = Math.Cosh(v.x);

        var dsinh = new Dual2(sinh, cosh * v.dx, sinh * v.dx * v.dx + cosh * v.ddx);
        var dcosh = new Dual2(cosh, sinh * v.dx, cosh * v.dx * v.dx + sinh * v.ddx);

        return (dsinh, dcosh);
    }

    public static Dual2 Sinh(Dual2 v)
    {
        var sinh = Math.Sinh(v.x);
        var cosh = Math.Cosh(v.x);

        return new Dual2(sinh, cosh * v.dx, sinh * v.dx * v.dx + cosh * v.ddx);
    }

    public static Dual2 Cosh(Dual2 v)
    {
        var sinh = Math.Sinh(v.x);
        var cosh = Math.Cosh(v.x);

        return new Dual2(cosh, sinh * v.dx, cosh * v.dx * v.dx + sinh * v.ddx);
    }
}
