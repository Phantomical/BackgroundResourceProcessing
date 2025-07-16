using System;
using System.Diagnostics;
using BackgroundResourceProcessing.Utils;
using static BackgroundResourceProcessing.Utils.MathUtil;

namespace BackgroundResourceProcessing.Maths;

[DebuggerDisplay("{r} + {v.x}i + {v.y}j + {v.z}k")]
internal struct QuaternionD(double r, Vector3d v)
{
    public double r = r;
    public Vector3d v = v;

    public readonly double this[int index] =>
        index switch
        {
            0 => r,
            1 => v.x,
            2 => v.y,
            3 => v.z,
            _ => ThrowIndexOutOfBoundsException(index),
        };

    private static double ThrowIndexOutOfBoundsException(int index)
    {
        throw new ArgumentOutOfRangeException(
            $"index must be a value in the range 0 to 3, got {index} instead"
        );
    }

    public QuaternionD(double r, double x, double y, double z)
        : this(r, new(x, y, z)) { }

    public static QuaternionD operator +(QuaternionD a, QuaternionD b)
    {
        return new(a.r + b.r, a.v + b.v);
    }

    public static QuaternionD operator -(QuaternionD a, QuaternionD b)
    {
        return new(a.r - b.r, a.v - b.v);
    }

    /// <summary>
    /// Computes the quaternion representing the rotation
    /// <paramref name="b"/> followed by <paramref name="a"/>.
    /// </summary>
    public static QuaternionD operator *(QuaternionD a, QuaternionD b)
    {
        return new(
            a.r * b.r - Vector3d.Dot(a.v, b.v),
            a.r * b.v + b.r * a.v + Vector3d.Cross(a.v, b.v)
        );
    }

    public static QuaternionD operator *(QuaternionD a, double b)
    {
        return new(a.r * b, a.v * b);
    }

    public static QuaternionD operator *(double a, QuaternionD b)
    {
        return new(a * b.r, a * b.v);
    }

    public static QuaternionD operator /(QuaternionD a, double b)
    {
        return new(a.r / b, a.v / b);
    }

    public readonly Vector3d Rotate(Vector3d u)
    {
        var q = Normalized();
        return (q.Conjugate() * new QuaternionD(0, u) * q).v;
    }

    public readonly QuaternionD Inverse()
    {
        var denom = SqrNorm();
        return new(r / denom, -v / denom);
    }

    public readonly QuaternionD Conjugate()
    {
        return new(r, -v);
    }

    public readonly double Norm()
    {
        return Math.Sqrt(SqrNorm());
    }

    public readonly double SqrNorm()
    {
        return r * r + Vector3d.Dot(v, v);
    }

    public readonly QuaternionD Normalized()
    {
        var denom = r * r + Vector3d.Dot(v, v);
        return new(r / denom, v / denom);
    }

    public static QuaternionD FromAngleAxis(double angle, Vector3d axis)
    {
        var (sin, cos) = MathUtil.SinCos(angle * 0.5);
        return new(cos, axis * sin);
    }

    public readonly void ToAngleAxis(out double angle, out Vector3d axis)
    {
        var norm = v.magnitude;

        angle = 2 * Math.Atan2(norm, r);
        axis = v / norm;
    }

    /// <summary>
    /// Give the rotation that transforms from the basis given by
    /// <paramref name="x"/>, <paramref name="y"/>, and <paramref name="z"/>
    /// into the standard basis.
    /// </summary>
    /// <param name="x"></param>
    /// <param name="y"></param>
    /// <param name="z"></param>
    /// <returns></returns>
    public static QuaternionD FromBasis(Vector3d x, Vector3d y, Vector3d z)
    {
        return FromUnity(new UnityEngine.QuaternionD(x, y, z));

        // // See: https://en.wikipedia.org/wiki/Rotation_matrix#Quaternion

        // var t = x.x + y.y + z.z;

        // if (t >= 0)
        // {
        //     var r = Math.Sqrt(1 + t);
        //     var s = 0.5 / r;

        //     return new QuaternionD(
        //         0.5 * r,
        //         s * (z.y - y.z),
        //         s * (x.z - z.x),
        //         s * (y.x - x.y)
        //     ).Normalized();
        // }
        // else
        // {
        //     var r = Math.Sqrt(1 + x.x - y.y - z.z);
        //     var s = 0.5 / r;

        //     return new QuaternionD(
        //         s * (z.y - y.z),
        //         0.5 * r,
        //         s * (x.y + y.x),
        //         s * (z.x + x.z)
        //     ).Normalized();
        // }
    }

