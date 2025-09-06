using System;
using BackgroundResourceProcessing.Collections.Burst;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Unity.Collections;

namespace BackgroundResourceProcessing.Test.Collections.Burst;

[TestClass]
public sealed class MatrixTest
{
    TestAllocator.TestGuard? guard = null;

    [TestInitialize]
    public void Init()
    {
        guard = new TestAllocator.TestGuard();
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (guard is null)
            return;

        guard.Value.Dispose();
        guard = null;
    }

    [TestMethod]
    public void Constructor_ValidDimensions_CreatesMatrix()
    {
        using var matrix = new Matrix(2, 3, Allocator.Temp);

        Assert.AreEqual(2, matrix.Rows);
        Assert.AreEqual(3, matrix.Cols);
        Assert.AreEqual(Allocator.Temp, matrix.Allocator);
    }

    [TestMethod]
    public void Constructor_ZeroDimensions_CreatesEmptyMatrix()
    {
        using var matrix = new Matrix(0, 0, Allocator.Temp);

        Assert.AreEqual(0, matrix.Rows);
        Assert.AreEqual(0, matrix.Cols);
    }

    [TestMethod]
    public void Constructor_NegativeRows_ThrowsArgumentOutOfRangeException()
    {
        Assert.ThrowsException<ArgumentOutOfRangeException>(() => new Matrix(-1, 3, Allocator.Temp)
        );
    }

    [TestMethod]
    public void Constructor_NegativeCols_ThrowsArgumentOutOfRangeException()
    {
        Assert.ThrowsException<ArgumentOutOfRangeException>(() => new Matrix(2, -1, Allocator.Temp)
        );
    }

    [TestMethod]
    public void Constructor_ArrayTooSmall_ThrowsArgumentException()
    {
        using var array = new RawArray<double>(5, Allocator.Temp);

        Assert.ThrowsException<ArgumentException>(() => new Matrix(array, 2, 3));
    }

    [TestMethod]
    public void Indexer_ValidIndices_SetsAndGetsValues()
    {
        using var matrix = new Matrix(2, 3, Allocator.Temp);

        matrix[0, 0] = 1.0;
        matrix[1, 2] = 5.0;

        Assert.AreEqual(1.0, matrix[0, 0]);
        Assert.AreEqual(5.0, matrix[1, 2]);
        Assert.AreEqual(0.0, matrix[0, 1]); // Default value
    }

    [TestMethod]
    public void Indexer_NegativeRowIndex_ThrowsIndexOutOfRangeException()
    {
        using var matrix = new Matrix(2, 3, Allocator.Temp);

        Assert.ThrowsException<IndexOutOfRangeException>(() => _ = matrix[-1, 0]);
        Assert.ThrowsException<IndexOutOfRangeException>(() => matrix[-1, 0] = 1.0);
    }

    [TestMethod]
    public void Indexer_NegativeColIndex_ThrowsIndexOutOfRangeException()
    {
        using var matrix = new Matrix(2, 3, Allocator.Temp);

        Assert.ThrowsException<IndexOutOfRangeException>(() => _ = matrix[0, -1]);
        Assert.ThrowsException<IndexOutOfRangeException>(() => matrix[0, -1] = 1.0);
    }

    [TestMethod]
    public void Indexer_RowOutOfBounds_ThrowsIndexOutOfRangeException()
    {
        using var matrix = new Matrix(2, 3, Allocator.Temp);

        Assert.ThrowsException<IndexOutOfRangeException>(() => _ = matrix[2, 0]);
        Assert.ThrowsException<IndexOutOfRangeException>(() => matrix[2, 0] = 1.0);
    }

    [TestMethod]
    public void Indexer_ColOutOfBounds_ThrowsIndexOutOfRangeException()
    {
        using var matrix = new Matrix(2, 3, Allocator.Temp);

        Assert.ThrowsException<IndexOutOfRangeException>(() => _ = matrix[0, 3]);
        Assert.ThrowsException<IndexOutOfRangeException>(() => matrix[0, 3] = 1.0);
    }

