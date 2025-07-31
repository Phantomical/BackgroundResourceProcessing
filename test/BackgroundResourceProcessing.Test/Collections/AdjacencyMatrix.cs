using BackgroundResourceProcessing.Collections;
using BackgroundResourceProcessing.Solver;
using BackgroundResourceProcessing.Test;

namespace BackgroundResourceProcessing.Text.Collections
{
    [TestClass]
    public sealed class BitEnumeratorTests
    {
        [TestMethod]
        public void TestBitEnumerator1()
        {
            var it = new BitEnumerator(0, 0xF);
            int[] bits = [.. it];

            Assert.IsTrue(
                Enumerable.SequenceEqual([0, 1, 2, 3], bits),
                TestUtil.SequenceToString(bits)
            );
        }

        [TestMethod]
        public void TestBitEnumerator2()
        {
            var it = new BitEnumerator(0, 0x111);
            int[] bits = [.. it];

            Assert.IsTrue(
                Enumerable.SequenceEqual([0, 4, 8], bits),
                TestUtil.SequenceToString(bits)
            );
        }
    }

    [TestClass]
    public sealed class BitSetXTests
    {
        [TestMethod]
        public void TestEnumerate()
        {
            ulong[] vals = [0xF00, 0x1];
            var slice = new BitSliceX(new(vals));
            int[] bits = [.. slice];

            Assert.IsTrue(
                Enumerable.SequenceEqual([8, 9, 10, 11, 64], bits),
                TestUtil.SequenceToString(bits)
            );
        }
    }

    [TestClass]
    public sealed class AdjacencyMatrixTests
    {
        [TestMethod]
        public void TestRowEnumerate()
        {
            var matrix = new AdjacencyMatrix(128, 128);
            foreach (var row in matrix.GetRows()) { }
        }

        [TestMethod]
        public void TestEdgeEnumerate()
        {
            AdjacencyMatrix matrix = new(128, 128);

            matrix[5, 5] = true;
            matrix[127, 127] = true;
            matrix[7, 89] = true;

            List<AdjacencyMatrixExt.Edge> edges = [];
            AdjacencyMatrixExt.Edge[] expected = [new(5, 5), new(7, 89), new(127, 127)];

            int index = 0;
            foreach (var edge in matrix.ConverterToInventoryEdges())
            {
                edges.Add(edge);
                if (index++ > 5)
                    break;
            }

            Assert.IsTrue(
                Enumerable.SequenceEqual(expected, edges),
                "[" + string.Join(", ", edges.Select(e => $"({e.Converter}, {e.Inventory})")) + "]"
            );
        }
    }
}
