namespace BackgroundResourceProcessing.Test;

public static class AssertUtils
{
    public static void AreEqual(Vector3d expected, Vector3d actual, double epsilon = 1e-6)
    {
        Assert.AreEqual(expected, actual, new Vector3dEqualityComparer(epsilon));
    }

    private class Vector3dEqualityComparer(double epsilon) : IEqualityComparer<Vector3d>
    {
        public bool Equals(Vector3d x, Vector3d y)
        {
            return (x - y).magnitude < epsilon;
        }

        public int GetHashCode(Vector3d obj)
        {
            throw new NotImplementedException();
        }
    }

    public static void SequenceEqual<T>(IEnumerable<T> expected, IEnumerable<T> actual)
    {
        Assert.AreEqual(
            expected,
            actual,
            new SequenceEqualityComparer<T>(),
            $"[{string.Join(", ", expected)}] != [{string.Join(", ", actual)}]"
        );
    }

    public static void SequenceEqual<T>(
        IEnumerable<T> expected,
        IEnumerable<T> actual,
        string message
    )
    {
        Assert.AreEqual(
            expected,
            actual,
            new SequenceEqualityComparer<T>(),
            $"{message}: [{string.Join(", ", expected)}] != [{string.Join(", ", actual)}]"
        );
    }

    private class SequenceEqualityComparer<T> : IEqualityComparer<IEnumerable<T>>
    {
        public bool Equals(IEnumerable<T> x, IEnumerable<T> y)
        {
            return Enumerable.SequenceEqual(x, y);
        }

        public int GetHashCode(IEnumerable<T> obj)
        {
            throw new NotImplementedException();
        }
    }
}
