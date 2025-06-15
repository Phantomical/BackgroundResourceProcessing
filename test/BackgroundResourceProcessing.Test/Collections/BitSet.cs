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

            CollectionAssert.AreEqual(new int[] { 1, 2 }, values);
        }
    }
}
