using KSP.Testing;

namespace BackgroundResourceProcessing.Test.Core
{
    public sealed class ChangepointTests : BRPTestBase
    {
        [TestInfo("ChangepointTests_TestSingleInventoryPositiveRate")]
        public void TestSingleInventoryPositiveRate()
        {
            var processor = TestUtil.LoadVessel("changepoint/one-inventory-positive.cfg");
            var changepoint = processor.ComputeNextChangepoint(0.0);

            Assert.AreEqual(50.0, changepoint, 1e-6);
        }

        [TestInfo("ChangepointTests_TestSingleInventoryNegativeRate")]
        public void TestSingleInventoryNegativeRate()
        {
            var processor = TestUtil.LoadVessel("changepoint/one-inventory-negative.cfg");
            var changepoint = processor.ComputeNextChangepoint(0.0);

            Assert.AreEqual(75.0, changepoint, 1e-6);
        }

        [TestInfo("ChangepointTests_TestMultipleInventories")]
        public void TestMultipleInventories()
        {
            var processor = TestUtil.LoadVessel("changepoint/multiple.cfg");
            var changepoint = processor.ComputeNextChangepoint(0.0);

            Assert.AreEqual(20.0, changepoint, 1e-6);
        }
    }
}
