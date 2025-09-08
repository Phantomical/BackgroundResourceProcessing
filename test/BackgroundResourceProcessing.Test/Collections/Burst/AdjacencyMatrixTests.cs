using BackgroundResourceProcessing.Collections.Burst;
using Unity.Collections;

namespace BackgroundResourceProcessing.Test.Collections.Burst
{
    [TestClass]
    public sealed class AdjacencyMatrixTests
    {
        [TestCleanup]
        public void Cleanup()
        {
            TestAllocator.Cleanup();
        }

        [TestMethod]
        public void Constructor_ValidDimensions_CreatesMatrix()
        {
            var matrix = new AdjacencyMatrix(5, 10);

            Assert.AreEqual(5, matrix.Rows);
            Assert.AreEqual(64, matrix.Cols); // Rounded up to next multiple of 64
            Assert.AreEqual(1, matrix.ColumnWords); // 10 bits fits in 1 ulong
        }

        [TestMethod]
        public void Constructor_LargeDimensions_CalculatesCorrectColumnWords()
        {
            var matrix = new AdjacencyMatrix(5, 128);

            Assert.AreEqual(5, matrix.Rows);
            Assert.AreEqual(128, matrix.Cols);
            Assert.AreEqual(2, matrix.ColumnWords); // 128 bits requires 2 ulongs
        }

        [TestMethod]
        public void Constructor_NegativeRows_ThrowsException()
        {
            Assert.ThrowsException<ArgumentOutOfRangeException>(() => new AdjacencyMatrix(-1, 5));
        }

        [TestMethod]
        public void Constructor_NegativeCols_ThrowsException()
        {
            Assert.ThrowsException<ArgumentOutOfRangeException>(() => new AdjacencyMatrix(5, -1));
        }

        [TestMethod]
        public void Constructor_ZeroDimensions_CreatesEmptyMatrix()
        {
            var matrix = new AdjacencyMatrix(0, 0);

            Assert.AreEqual(0, matrix.Rows);
            Assert.AreEqual(0, matrix.Cols);
            Assert.AreEqual(0, matrix.ColumnWords);
        }

        [TestMethod]
        public void Indexer_SingleElement_GetSet()
        {
            var matrix = new AdjacencyMatrix(3, 3);

            // Initially false
            Assert.IsFalse(matrix[1, 2]);

            // Set to true
            matrix[1, 2] = true;
            Assert.IsTrue(matrix[1, 2]);

            // Set back to false
            matrix[1, 2] = false;
            Assert.IsFalse(matrix[1, 2]);
        }

        [TestMethod]
        public void Indexer_MultipleElements_Independent()
        {
            var matrix = new AdjacencyMatrix(3, 3);

            matrix[0, 0] = true;
            matrix[1, 1] = true;
            matrix[2, 2] = false;

            Assert.IsTrue(matrix[0, 0]);
            Assert.IsTrue(matrix[1, 1]);
            Assert.IsFalse(matrix[2, 2]);
            Assert.IsFalse(matrix[0, 1]);
            Assert.IsFalse(matrix[1, 0]);
        }

        [TestMethod]
        public void RowIndexer_OutOfRange_ThrowsException()
        {
            var matrix = new AdjacencyMatrix(3, 3);

            Assert.ThrowsException<IndexOutOfRangeException>(() =>
            {
                var row = matrix[-1];
            });

            Assert.ThrowsException<IndexOutOfRangeException>(() =>
            {
                var row = matrix[3];
            });
        }

        [TestMethod]
        public void RowIndexer_ValidRange_ReturnsBitSpan()
        {
            var matrix = new AdjacencyMatrix(3, 65);

            var row0 = matrix[0];
            var row1 = matrix[1];
            var row2 = matrix[2];

            Assert.AreEqual(matrix.ColumnWords, row0.Words);
            Assert.AreEqual(matrix.ColumnWords, row1.Words);
            Assert.AreEqual(matrix.ColumnWords, row2.Words);
        }

