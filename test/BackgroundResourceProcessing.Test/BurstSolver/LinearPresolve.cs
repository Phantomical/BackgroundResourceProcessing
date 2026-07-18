using BackgroundResourceProcessing.BurstSolver;
using BackgroundResourceProcessing.Collections.Burst;
using KSP.Testing;

namespace BackgroundResourceProcessing.Test.BurstSolver;

/// <summary>
/// Tests for <c>LinearPresolve.InferConstants</c>, which is the presolve step
/// that spots inequality rows of the form <c>a*x &lt;= b</c> (a single positive
/// coefficient, a positive constant, and a positive objective coefficient) and
/// promotes them to equalities.
/// </summary>
///
/// <remarks>
/// These all use at least 8 variables on purpose. <c>InferConstants</c> builds
/// its scratch <c>indices</c> array with an AVX2 loop that steps 8 columns at a
/// time and then falls back to a scalar loop for whatever is left over, so a
/// problem with fewer than 8 variables never executes the vectorized code at
/// all and the two implementations can silently disagree.
///
/// Note that the AVX2 path is only reached when the mod is built with
/// <c>BURST_TEST_OVERRIDE_AVX2</c> (or is genuinely burst-compiled).
/// <c>X86.Avx2.IsAvx2Supported</c> is false under plain managed execution, so
/// in a default build these tests only cover the scalar path.
/// </remarks>
public sealed class LinearPresolveTest : BRPTestBase
{
    /// <summary>
    /// Run presolve over <paramref name="rows"/> (each entry being a constraint
    /// row whose last element is the constant) plus <paramref name="objective"/>.
    /// </summary>
    ///
    /// <remarks>
    /// Every row starts out as an inequality. Each one is given a distinct
    /// constant so that <see cref="Constants"/> can identify rows after
    /// <c>InferConstants</c> has reordered them.
    /// </remarks>
    private static Matrix Presolve(
        double[][] rows,
        double[] objective,
        out int equalities,
        out int inequalities,
        out bool found
    )
    {
        var allocator = AllocatorHandle.Temp;
        int cols = objective.Length;
        var matrix = new Matrix(rows.Length + 1, cols, allocator);

        for (int y = 0; y < rows.Length; ++y)
        {
            for (int x = 0; x < cols; ++x)
                matrix[y, x] = rows[y][x];
        }
        for (int x = 0; x < cols; ++x)
            matrix[rows.Length, x] = objective[x];

        var words = BitSet.WordsRequired(cols - 1);
        var zeros = new BitSpan(new RawArray<ulong>(words, allocator).Span);

        equalities = 0;
        inequalities = rows.Length;

        found = LinearPresolve
            .Presolve(matrix, zeros, ref equalities, ref inequalities, allocator)
            .Unwrap();

        return matrix;
    }

    /// <summary>
    /// The constant column of every constraint row, in row order. Used to check
    /// how <c>InferConstants</c> permuted the rows.
    /// </summary>
    private static double[] Constants(Matrix matrix)
    {
        var constants = new double[matrix.Rows - 1];
        for (int y = 0; y < constants.Length; ++y)
            constants[y] = matrix[y, matrix.Cols - 1];
        return constants;
    }

    private static void AssertRowOrder(double[] expected, Matrix matrix)
    {
        var actual = Constants(matrix);
        Assert.AreEqual(expected.Length, actual.Length);

        for (int y = 0; y < expected.Length; ++y)
            Assert.AreEqual(expected[y], actual[y]);
    }

    /// <summary>
    /// Exactly 8 variables, so the AVX2 loop runs once and the scalar tail runs
    /// not at all.
    /// </summary>
    ///
    /// <remarks>
    /// Columns 0, 1 and 4 are each touched by exactly one row and those rows
    /// have a single coefficient, so all three get promoted. Columns 2 and 3
    /// share a row with two coefficients, and 5-7 are empty, so neither gets
    /// promoted.
    /// </remarks>
    [TestInfo("LinearPresolveTest_TestInferConstants_ExactlyOneVector")]
    public void TestInferConstants_ExactlyOneVector()
    {
        var matrix = Presolve(
            [
                //x0 x1 x2 x3 x4 x5 x6 x7  c
                [2, 0, 0, 0, 0, 0, 0, 0, 10],
                [0, 1, 0, 0, 0, 0, 0, 0, 5],
                [0, 0, 1, 1, 0, 0, 0, 0, 7],
                [0, 0, 0, 0, 3, 0, 0, 0, 9],
            ],
            [1, 1, 1, 1, 1, 1, 1, 1, 0],
            out var equalities,
            out var inequalities,
            out var found
        );

        Assert.IsTrue(found);
        Assert.AreEqual(3, equalities);
        Assert.AreEqual(1, inequalities);

        // The three promoted rows are moved to the front, in row order, which
        // pushes the two-coefficient row to the end.
        AssertRowOrder([10, 5, 9, 7], matrix);

        // Promoted columns are removed from the objective.
        Assert.AreEqual(0.0, matrix[4, 0]);
        Assert.AreEqual(0.0, matrix[4, 1]);
        Assert.AreEqual(0.0, matrix[4, 4]);
        Assert.AreEqual(1.0, matrix[4, 2]);
        Assert.AreEqual(1.0, matrix[4, 3]);
        Assert.AreEqual(1.0, matrix[4, 5]);
    }

