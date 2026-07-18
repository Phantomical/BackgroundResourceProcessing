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

        // This vessel has a single nearly-full inventory whose changepoint
        // duration is ~5e-9 seconds. At t == 0 that duration is representable,
        // so the changepoint sits just after the current time.
        [TestInfo("ChangepointTests_TestSubUlpDurationAtZero")]
        public void TestSubUlpDurationAtZero()
        {
            var processor = TestUtil.LoadVessel("changepoint/sub-ulp-duration.cfg");
            var changepoint = processor.ComputeNextChangepoint(0.0);

            Assert.IsTrue(
                changepoint > 0.0,
                $"changepoint {changepoint} should be strictly after the current time"
            );
        }

        // Regression test for issue #24. The same sub-ULP changepoint duration
        // as above, but computed at a large universe time. Adding the ~5e-9s
        // duration to a universe time of ~1.4e8 is below half a ULP, so a naive
        // `currentTime + duration` is absorbed back to `currentTime`. That makes
        // the next changepoint equal to the current time, which trips the
        // "Background simulation failed to progress" guard and disables the
        // vessel. The changepoint must still make forward progress.
        [TestInfo("ChangepointTests_TestSubUlpDurationAtLargeUniverseTime")]
        public void TestSubUlpDurationAtLargeUniverseTime()
        {
            var processor = TestUtil.LoadVessel("changepoint/sub-ulp-duration.cfg");
            double currentTime = 142832464.44206977;
            var changepoint = processor.ComputeNextChangepoint(currentTime);

            Assert.IsTrue(
                changepoint > currentTime,
                $"changepoint {changepoint} should be strictly after currentTime {currentTime}"
            );
        }

        // A converter's REQUIRED_RESOURCE constraint is evaluated against the
        // inventories its flow mode can actually reach (CONSTRAINT_INVENTORIES),
        // so the time at which that constraint flips has to be predicted from
        // the same set of inventories.
        //
        // This vessel has a local 4000-unit tank filling at 100/s that the
        // converter's NO_FLOW constraint watches, plus an unreachable 50000-unit
        // central tank. The constraint boundary (5000 units in the local tank)
        // is crossed at t = 10. Summing both tanks instead puts the total at
        // 54000, far past the 5000 requirement, so the crossing looks like it
        // already happened and gets dropped — leaving the local tank filling up
        // at t = 960 as the only changepoint.
        [TestInfo("ChangepointTests_TestRequiredResourceUsesConstraintInventories")]
        public void TestRequiredResourceUsesConstraintInventories()
        {
            var processor = TestUtil.LoadVessel("changepoint/constraint-flow-mode.cfg");
            var changepoint = processor.ComputeNextChangepoint(0.0);

            Assert.AreEqual(10.0, changepoint, 1e-6);
        }
    }
}
