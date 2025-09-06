using System;
using BackgroundResourceProcessing.Collections;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace BackgroundResourceProcessing.Test.Collections;

[TestClass]
public sealed class MatrixTest
{
    [TestMethod]
    public void Constructor_ValidDimensions_CreatesMatrix()
    {
        const int width = 3;
        const int height = 2;

        var matrix = new Matrix(width, height);

        Assert.AreEqual(width, matrix.Width);
        Assert.AreEqual(height, matrix.Height);
        Assert.AreEqual(width * height, matrix.Values.Length);
    }

    [TestMethod]
    public void Constructor_ZeroDimensions_CreatesEmptyMatrix()
    {
        var matrix = new Matrix(0, 0);

        Assert.AreEqual(0, matrix.Width);
        Assert.AreEqual(0, matrix.Height);
        Assert.AreEqual(0, matrix.Values.Length);
    }

    [TestMethod]
    public void Constructor_NegativeWidth_ThrowsArgumentException()
    {
        Assert.ThrowsException<ArgumentException>(() => new Matrix(-1, 2));
    }

    [TestMethod]
    public void Constructor_NegativeHeight_ThrowsArgumentException()
    {
        Assert.ThrowsException<ArgumentException>(() => new Matrix(2, -1));
    }

    [TestMethod]
    public void Indexer_ValidIndices_SetsAndGetsValues()
    {
        var matrix = new Matrix(3, 2);

        matrix[0, 0] = 1.0;
        matrix[2, 1] = 5.0;

        Assert.AreEqual(1.0, matrix[0, 0]);
        Assert.AreEqual(5.0, matrix[2, 1]);
        Assert.AreEqual(0.0, matrix[1, 0]); // Default value
    }

    [TestMethod]
    public void Indexer_UnsignedValidIndices_SetsAndGetsValues()
    {
        var matrix = new Matrix(3, 2);

        matrix[0u, 0u] = 1.5;
        matrix[2u, 1u] = 3.5;

        Assert.AreEqual(1.5, matrix[0u, 0u]);
        Assert.AreEqual(3.5, matrix[2u, 1u]);
    }

    [TestMethod]
    public void Indexer_NegativeIndices_ThrowsIndexOutOfRangeException()
    {
        var matrix = new Matrix(3, 2);

        Assert.ThrowsException<IndexOutOfRangeException>(() => _ = matrix[-1, 0]);
        Assert.ThrowsException<IndexOutOfRangeException>(() => _ = matrix[0, -1]);
        Assert.ThrowsException<IndexOutOfRangeException>(() => matrix[-1, 0] = 1.0);
        Assert.ThrowsException<IndexOutOfRangeException>(() => matrix[0, -1] = 1.0);
    }

    [TestMethod]
    public void Indexer_IndicesOutOfBounds_ThrowsIndexOutOfRangeException()
    {
        var matrix = new Matrix(3, 2);

        Assert.ThrowsException<IndexOutOfRangeException>(() => _ = matrix[3, 0]);
        Assert.ThrowsException<IndexOutOfRangeException>(() => _ = matrix[0, 2]);
        Assert.ThrowsException<IndexOutOfRangeException>(() => matrix[3, 0] = 1.0);
        Assert.ThrowsException<IndexOutOfRangeException>(() => matrix[0, 2] = 1.0);
    }

    [TestMethod]
    public void UnsignedIndexer_IndicesOutOfBounds_ThrowsIndexOutOfRangeException()
    {
        var matrix = new Matrix(3, 2);

        Assert.ThrowsException<IndexOutOfRangeException>(() => _ = matrix[3u, 0u]);
        Assert.ThrowsException<IndexOutOfRangeException>(() => _ = matrix[0u, 2u]);
        Assert.ThrowsException<IndexOutOfRangeException>(() => matrix[3u, 0u] = 1.0);
        Assert.ThrowsException<IndexOutOfRangeException>(() => matrix[0u, 2u] = 1.0);
    }

    [TestMethod]
    public void GetRow_ValidRowIndex_ReturnsCorrectSpan()
    {
        var matrix = new Matrix(3, 2);
        matrix[0, 1] = 1.0;
        matrix[1, 1] = 2.0;
        matrix[2, 1] = 3.0;

        var row = matrix.GetRow(1);

        Assert.AreEqual(3, row.Length);
        Assert.AreEqual(1.0, row[0]);
        Assert.AreEqual(2.0, row[1]);
        Assert.AreEqual(3.0, row[2]);
    }

    [TestMethod]
    public void GetRow_ModifySpan_ModifiesOriginalMatrix()
    {
        var matrix = new Matrix(3, 2);

        var row = matrix.GetRow(0);
        row[1] = 42.0;

        Assert.AreEqual(42.0, matrix[1, 0]);
    }

    [TestMethod]
    public void GetRow_NegativeRowIndex_ThrowsIndexOutOfRangeException()
    {
        var matrix = new Matrix(3, 2);

        Assert.ThrowsException<IndexOutOfRangeException>(() => matrix.GetRow(-1));
    }