    [TestMethod]
    public void RowIndexer_ValidIndex_ReturnsCorrectSpan()
    {
        using var matrix = new Matrix(2, 3, Allocator.Temp);
        matrix[1, 0] = 1.0;
        matrix[1, 1] = 2.0;
        matrix[1, 2] = 3.0;

        var row = matrix[1];

        Assert.AreEqual(3, row.Length);
        Assert.AreEqual(1.0, row[0]);
        Assert.AreEqual(2.0, row[1]);
        Assert.AreEqual(3.0, row[2]);
    }

    [TestMethod]
    public void RowIndexer_ModifySpan_ModifiesOriginalMatrix()
    {
        using var matrix = new Matrix(2, 3, Allocator.Temp);

        var row = matrix[0];
        row[1] = 42.0;

        Assert.AreEqual(42.0, matrix[0, 1]);
    }

    [TestMethod]
    public void RowIndexer_NegativeIndex_ThrowsIndexOutOfRangeException()
    {
        using var matrix = new Matrix(2, 3, Allocator.Temp);

        Assert.ThrowsException<IndexOutOfRangeException>(() =>
        {
            var row = matrix[-1];
        });
    }

    [TestMethod]
    public void RowIndexer_IndexOutOfBounds_ThrowsIndexOutOfRangeException()
    {
        using var matrix = new Matrix(2, 3, Allocator.Temp);

        Assert.ThrowsException<IndexOutOfRangeException>(() =>
        {
            var row = matrix[2];
        });
    }

    [TestMethod]
    public void SwapRows_ValidIndices_SwapsRowsCorrectly()
    {
        using var matrix = new Matrix(3, 3, Allocator.Temp);

        // Set up first row: [1, 2, 3]
        matrix[0, 0] = 1.0;
        matrix[0, 1] = 2.0;
        matrix[0, 2] = 3.0;

        // Set up second row: [4, 5, 6]
        matrix[1, 0] = 4.0;
        matrix[1, 1] = 5.0;
        matrix[1, 2] = 6.0;

        matrix.SwapRows(0, 1);

        // First row should now be [4, 5, 6]
        Assert.AreEqual(4.0, matrix[0, 0]);
        Assert.AreEqual(5.0, matrix[0, 1]);
        Assert.AreEqual(6.0, matrix[0, 2]);

        // Second row should now be [1, 2, 3]
        Assert.AreEqual(1.0, matrix[1, 0]);
        Assert.AreEqual(2.0, matrix[1, 1]);
        Assert.AreEqual(3.0, matrix[1, 2]);
    }

    [TestMethod]
    public void SwapRows_SameRow_NoChange()
    {
        using var matrix = new Matrix(2, 2, Allocator.Temp);
        matrix[0, 0] = 1.0;
        matrix[0, 1] = 2.0;

        matrix.SwapRows(0, 0);

        Assert.AreEqual(1.0, matrix[0, 0]);
        Assert.AreEqual(2.0, matrix[0, 1]);
    }

    [TestMethod]
    public void ScaleRow_ValidScale_ScalesAllElementsInRow()
    {
        using var matrix = new Matrix(2, 3, Allocator.Temp);
        matrix[0, 0] = 2.0;
        matrix[0, 1] = 4.0;
        matrix[0, 2] = 6.0;

        matrix.ScaleRow(0, 0.5);

        Assert.AreEqual(1.0, matrix[0, 0]);
        Assert.AreEqual(2.0, matrix[0, 1]);
        Assert.AreEqual(3.0, matrix[0, 2]);
    }

    [TestMethod]
    public void ScaleRow_ScaleOne_NoChange()
    {
        using var matrix = new Matrix(2, 2, Allocator.Temp);
        matrix[0, 0] = 3.0;
        matrix[0, 1] = 7.0;

        matrix.ScaleRow(0, 1.0);

        Assert.AreEqual(3.0, matrix[0, 0]);
        Assert.AreEqual(7.0, matrix[0, 1]);
    }

    [TestMethod]
    public void ScaleRow_ScaleZero_SetsRowToZero()
    {
        using var matrix = new Matrix(2, 2, Allocator.Temp);
        matrix[0, 0] = 5.0;
        matrix[0, 1] = 10.0;

        matrix.ScaleRow(0, 0.0);

        Assert.AreEqual(0.0, matrix[0, 0]);
        Assert.AreEqual(0.0, matrix[0, 1]);
    }

