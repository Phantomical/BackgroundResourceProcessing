using BackgroundResourceProcessing.Collections;
using KSP.Testing;

namespace BackgroundResourceProcessing.Test.Collections
{
    public sealed class PriorityQueueTest : BRPTestBase
    {
        [TestInfo("PriorityQueue_RemoveFromMiddle")]
        public void RemoveFromMiddle()
        {
            PriorityQueue<int, int> queue = new();

            for (int i = 0; i < 32; ++i)
                queue.Enqueue(i, i);

            Assert.IsTrue(queue.Remove(6, out int removed, out int _));
            Assert.AreEqual(6, removed);
        }

        [TestInfo("PriorityQueue_InsertReversed")]
        public void InsertReversed()
        {
            int count = 8;
            PriorityQueue<int, int> queue = new();

            for (int i = 0; i < count; ++i)
                queue.Enqueue(count - i - 1, count - i - 1);

            for (int i = 0; i < count; ++i)
                Assert.AreEqual(i, queue.Dequeue());
        }

        [TestInfo("PriorityQueue_TryDequeueEmpty")]
        public void TryDequeueEmpty()
        {
            PriorityQueue<int, int> queue = new();

            Assert.IsFalse(queue.TryDequeue(out int _, out int _));
        }
    }
}