    [TestMethod]
    public void GetRow_RowIndexOutOfBounds_ThrowsIndexOutOfRangeException()
    {
        var matrix = new Matrix(3, 2);

        Assert.ThrowsException<IndexOutOfRangeException>(() => matrix.GetRow(2));
    }

    [TestMethod]
    public void SwapRows_ValidIndices_SwapsRowsCorrectly()
    {
        var matrix = new Matrix(3, 3);

        // Set up first row: [1, 2, 3]
        matrix[0, 0] = 1.0;
        matrix[1, 0] = 2.0;
        matrix[2, 0] = 3.0;

        // Set up second row: [4, 5, 6]
        matrix[0, 1] = 4.0;
        matrix[1, 1] = 5.0;
        matrix[2, 1] = 6.0;

        matrix.SwapRows(0, 1);

        // First row should now be [4, 5, 6]
        Assert.AreEqual(4.0, matrix[0, 0]);
        Assert.AreEqual(5.0, matrix[1, 0]);
        Assert.AreEqual(6.0, matrix[2, 0]);

        // Second row should now be [1, 2, 3]
        Assert.AreEqual(1.0, matrix[0, 1]);
        Assert.AreEqual(2.0, matrix[1, 1]);
        Assert.AreEqual(3.0, matrix[2, 1]);
    }

    [TestMethod]
    public void SwapRows_SameRow_NoChange()
    {
        var matrix = new Matrix(2, 2);
        matrix[0, 0] = 1.0;
        matrix[1, 0] = 2.0;

        matrix.SwapRows(0, 0);

        Assert.AreEqual(1.0, matrix[0, 0]);
        Assert.AreEqual(2.0, matrix[1, 0]);
    }

    [TestMethod]
    public void ScaleRow_ValidScale_ScalesAllElementsInRow()
    {
        var matrix = new Matrix(3, 2);
        matrix[0, 0] = 2.0;
        matrix[1, 0] = 4.0;
        matrix[2, 0] = 6.0;

        matrix.ScaleRow(0, 0.5);

        Assert.AreEqual(1.0, matrix[0, 0]);
        Assert.AreEqual(2.0, matrix[1, 0]);
        Assert.AreEqual(3.0, matrix[2, 0]);
    }

    [TestMethod]
    public void ScaleRow_ScaleOne_NoChange()
    {
        var matrix = new Matrix(2, 2);
        matrix[0, 0] = 3.0;
        matrix[1, 0] = 7.0;

        matrix.ScaleRow(0, 1.0);

        Assert.AreEqual(3.0, matrix[0, 0]);
        Assert.AreEqual(7.0, matrix[1, 0]);
    }

    [TestMethod]
    public void ScaleRow_ScaleZero_SetsRowToZero()
    {
        var matrix = new Matrix(2, 2);
        matrix[0, 0] = 5.0;
        matrix[1, 0] = 10.0;

        matrix.ScaleRow(0, 0.0);

        Assert.AreEqual(0.0, matrix[0, 0]);
        Assert.AreEqual(0.0, matrix[1, 0]);
    }

    [TestMethod]
    public void Reduce_ValidRows_PerformsRowReduction()
    {
        var matrix = new Matrix(3, 2);

        // Destination row: [2, 4, 6]
        matrix[0, 0] = 2.0;
        matrix[1, 0] = 4.0;
        matrix[2, 0] = 6.0;

        // Source row: [1, 2, 3]
        matrix[0, 1] = 1.0;
        matrix[1, 1] = 2.0;
        matrix[2, 1] = 3.0;

        matrix.Reduce(0, 1, 2.0); // dst = dst + src * 2

        // Destination row should now be [4, 8, 12]
        Assert.AreEqual(4.0, matrix[0, 0]);
        Assert.AreEqual(8.0, matrix[1, 0]);
        Assert.AreEqual(12.0, matrix[2, 0]);

        // Source row should remain unchanged
        Assert.AreEqual(1.0, matrix[0, 1]);
        Assert.AreEqual(2.0, matrix[1, 1]);
        Assert.AreEqual(3.0, matrix[2, 1]);
    }

    [TestMethod]
    public void Reduce_ScaleZero_NoChange()
    {
        var matrix = new Matrix(2, 2);
        matrix[0, 0] = 3.0;
        matrix[1, 0] = 7.0;
        matrix[0, 1] = 1.0;
        matrix[1, 1] = 2.0;

        matrix.Reduce(0, 1, 0.0);

        Assert.AreEqual(3.0, matrix[0, 0]);
        Assert.AreEqual(7.0, matrix[1, 0]);
    }

    [TestMethod]
    public void Reduce_SameRow_ThrowsArgumentException()
    {
        var matrix = new Matrix(2, 2);

        Assert.ThrowsException<ArgumentException>(() => matrix.Reduce(0, 0, 1.0));
    }

    [TestMethod]
    public void InvScaleRow_ValidScale_DividesAllElementsInRow()
    {
        var matrix = new Matrix(3, 2);
        matrix[0, 0] = 4.0;
        matrix[1, 0] = 8.0;
        matrix[2, 0] = 12.0;

        matrix.InvScaleRow(0, 2.0);

        Assert.AreEqual(2.0, matrix[0, 0]);
        Assert.AreEqual(4.0, matrix[1, 0]);
        Assert.AreEqual(6.0, matrix[2, 0]);
    }

