using System.Runtime.CompilerServices;

namespace BackgroundResourceProcessing.Maths;

[method: MethodImpl(MethodImplOptions.AggressiveInlining)]
internal struct DualVector3(Dual x, Dual y, Dual z)
{
    public Vector3d v = new(x.x, y.x, z.x);
    public Vector3d dv = new(x.dx, y.dx, z.dx);

    public readonly Dual x => new(v.x, dv.x);
    public readonly Dual y => new(v.y, dv.y);
    public readonly Dual z => new(v.z, dv.z);

    public readonly DualVector3 xzy => new(x, z, y);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public DualVector3(Vector3d p, Vector3d v = default)
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

    public readonly Dual Magnitude()
    {
        return Dual.Sqrt(Dot(this, this));
    }

    public readonly DualVector3 Normalized()
    {
        return this / Magnitude();
    }

    public readonly Dx Derivatives => new() { x = new(x.x, y.x, z.x), dx = new(x.dx, y.dx, z.dx) };

    public struct Dx
    {
        public Vector3d x;
        public Vector3d dx;
    }
}

[method: MethodImpl(MethodImplOptions.AggressiveInlining)]
internal struct Dual2Vector3(Dual2 x, Dual2 y, Dual2 z)
{
    public Vector3d v = new(x.x, y.x, z.x);
    public Vector3d dv = new(x.dx, y.dx, z.dx);
    public Vector3d ddv = new(x.ddx, y.ddx, z.ddx);

    public readonly Dual2 x => new(v.x, dv.x, ddv.x);
    public readonly Dual2 y => new(v.y, dv.y, ddv.y);
    public readonly Dual2 z => new(v.z, dv.z, ddv.z);

    public readonly Dual2Vector3 xzy => new(x, z, y);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Dual2Vector3(Vector3d p, Vector3d dp = default, Vector3d ddp = default)
        : this(
            new Dual2(p.x, dp.x, ddp.x),
            new Dual2(p.y, dp.y, ddp.y),
            new Dual2(p.z, dp.z, ddp.z)
        ) { }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Dual2Vector3 operator +(Dual2Vector3 l, Dual2Vector3 r)
    {
        return new(l.x + r.x, l.y + r.y, l.z + r.z);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Dual2Vector3 operator -(Dual2Vector3 l, Dual2Vector3 r)
    {
        return new(l.x - r.x, l.y - r.y, l.z - r.z);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Dual2Vector3 operator *(Dual2Vector3 v, Dual2 s)
    {
        return new(v.x * s, v.y * s, v.z * s);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Dual2Vector3 operator *(Dual2 s, Dual2Vector3 v)
    {
        return v * s;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Dual2Vector3 operator /(Dual2Vector3 v, Dual2 s)
    {
        return new(v.x / s, v.y / s, v.z / s);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Dual2Vector3 operator -(Dual2Vector3 v)
    {
        return new(-v.x, -v.y, -v.z);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Dual2 Dot(Dual2Vector3 l, Dual2Vector3 r)
    {
        return l.x * r.x + l.y * r.y + l.z * r.z;
    }

    public static Dual2Vector3 Cross(Dual2Vector3 a, Dual2Vector3 b)
    {
        return new(a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x);
    }

    public readonly Dual2 Magnitude()
    {
        return Dual2.Sqrt(Dot(this, this));
    }

    public readonly Dual2Vector3 Normalized()
    {
        return this / Magnitude();
    }

    public readonly Dx Derivatives =>
        new()
        {
            x = new(x.x, y.x, z.x),
            dx = new(x.dx, y.dx, z.dx),
            ddx = new(x.ddx, y.ddx, z.ddx),
        };

    public struct Dx
    {
        public Vector3d x;
        public Vector3d dx;
        public Vector3d ddx;
    }
}
