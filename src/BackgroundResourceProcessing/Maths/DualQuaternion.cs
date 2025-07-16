using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace BackgroundResourceProcessing.Maths;

[method: MethodImpl(MethodImplOptions.AggressiveInlining)]
internal struct DualQuaternion(Dual r, DualVector3 v)
{
    public Dual r = r;
    public DualVector3 v = v;

    public DualQuaternion(Dual r, Dual x, Dual y, Dual z)
        : this(r, new(x, y, z)) { }

    public static DualQuaternion operator +(DualQuaternion l, DualQuaternion r)
    {
        return new(l.r + r.r, l.v + r.v);
    }

    public static DualQuaternion operator -(DualQuaternion l, DualQuaternion r)
    {
        return new(l.r - r.r, l.v - r.v);
    }

    public static DualQuaternion operator *(DualQuaternion a, DualQuaternion b)
    {
        return new(
            a.r * b.r - DualVector3.Dot(a.v, b.v),
            a.r * b.v + b.r * a.v + DualVector3.Cross(a.v, b.v)
        );
    }

    public static DualQuaternion operator /(DualQuaternion a, Dual b)
    {
        return new(a.r / b, a.v / b);
    }

    public readonly DualVector3 Rotate(DualVector3 u)
    {
        var q = Normalized();
        return (q.Conjugate() * new DualQuaternion(new(0), u) * q).v;
    }

    public readonly DualQuaternion Normalized()
    {
        var denom = r * r + DualVector3.Dot(v, v);
        return new(r / denom, v / denom);
    }

    public readonly DualQuaternion Inverse()
    {
        var denom = r * r + DualVector3.Dot(v, v);
        return new(r / denom, -v / denom);
    }

    public readonly DualQuaternion Conjugate()
    {
        return new(r, -v);
    }

    public static DualQuaternion FromAngleAxis(Dual angle, DualVector3 axis)
    {
        var (sin, cos) = Dual.SinCos(angle * 0.5);
        return new(cos, axis * sin);
    }
}

[method: MethodImpl(MethodImplOptions.AggressiveInlining)]
internal struct Dual2Quaternion(Dual2 r, Dual2Vector3 v)
{
    public Dual2 r = r;
    public Dual2Vector3 v = v;

    public Dual2Quaternion(Dual2 r, Dual2 x, Dual2 y, Dual2 z)
        : this(r, new(x, y, z)) { }

    public static Dual2Quaternion operator +(Dual2Quaternion l, Dual2Quaternion r)
    {
        return new(l.r + r.r, l.v + r.v);
    }

    public static Dual2Quaternion operator -(Dual2Quaternion l, Dual2Quaternion r)
    {
        return new(l.r - r.r, l.v - r.v);
    }

    public static Dual2Quaternion operator *(Dual2Quaternion a, Dual2Quaternion b)
    {
        return new(
            a.r * b.r - Dual2Vector3.Dot(a.v, b.v),
            a.r * b.v + b.r * a.v + Dual2Vector3.Cross(a.v, b.v)
        );
    }

    public static Dual2Quaternion operator /(Dual2Quaternion a, Dual2 b)
    {
        return new(a.r / b, a.v / b);
    }

    public readonly Dual2Vector3 Rotate(Dual2Vector3 u)
    {
        var q = Normalized();
        return (q.Conjugate() * new Dual2Quaternion(new(0), u) * q).v;
    }

    public readonly Dual2Quaternion Normalized()
    {
        var denom = r * r + Dual2Vector3.Dot(v, v);
        return new(r / denom, v / denom);
    }

    public readonly Dual2Quaternion Inverse()
    {
        var denom = r * r + Dual2Vector3.Dot(v, v);
        return new(r / denom, -v / denom);
    }

    public readonly Dual2Quaternion Conjugate()
    {
        return new(r, -v);
    }

    public static Dual2Quaternion FromAngleAxis(Dual2 angle, Dual2Vector3 axis)
    {
        var (sin, cos) = Dual2.SinCos(angle * 0.5);
        return new(cos, axis * sin);
    }
}