    public static QuaternionD ToBasis(Vector3d x, Vector3d y, Vector3d z)
    {
        return FromBasis(x, y, z).Inverse();
    }

    public static QuaternionD FromUnity(UnityEngine.QuaternionD q)
    {
        return new(q.w, q.x, q.y, q.z);
    }

    public static QuaternionD EulerAngleX(double angle)
    {
        return FromAngleAxis(angle, new(1.0, 0.0, 0.0));
    }

    public static QuaternionD EulerAngleY(double angle)
    {
        return FromAngleAxis(angle, new(0.0, 1.0, 0.0));
    }

    public static QuaternionD EulerAngleZ(double angle)
    {
        return FromAngleAxis(angle, new(0.0, 0.0, 1.0));
    }

    public readonly (double, double, double) ToEulerAngles313(double originalTheta3 = 0.0)
    {
        return ToEulerAngles(3, 1, 3, originalTheta3);
    }

    public readonly (double, double, double) ToEulerAngles212(double originalTheta3 = 0.0)
    {
        return ToEulerAngles(2, 1, 2, originalTheta3);
    }

    private readonly (double, double, double) ToEulerAngles(
        int i,
        int j,
        int k,
        double originalTheta3 = 0.0
    )
    {
        // This method comes from chapter 10 of https://theses.hal.science/tel-04646218

#if DEBUG
        // These are useful but generally cause extra overhead, so we remove
        // them in release mode.
        if (i < 0 || i > 3)
            throw new ArgumentException($"index i out of range (got {i})");
        if (j < 0 || j > 3)
            throw new ArgumentException($"index j out of range (got {j})");
        if (k < 0 || k > 3)
            throw new ArgumentException($"index k out of range (got {k})");
        if (i == j)
            throw new ArgumentException($"index i cannot be equal to index j");
        if (j == k)
            throw new ArgumentException($"index j cannot be equal to index k");
#endif

        bool proper = i == k;
        if (proper)
            k = 6 - i - j;

        int sign = (i - j) * (j - k) * (k - i) / 2;
        double a;
        double b;
        double c;
        double d;

        if (!proper)
        {
            a = this[0] - this[j];
            b = this[i] + this[k] * sign;
            c = this[j] + this[0];
            d = this[k] * sign - this[i];
        }
        else
        {
            a = this[0];
            b = this[i];
            c = this[j];
            d = this[k] * sign;
        }

        double theta2 = 2 * Math.Atan2(Math.Sqrt(c * c + d * d), Math.Sqrt(a * a + b * b));
        double thetap = Math.Atan2(b, a);
        double thetam = Math.Atan2(d, c);

        double theta1;
        double theta3;

        if (ApproxEqual(theta2, 0.0))
        {
            theta3 = originalTheta3;
            theta1 = 2 * thetap - theta3;
        }
        else if (ApproxEqual(theta2, Math.PI / 2))
        {
            theta3 = originalTheta3;
            theta1 = 2 * thetam + theta3;
        }
        else
        {
            theta1 = thetap - thetam;
            theta3 = thetap + thetam;
        }

        if (!proper)
        {
            theta3 *= sign;
            theta2 -= Math.PI / 2;
        }

        theta1 = FMod(theta1, 2 * Math.PI);
        theta3 = FMod(theta3, 2 * Math.PI);

        return (theta1, theta2, theta3);
    }

    /// <summary>
    /// Available for use as a debugging aid.
    /// </summary>
    private readonly AngleAxis AsAngleAxis
    {
        get
        {
            ToAngleAxis(out var angle, out var axis);
            return new() { angle = angle, axis = axis };
        }
    }

    private readonly QuaternionD AsInverse => Inverse();

    [DebuggerDisplay("angle={angle}, axis={axis}")]
    struct AngleAxis
    {
        public double angle;
        public Vector3d axis;
    }
}