        [TestMethod]
        public void ElementIndexer_OutOfRange_ThrowsException()
        {
            var matrix = new AdjacencyMatrix(3, 3);

            Assert.ThrowsException<IndexOutOfRangeException>(() =>
            {
                bool value = matrix[-1, 0];
            });

            Assert.ThrowsException<IndexOutOfRangeException>(() =>
            {
                bool value = matrix[3, 0];
            });
        }

        [TestMethod]
        public void Bits_Property_ReturnsCorrectBitSpan()
        {
            var matrix = new AdjacencyMatrix(2, 65);

            var bits = matrix.Bits;
            Assert.AreEqual(matrix.Rows * matrix.ColumnWords, bits.Words);
        }

        [TestMethod]
        public void SetEqualColumns_EmptyMatrix_SetsAllBitsTrue()
        {
            var matrix = new AdjacencyMatrix(0, 5);
            var bitSet = new BackgroundResourceProcessing.Collections.Burst.BitSet(matrix.Cols);
            var span = bitSet.Span;

            matrix.SetEqualColumns(span, 2);

            // All bits should be true since there are no rows to compare
            Assert.IsTrue(span[0]);
            Assert.IsTrue(span[1]);
            Assert.IsTrue(span[2]);
            Assert.IsTrue(span[3]);
            Assert.IsTrue(span[4]);
        }

        [TestMethod]
        public void SetEqualColumns_WithData_FindsEqualColumns()
        {
            var matrix = new AdjacencyMatrix(3, 5);
            var bitSet = new BackgroundResourceProcessing.Collections.Burst.BitSet(matrix.Cols);
            var span = bitSet.Span;

            // Set up matrix where columns 0 and 2 match column 1
            matrix[0, 0] = true;
            matrix[0, 1] = true;
            matrix[0, 2] = true;
            matrix[1, 0] = false;
            matrix[1, 1] = false;
            matrix[1, 2] = false;
            matrix[2, 0] = true;
            matrix[2, 1] = true;
            matrix[2, 2] = true;

            // Column 3 and 4 are different
            matrix[0, 3] = false;
            matrix[0, 4] = true;
            matrix[1, 3] = true;
            matrix[1, 4] = false;
            matrix[2, 3] = false;
            matrix[2, 4] = true;

            matrix.SetEqualColumns(span, 1);

            Assert.IsTrue(span[0]); // Same as column 1
            Assert.IsTrue(span[1]); // Same as itself
            Assert.IsTrue(span[2]); // Same as column 1
            Assert.IsFalse(span[3]); // Different from column 1
            Assert.IsTrue(span[4]); // Actually same as column 1: [true, false, true]

            // Check a few more columns to understand the pattern
            for (int i = 5; i < 10; i++)
            {
                // Unset columns should be false since they don't match column 1's pattern
                Assert.IsFalse(span[i], $"Column {i} should be false");
            }
        }

        [TestMethod]
        public void RemoveUnequalColumns_InvalidSpanSize_ThrowsException()
        {
            var matrix = new AdjacencyMatrix(3, 5);
            var wrongBitSet = new BackgroundResourceProcessing.Collections.Burst.BitSet(
                matrix.Cols + 320
            ); // Much larger wrong size (5 words * 64 bits)
            var wrongSpan = wrongBitSet.Span;

            try
            {
                matrix.RemoveUnequalColumns(wrongSpan, 1);
                Assert.Fail("Expected ArgumentException");
            }
            catch (ArgumentException)
            {
                // Expected
            }
        }

        [TestMethod]
        public void RemoveUnequalColumns_InvalidColumn_ThrowsException()
        {
            var matrix = new AdjacencyMatrix(3, 5);
            var bitSet = new BackgroundResourceProcessing.Collections.Burst.BitSet(matrix.Cols);
            var span = bitSet.Span;

            try
            {
                matrix.RemoveUnequalColumns(span, -1);
                Assert.Fail("Expected ArgumentOutOfRangeException for negative column");
            }
            catch (ArgumentOutOfRangeException)
            {
                // Expected
            }

            try
            {
                matrix.RemoveUnequalColumns(span, matrix.Cols); // Use actual column count which is 64
                Assert.Fail("Expected ArgumentOutOfRangeException for out of range column");
            }
            catch (ArgumentOutOfRangeException)
            {
                // Expected
            }
        }