    /// <summary>
    /// 11 variables, so the AVX2 loop covers columns 0-7 and the scalar tail
    /// covers 8-10. This is the case that catches the two halves disagreeing.
    /// </summary>
    ///
    /// <remarks>
    /// Column 1 is promoted from within the vectorized range and column 9 from
    /// within the scalar tail. Column 5 is deliberately shared between two rows
    /// so that it has to be marked ambiguous rather than attributed to either
    /// one -- that is the branch the vector code implements with a blend rather
    /// than a comparison, so it is worth pinning down.
    /// </remarks>
    [TestInfo("LinearPresolveTest_TestInferConstants_VectorAndScalarTail")]
    public void TestInferConstants_VectorAndScalarTail()
    {
        var matrix = Presolve(
            [
                //x0 x1 x2 x3 x4 x5 x6 x7 x8 x9 x10  c
                [0, 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 10],
                [0, 0, 0, 0, 1, 1, 0, 0, 0, 0, 0, 7],
                [0, 0, 0, 0, 0, 3, 0, 0, 0, 0, 0, 9],
                [0, 0, 0, 0, 0, 0, 0, 0, 0, 2, 0, 6],
                [0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 1, 4],
            ],
            [1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 0],
            out var equalities,
            out var inequalities,
            out var found
        );

        Assert.IsTrue(found);
        Assert.AreEqual(2, equalities);
        Assert.AreEqual(3, inequalities);

        // Rows 0 and 3 are promoted; promoting row 3 swaps it with row 1.
        AssertRowOrder([10, 6, 9, 7, 4], matrix);

        Assert.AreEqual(0.0, matrix[5, 1]);
        Assert.AreEqual(0.0, matrix[5, 9]);

        // Column 5 appears in two rows, so it is ambiguous and stays put even
        // though row 2 on its own looks like a promotable simple bound.
        Assert.AreEqual(1.0, matrix[5, 5]);
        // Columns 2 and 10 share a row, so neither is a simple bound.
        Assert.AreEqual(1.0, matrix[5, 2]);
        Assert.AreEqual(1.0, matrix[5, 10]);
    }

    /// <summary>
    /// 16 variables, so the AVX2 loop runs twice and the scalar tail runs not at
    /// all. This covers a promotion in the second vector iteration, which a
    /// single-iteration test cannot distinguish from a loop that only ever runs
    /// once.
    /// </summary>
    [TestInfo("LinearPresolveTest_TestInferConstants_MultipleVectors")]
    public void TestInferConstants_MultipleVectors()
    {
        var matrix = Presolve(
            [
                [1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 3],
                [0, 0, 0, 0, 0, 0, 0, 5, 0, 0, 0, 0, 0, 0, 0, 0, 20],
                [0, 0, 0, 0, 0, 0, 0, 0, 4, 0, 0, 0, 0, 0, 0, 0, 8],
                [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 2, 12],
                [0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 5],
                [0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 6],
            ],
            [1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 0],
            out var equalities,
            out var inequalities,
            out var found
        );

        Assert.IsTrue(found);
        Assert.AreEqual(4, equalities);
        Assert.AreEqual(2, inequalities);

        // Rows 0-3 are already at the front, so the swaps are all no-ops.
        AssertRowOrder([3, 20, 8, 12, 5, 6], matrix);

        // Columns 0 and 7 are in the first vector iteration, 8 and 15 the second.
        Assert.AreEqual(0.0, matrix[6, 0]);
        Assert.AreEqual(0.0, matrix[6, 7]);
        Assert.AreEqual(0.0, matrix[6, 8]);
        Assert.AreEqual(0.0, matrix[6, 15]);

        // Column 1 is shared between rows 4 and 5, and column 14 shares a row
        // with column 1, so neither is promoted.
        Assert.AreEqual(1.0, matrix[6, 1]);
        Assert.AreEqual(1.0, matrix[6, 14]);
    }
}