    [TestMethod]
    public void InvScaleRow_ScaleOne_NoChange()
    {
        var matrix = new Matrix(2, 2);
        matrix[0, 0] = 3.0;
        matrix[1, 0] = 7.0;

        matrix.InvScaleRow(0, 1.0);

        Assert.AreEqual(3.0, matrix[0, 0]);
        Assert.AreEqual(7.0, matrix[1, 0]);
    }

    [TestMethod]
    public void ScaleReduce_ValidParameters_PerformsScaleReduction()
    {
        var matrix = new Matrix(3, 3);

        // Set up matrix for pivot-based reduction
        matrix[0, 0] = 4.0; // dst row: [4, 8, 12]
        matrix[1, 0] = 8.0;
        matrix[2, 0] = 12.0;

        matrix[0, 1] = 1.0; // src row: [1, 2, 3]
        matrix[1, 1] = 2.0;
        matrix[2, 1] = 3.0;

        // Pivot is at column 0, so scale = matrix[0, 0] = 4.0
        matrix.ScaleReduce(0, 1, 0);

        // dst = dst - src * scale
        // Expected: [4, 8, 12] - [1, 2, 3] * 4 = [0, 0, 0]
        Assert.AreEqual(0.0, matrix[0, 0]);
        Assert.AreEqual(0.0, matrix[1, 0]);
        Assert.AreEqual(0.0, matrix[2, 0]);

        // Source row should remain unchanged
        Assert.AreEqual(1.0, matrix[0, 1]);
        Assert.AreEqual(2.0, matrix[1, 1]);
        Assert.AreEqual(3.0, matrix[2, 1]);
    }

    [TestMethod]
    public void ScaleReduce_ZeroPivotValue_NoChange()
    {
        var matrix = new Matrix(2, 2);
        matrix[0, 0] = 0.0;
        matrix[1, 0] = 5.0;
        matrix[0, 1] = 3.0;
        matrix[1, 1] = 7.0;

        matrix.ScaleReduce(0, 1, 0); // Pivot at [0,0] = 0

        // No change expected because pivot value is zero
        Assert.AreEqual(0.0, matrix[0, 0]);
        Assert.AreEqual(5.0, matrix[1, 0]);
    }

    [TestMethod]
    public void ScaleReduce_SameRow_ThrowsArgumentException()
    {
        var matrix = new Matrix(2, 2);

        Assert.ThrowsException<ArgumentException>(() => matrix.ScaleReduce(0, 0, 0));
    }

    [TestMethod]
    public void ScaleReduce_InvalidPivot_ThrowsArgumentOutOfRangeException()
    {
        var matrix = new Matrix(3, 2);

        Assert.ThrowsException<ArgumentOutOfRangeException>(() => matrix.ScaleReduce(0, 1, 3));
        Assert.ThrowsException<ArgumentOutOfRangeException>(() => matrix.ScaleReduce(0, 1, -1));
    }

    [TestMethod]
    public void ScaleReduce_NumericalAccuracy_TruncatesSmallValues()
    {
        var matrix = new Matrix(2, 2);

        // Set up values that will result in very small differences
        matrix[0, 0] = 1.0;
        matrix[1, 0] = 1e-12; // Very small value that should be truncated

        matrix[0, 1] = 1.0;
        matrix[1, 1] = 1e-12;

        matrix.ScaleReduce(0, 1, 0); // This should result in near-zero values

        // Values should be truncated to exactly zero due to numerical accuracy handling
        Assert.AreEqual(0.0, matrix[0, 0], 1e-10);
        Assert.AreEqual(0.0, matrix[1, 0], 1e-10);
    }

    [TestMethod]
    public void ToString_SmallMatrix_FormatsCorrectly()
    {
        var matrix = new Matrix(2, 2);
        matrix[0, 0] = 1.23;
        matrix[1, 0] = 4.56;
        matrix[0, 1] = 7.89;
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
        var matrix = new Matrix(0, 0);

        string result = matrix.ToString();

        Assert.AreEqual("", result);
    }

    [TestMethod]
    public void Values_Property_ReturnsUnderlyingArray()
    {
        var matrix = new Matrix(2, 2);
        matrix[0, 0] = 1.0;
        matrix[1, 1] = 2.0;

        double[] values = matrix.Values;

        Assert.AreEqual(4, values.Length);
        Assert.AreEqual(1.0, values[0]); // [0,0] maps to index 0
        Assert.AreEqual(2.0, values[3]); // [1,1] maps to index 3 (1*2 + 1)
    }

    [TestMethod]
    public void MatrixIndexing_RowMajorOrder_MapsCorrectly()
    {
        var matrix = new Matrix(3, 2);

        // Test that indices map to row-major order in the underlying array
        matrix[1, 0] = 42.0; // Should be at index 1
        matrix[2, 1] = 99.0; // Should be at index 5 (1*3 + 2)

        Assert.AreEqual(42.0, matrix.Values[1]);
        Assert.AreEqual(99.0, matrix.Values[5]);
    }
}