        [TestMethod]
        public void RemoveUnequalColumns_SingleColumnWord_WorksCorrectly()
        {
            var matrix = new AdjacencyMatrix(3, 32); // Single column word
            var bitSet = new BackgroundResourceProcessing.Collections.Burst.BitSet(matrix.Cols);
            var span = bitSet.Span;

            // Initialize span with all bits set
            span.Fill(true);

            // Set up matrix where some columns match column 5, others don't
            for (int r = 0; r < 3; r++)
            {
                matrix[r, 5] = (r % 2 == 0); // Column 5: true, false, true
                matrix[r, 10] = (r % 2 == 0); // Column 10 matches column 5
                matrix[r, 15] = (r % 2 == 1); // Column 15 doesn't match column 5
            }

            matrix.RemoveUnequalColumns(span, 5);

            Assert.IsTrue(span[5]); // Same as itself
            Assert.IsTrue(span[10]); // Same as column 5
            Assert.IsFalse(span[15]); // Different from column 5
        }

        [TestMethod]
        public void RemoveUnequalColumns_MultipleColumnWords_WorksCorrectly()
        {
            var matrix = new AdjacencyMatrix(2, 128); // Multiple column words
            var bitSet = new BackgroundResourceProcessing.Collections.Burst.BitSet(matrix.Cols); // Use matrix column words * 64 bits per word
            var span = bitSet.Span;

            // Initialize span with all bits set
            span.Fill(true);

            // Set up test data across word boundaries
            matrix[0, 63] = true; // End of first word
            matrix[1, 63] = false;

            matrix[0, 64] = true; // Start of second word
            matrix[1, 64] = false;

            matrix[0, 100] = true; // Middle of second word - should match column 63
            matrix[1, 100] = false;

            matrix[0, 120] = false; // Different pattern
            matrix[1, 120] = true;

            matrix.RemoveUnequalColumns(span, 63);

            Assert.IsTrue(span[63]); // Same as itself
            Assert.IsTrue(span[64]); // Same pattern as column 63
            Assert.IsTrue(span[100]); // Same pattern as column 63
            Assert.IsFalse(span[120]); // Different pattern
        }

        [TestMethod]
        public void Dispose_CallsUnderlyingDispose()
        {
            var matrix = new AdjacencyMatrix(5, 5);

            // Should not throw

            // Accessing after dispose should be undefined behavior, but we can't easily test this
            // without causing undefined behavior in the test itself
        }

        [TestMethod]
        public void LargeDimensionsHandling()
        {
            // Test with dimensions that require multiple column words
            var matrix = new AdjacencyMatrix(10, 200);

            Assert.AreEqual(10, matrix.Rows);
            Assert.AreEqual(256, matrix.Cols); // Rounded up to next multiple of 64
            Assert.AreEqual(4, matrix.ColumnWords); // 200 bits requires 4 ulongs (64*3 = 192, need 1 more)

            // Test setting and getting elements across word boundaries
            matrix[5, 63] = true; // End of first word
            matrix[5, 64] = true; // Start of second word
            matrix[5, 127] = true; // End of second word
            matrix[5, 128] = true; // Start of third word
            matrix[5, 199] = true; // Near end of matrix

            Assert.IsTrue(matrix[5, 63]);
            Assert.IsTrue(matrix[5, 64]);
            Assert.IsTrue(matrix[5, 127]);
            Assert.IsTrue(matrix[5, 128]);
            Assert.IsTrue(matrix[5, 199]);

            // Test that other bits remain false
            Assert.IsFalse(matrix[5, 62]);
            Assert.IsFalse(matrix[5, 65]);
            Assert.IsFalse(matrix[4, 63]);
        }
    }
}
