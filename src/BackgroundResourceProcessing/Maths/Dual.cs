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

internal struct DualVector3
{
    public Dual x;
    public Dual y;
    public Dual z;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public DualVector3(Dual x, Dual y, Dual z)
    {
        this.x = x;
        this.y = y;
        this.z = z;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public DualVector3(Vector3d p, Vector3d v)
        : this(new(p.x, v.x), new(p.y, v.y), new(p.z, v.z)) { }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static DualVector3 operator +(DualVector3 l, DualVector3 r)
    {
        return new(l.x + r.x, l.y + r.y, l.z + r.z);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static DualVector3 operator -(DualVector3 l, DualVector3 r)
    {
        return new(l.x - r.x, l.y - r.y, l.z - r.z);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static DualVector3 operator *(DualVector3 v, Dual s)
    {
        return new(v.x * s, v.y * s, v.z * s);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static DualVector3 operator *(Dual s, DualVector3 v)
    {
        return v * s;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static DualVector3 operator /(DualVector3 v, Dual s)
    {
        return new(v.x / s, v.y / s, v.z / s);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static DualVector3 operator -(DualVector3 v)
    {
        return new(-v.x, -v.y, -v.z);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Dual Dot(DualVector3 l, DualVector3 r)
    {
        return l.x * r.x + l.y * r.y + l.z * r.z;
    }

    public static DualVector3 Cross(DualVector3 a, DualVector3 b)
    {
        return new(a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x);
    }
}

internal struct DualQuaternion
{
    public Dual r;
    public DualVector3 v;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public DualQuaternion(Dual r, DualVector3 v)
    {
        this.r = r;
        this.v = v;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public DualQuaternion(Dual r, Dual x, Dual y, Dual z)
        : this(r, new(x, y, z)) { }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static DualQuaternion operator +(DualQuaternion l, DualQuaternion r)
    {
        return new(l.r + r.r, l.v + r.v);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static DualQuaternion operator -(DualQuaternion l, DualQuaternion r)
    {
        return new(l.r - r.r, l.v - r.v);
    }

    public static DualQuaternion operator *(DualQuaternion l, DualQuaternion r)
    {
        return new(
            l.r * r.r - DualVector3.Dot(l.v, r.v),
            l.r * r.v + r.r * l.v + DualVector3.Cross(l.v, r.v)
        );
    }

    public readonly DualQuaternion Inverse()
    {
        var denom = r * r + DualVector3.Dot(v, v);
        return new(r / denom, v / denom);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly DualQuaternion Conjugate()
    {
        return new(r, -v);
    }
}

[DebuggerDisplay("x={x}, dx={dx}, ddx={ddx}")]
[method: MethodImpl(MethodImplOptions.AggressiveInlining)]
internal struct Dual2(double x, double dx, double ddx)
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
        return new(a.x * b.x, b.x * a.dx + a.x * b.dx, 2 * a.dx * b.dx + b.x * a.ddx + a.x * b.ddx);
    }

    public static Dual2 operator /(Dual2 a, Dual2 b)
    {
        double x = a.x / b.x;
        double dx = (a.x * b.dx - b.x * a.dx) / (b.x * b.x);
        double ddx = (-2 * b.dx * dx / b.x) + (b.x * a.ddx - a.x * b.ddx) / (b.x * b.x);

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
}
