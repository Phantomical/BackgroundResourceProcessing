using System.Collections.ObjectModel;
using BackgroundResourceProcessing.Collections;

namespace BackgroundResourceProcessing.Test.Collections
{
    [TestClass]
    public sealed class BitSetTests
    {
        [TestMethod]
        public void TestBasic()
        {
            BitSet set = new(7) { [1] = true, [2] = true };

            Assert.IsTrue(set[1]);
            Assert.IsTrue(set[2]);

            Assert.IsFalse(set[0]);
            Assert.IsFalse(set[3]);
        }

        [TestMethod]
        public void TestIterator()
        {
            BitSet set = new(7) { [1] = true, [2] = true };
            int[] values = [.. set];

            AssertUtils.SequenceEqual([1, 2], values);
        }

        [TestMethod]
        public void TestClearUpTo()
        {
            BitSet set = new(64)
            {
                [1] = true,
                [2] = true,
                [45] = true,
                [56] = true,
            };

            BitSet copy = new(64);
            copy.CopyFrom(set);
            copy.ClearUpTo(7);
            AssertUtils.SequenceEqual([45, 56], copy);

            copy.CopyFrom(set);
            copy.ClearUpTo(54);
            AssertUtils.SequenceEqual([56], copy);
        }

        [TestMethod]
        public void TestClearOutsideRange()
        {
            BitSet set = new(256)
            {
                [1] = true,
                [2] = true,
                [45] = true,
                [56] = true,
                [89] = true,
                [129] = true,
                [130] = true,
                [245] = true,
            };

            BitSet copy;
            copy = set.Clone();
            copy.ClearOutsideRange(64, 128);
            AssertUtils.SequenceEqual([89], copy);

            copy = set.Clone();
            copy.ClearOutsideRange(13, 97);
            AssertUtils.SequenceEqual([45, 56, 89], copy);

            copy = set.Clone();
            copy.ClearOutsideRange(44, 48);
            AssertUtils.SequenceEqual([45], copy);

            copy = set.Clone();
            copy.ClearOutsideRange(128, 130);
            AssertUtils.SequenceEqual([129], copy);
        }

    }
}
