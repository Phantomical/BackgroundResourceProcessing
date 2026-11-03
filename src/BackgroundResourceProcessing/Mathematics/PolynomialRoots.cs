using System;
using BackgroundResourceProcessing.Utils;
using Unity.Burst.CompilerServices;

namespace BackgroundResourceProcessing.Mathematics;

/// <summary>
/// Real roots of low-degree polynomials.
/// </summary>
///
/// <remarks>
/// Roots are found by bracketing: the real roots of a polynomial's derivative
/// split the real line into intervals on which the polynomial is monotonic, so
/// each of those intervals contains at most one root and it can be found by
/// bisection. Starting from the linear derivative and working up gives every
/// real root.
/// </remarks>
internal static unsafe class PolynomialRoots
{
    public const int MaxDegree = 4;

    const int Stride = MaxDegree + 1;
    const int MaxBisectIter = 256;

    /// <summary>
    /// Find the real roots of <c>c[0] + c[1]x + ... + c[degree]x^degree</c>.
    /// </summary>
    ///
    /// <param name="c">The coefficients, lowest degree first.</param>
    /// <param name="degree">The degree, at most <see cref="MaxDegree"/>.</param>
    /// <param name="roots">
    /// Receives the roots in increasing order. Must have room for
    /// <paramref name="degree"/> entries.
    /// </param>
    /// <returns>The number of roots found.</returns>
    ///
    /// <remarks>
    /// A root where the polynomial touches zero without changing sign may be
    /// missed. Leading coefficients that are negligible compared to the others
    /// are dropped.
    /// </remarks>
    [SkipLocalsInit]
    public static int Solve(double* c, int degree, double* roots)
    {
        if (degree > MaxDegree)
            degree = MaxDegree;

        double scale = 0.0;
        for (int i = 0; i <= degree; ++i)
            scale = Math.Max(scale, Math.Abs(c[i]));
        if (!(scale > 0.0) || !MathUtil.IsFinite(scale))
            return 0;

        while (degree > 0 && Math.Abs(c[degree]) <= 1e-14 * scale)
            degree -= 1;
        if (degree == 0)
            return 0;

        // polys[k * Stride + i] is coefficient i of the derivative of degree k.
        double* polys = stackalloc double[Stride * Stride];
        for (int i = 0; i <= degree; ++i)
            polys[degree * Stride + i] = c[i];
        for (int k = degree; k > 1; --k)
        {
            for (int i = 0; i < k; ++i)
                polys[(k - 1) * Stride + i] = (i + 1) * polys[k * Stride + i + 1];
        }

        double* prev = stackalloc double[MaxDegree];
        double* next = stackalloc double[MaxDegree];
        int prevCount = 1;
        prev[0] = -polys[Stride] / polys[Stride + 1];

        for (int k = 2; k <= degree; ++k)
        {
            double* a = polys + k * Stride;

            // Cauchy's bound: every root is within this distance of 0.
            double bound = 0.0;
            for (int i = 0; i < k; ++i)
                bound = Math.Max(bound, Math.Abs(a[i] / a[k]));
            bound += 1.0;

            int count = 0;
            double lo = -bound;
            for (int j = 0; j <= prevCount; ++j)
            {
                double hi = j < prevCount ? MathUtil.Clamp(prev[j], -bound, bound) : bound;
                if (hi > lo && Bracket(a, k, lo, hi, out var root))
                    next[count++] = root;
                lo = Math.Max(lo, hi);
            }

            var tmp = prev;
            prev = next;
            next = tmp;
            prevCount = count;
        }

        for (int i = 0; i < prevCount; ++i)
            roots[i] = prev[i];
        return prevCount;
    }

    static double Evaluate(double* a, int degree, double x)
    {
        double value = a[degree];
        for (int i = degree - 1; i >= 0; --i)
            value = value * x + a[i];
        return value;
    }

    /// <summary>
    /// Find the root in <c>[lo, hi)</c> of a polynomial that is monotonic on
    /// that interval, if there is one.
    /// </summary>
    static bool Bracket(double* a, int degree, double lo, double hi, out double root)
    {
        var flo = Evaluate(a, degree, lo);
        var fhi = Evaluate(a, degree, hi);
        root = lo;

        if (flo == 0.0)
            return true;
        // A root exactly at hi is found as the start of the next interval.
        if (fhi == 0.0 || (flo < 0.0) == (fhi < 0.0))
            return false;
        if (!MathUtil.IsFinite(flo) || !MathUtil.IsFinite(fhi))
            return false;

        bool negLo = flo < 0.0;
        for (int i = 0; i < MaxBisectIter; ++i)
        {
            var mid = 0.5 * (lo + hi);
            if (!(mid > lo && mid < hi))
                break;

            if ((Evaluate(a, degree, mid) < 0.0) == negLo)
                lo = mid;
            else
                hi = mid;
        }

        root = 0.5 * (lo + hi);
        return true;
    }
}