    [TestMethod]
    public void Reduce_ValidRows_PerformsRowReduction()
    {
        using var matrix = new Matrix(2, 3, Allocator.Temp);

        // Destination row: [2, 4, 6]
        matrix[0, 0] = 2.0;
        matrix[0, 1] = 4.0;
        matrix[0, 2] = 6.0;

        // Source row: [1, 2, 3]
        matrix[1, 0] = 1.0;
        matrix[1, 1] = 2.0;
        matrix[1, 2] = 3.0;

        matrix.Reduce(0, 1, 2.0); // dst = dst + src * 2

        // Destination row should now be [4, 8, 12]
        Assert.AreEqual(4.0, matrix[0, 0]);
        Assert.AreEqual(8.0, matrix[0, 1]);
        Assert.AreEqual(12.0, matrix[0, 2]);

        // Source row should remain unchanged
        Assert.AreEqual(1.0, matrix[1, 0]);
        Assert.AreEqual(2.0, matrix[1, 1]);
        Assert.AreEqual(3.0, matrix[1, 2]);
    }

    [TestMethod]
    public void Reduce_ScaleZero_NoChange()
    {
        using var matrix = new Matrix(2, 2, Allocator.Temp);
        matrix[0, 0] = 3.0;
        matrix[0, 1] = 7.0;
        matrix[1, 0] = 1.0;
        matrix[1, 1] = 2.0;

        matrix.Reduce(0, 1, 0.0);

        Assert.AreEqual(3.0, matrix[0, 0]);
        Assert.AreEqual(7.0, matrix[0, 1]);
    }

    [TestMethod]
    public void Reduce_SameRow_ThrowsArgumentException()
    {
        using var matrix = new Matrix(2, 2, Allocator.Temp);

        Assert.ThrowsException<ArgumentException>(() => matrix.Reduce(0, 0, 1.0));
    }

    [TestMethod]
    public void InvScaleRow_ValidScale_DividesAllElementsInRow()
    {
        using var matrix = new Matrix(2, 3, Allocator.Temp);
        matrix[0, 0] = 4.0;
        matrix[0, 1] = 8.0;
        matrix[0, 2] = 12.0;

        matrix.InvScaleRow(0, 2.0);

        Assert.AreEqual(2.0, matrix[0, 0]);
        Assert.AreEqual(4.0, matrix[0, 1]);
        Assert.AreEqual(6.0, matrix[0, 2]);
    }

    [TestMethod]
    public void InvScaleRow_ScaleOne_NoChange()
    {
        using var matrix = new Matrix(2, 2, Allocator.Temp);
        matrix[0, 0] = 3.0;
        matrix[0, 1] = 7.0;

        matrix.InvScaleRow(0, 1.0);

        Assert.AreEqual(3.0, matrix[0, 0]);
        Assert.AreEqual(7.0, matrix[0, 1]);
    }

    [TestMethod]
    public void ScaleReduce_ValidParameters_PerformsScaleReduction()
    {
        using var matrix = new Matrix(3, 3, Allocator.Temp);

        // Set up matrix for pivot-based reduction
        matrix[0, 0] = 4.0; // dst row: [4, 8, 12]
        matrix[0, 1] = 8.0;
        matrix[0, 2] = 12.0;

        matrix[1, 0] = 1.0; // src row: [1, 2, 3]
        matrix[1, 1] = 2.0;
        matrix[1, 2] = 3.0;

        // Pivot is at column 0, so scale = matrix[0, 0] = 4.0
        matrix.ScaleReduce(0, 1, 0);

        // dst = dst - src * scale
        // Expected: [4, 8, 12] - [1, 2, 3] * 4 = [0, 0, 0]
        Assert.AreEqual(0.0, matrix[0, 0]);
        Assert.AreEqual(0.0, matrix[0, 1]);
        Assert.AreEqual(0.0, matrix[0, 2]);

        // Source row should remain unchanged
        Assert.AreEqual(1.0, matrix[1, 0]);
        Assert.AreEqual(2.0, matrix[1, 1]);
        Assert.AreEqual(3.0, matrix[1, 2]);
    }

