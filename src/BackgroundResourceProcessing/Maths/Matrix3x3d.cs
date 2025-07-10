using System.Security.Policy;

namespace BackgroundResourceProcessing.Maths;

internal struct Matrix3x3d(Vector3d x, Vector3d y, Vector3d z)
{
    public Vector3d x = x;
    public Vector3d y = y;
    public Vector3d z = z;

    public static Matrix3x3d Identity => new(new(1, 0, 0), new(0, 1, 0), new(0, 0, 1));

    public readonly QuaternionD Rotation => QuaternionD.FromBasis(x, y, z);

    public readonly Matrix3x3d Transpose =>
        new(new(x.x, y.x, z.x), new(x.y, y.y, z.y), new(x.z, y.z, z.z));
    public readonly Matrix3x3d Inverse => ComputeInverse();

    public Matrix3x3d(Planetarium.CelestialFrame frame)
        : this(frame.X, frame.Y, frame.Z) { }

    public static Vector3d operator *(Matrix3x3d a, Vector3d b)
    {
        return new(Vector3d.Dot(a.x, b), Vector3d.Dot(a.y, b), Vector3d.Dot(a.z, b));
    }

    public static Matrix3x3d operator *(Matrix3x3d a, double b)
    {
        return new(a.x * b, a.y * b, a.z * b);
    }

    public static Matrix3x3d operator *(double a, Matrix3x3d b)
    {
        return b * a;
    }

    public static Matrix3x3d operator *(Matrix3x3d a, Matrix3x3d b)
    {
        var t = b.Transpose;
        return new Matrix3x3d(a * t.x, a * t.y, a * t.z).Transpose;
    }

    private readonly Matrix3x3d ComputeInverse()
    {
        Matrix3x3d adj = new(
            new(Det2x2(y.y, y.z, z.y, z.z), Det2x2(x.z, x.y, z.z, z.y), Det2x2(x.y, x.z, y.y, y.z)),
            new(Det2x2(y.z, y.x, z.z, z.x), Det2x2(x.x, x.z, z.x, z.z), Det2x2(x.z, x.x, y.z, y.x)),
            new(Det2x2(y.x, y.y, z.x, z.y), Det2x2(x.y, x.x, z.y, z.x), Det2x2(x.x, x.y, y.x, y.y))
        );
        double det = Vector3d.Dot(x, adj.x);

        return adj * (1.0 / det);
    }

    private static double Det2x2(double a, double b, double c, double d)
    {
        return a * d - b * c;
    }
}