    [TestMethod]
    public void ScaleReduce_ZeroPivotValue_NoChange()
    {
        using var matrix = new Matrix(2, 2, Allocator.Temp);
        matrix[0, 0] = 0.0;
        matrix[0, 1] = 5.0;
        matrix[1, 0] = 3.0;
        matrix[1, 1] = 7.0;

        matrix.ScaleReduce(0, 1, 0); // Pivot at [0,0] = 0

        // No change expected because pivot value is zero
        Assert.AreEqual(0.0, matrix[0, 0]);
        Assert.AreEqual(5.0, matrix[0, 1]);
    }

    [TestMethod]
    public void ScaleReduce_SameRow_ThrowsArgumentException()
    {
        using var matrix = new Matrix(2, 2, Allocator.Temp);

        Assert.ThrowsException<ArgumentException>(() => matrix.ScaleReduce(0, 0, 0));
    }

    [TestMethod]
    public void ScaleReduce_InvalidPivot_ThrowsArgumentOutOfRangeException()
    {
        using var matrix = new Matrix(2, 3, Allocator.Temp);

        Assert.ThrowsException<ArgumentOutOfRangeException>(() => matrix.ScaleReduce(0, 1, 3));
        Assert.ThrowsException<ArgumentOutOfRangeException>(() => matrix.ScaleReduce(0, 1, -1));
    }

    [TestMethod]
    public void ScaleReduce_NumericalAccuracy_TruncatesSmallValues()
    {
        using var matrix = new Matrix(2, 2, Allocator.Temp);

        // Set up values that will result in very small differences
        matrix[0, 0] = 1.0;
        matrix[0, 1] = 1e-12; // Very small value that should be truncated

        matrix[1, 0] = 1.0;
        matrix[1, 1] = 1e-12;

        matrix.ScaleReduce(0, 1, 0); // This should result in near-zero values

        // Values should be truncated to exactly zero due to numerical accuracy handling
        Assert.AreEqual(0.0, matrix[0, 0], 1e-10);
        Assert.AreEqual(0.0, matrix[0, 1], 1e-10);
    }

    [TestMethod]
    public void ToString_SmallMatrix_FormatsCorrectly()
    {
        using var matrix = new Matrix(2, 2, Allocator.Temp);
        matrix[0, 0] = 1.23;
        matrix[0, 1] = 4.56;
        matrix[1, 0] = 7.89;
        matrix[1, 1] = 10.1;

        string result = matrix.ToString();

        // Verify the string is not empty and contains our values
        Assert.IsFalse(string.IsNullOrEmpty(result));
        Assert.IsTrue(result.Contains("1.23"));
        Assert.IsTrue(result.Contains("4.56"));
        Assert.IsTrue(result.Contains("7.89"));
        Assert.IsTrue(result.Contains("10.1"));
    }

    [TestMethod]
    public void ToString_EmptyMatrix_ReturnsEmptyString()
    {
        using var matrix = new Matrix(0, 0, Allocator.Temp);

        string result = matrix.ToString();

        Assert.AreEqual("", result);
    }

    [TestMethod]
    public void Span_Property_ReturnsCorrectSpan()
    {
        using var matrix = new Matrix(2, 2, Allocator.Temp);
        matrix[0, 0] = 1.0;
        matrix[1, 1] = 2.0;

        var span = matrix.Span;

        Assert.AreEqual(4, span.Length);
        Assert.AreEqual(1.0, span[0]); // [0,0] maps to index 0
        Assert.AreEqual(2.0, span[3]); // [1,1] maps to index 3 (1*2 + 1)
    }

    [TestMethod]
    public void MatrixIndexing_RowMajorOrder_MapsCorrectly()
    {
        using var matrix = new Matrix(2, 3, Allocator.Temp);

        // Test that indices map to row-major order in the underlying array
        matrix[0, 1] = 42.0; // Should be at index 1
        matrix[1, 2] = 99.0; // Should be at index 5 (1*3 + 2)

        Assert.AreEqual(42.0, matrix.Span[1]);
        Assert.AreEqual(99.0, matrix.Span[5]);
    }

    [TestMethod]
    public void Dispose_ValidMatrix_DisposesWithoutException()
    {
        var matrix = new Matrix(2, 2, Allocator.Temp);
        matrix[0, 0] = 1.0;

        // Should not throw
        matrix.Dispose();
    }
}
