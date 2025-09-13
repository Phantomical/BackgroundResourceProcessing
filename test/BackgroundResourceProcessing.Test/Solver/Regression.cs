using BackgroundResourceProcessing.Collections.Burst;

namespace BackgroundResourceProcessing.Test.Solver
{
    [TestClass]
    public sealed class RegressionTests
    {
        [TestCleanup]
        public void Cleanup()
        {
            TestAllocator.Cleanup();
        }

        [TestMethod]
        public void TestCrashSplitOutput()
        {
            var processor = TestUtil.LoadVessel("regression/crash-split-output.cfg");
            processor.ComputeRates();

            Assert.AreNotEqual(0.0, processor.converters[12].Rate);
        }

        [TestMethod]
        public void TestCrashBadVariableSelection()
        {
            var processor = TestUtil.LoadVessel("regression/crash-bad-var-selection.cfg");
            processor.ComputeRates();
        }

        [TestMethod]
        public void TestCrashSelectedSlack()
        {
            var processor = TestUtil.LoadVessel("regression/crash-selected-slack.cfg");
            processor.ComputeRates();
        }

        [TestMethod]
        public void TestFullDumpExcess()
        {
            var processor = TestUtil.LoadVessel("regression/crash-full-dump-excess.cfg");
            processor.ComputeRates();
        }

        [TestMethod]
        public void TestDisjunctionInstability()
        {
            var processor = TestUtil.LoadVessel("regression/disjunction-instability.cfg");
            processor.ComputeRates();

            // The first inventory here has ElectricCharge and its rate of
            // change should be exactly 0.
            Assert.AreEqual(0.0, processor.converters[0].Rate, 0.0);
        }

        [TestMethod]
        public void TestUnderflowInstability()
        {
            var processor = TestUtil.LoadVessel("regression/underflow-instability.cfg");
            processor.ComputeRates();

            // All inventories in this test case should have a rate of 0.
            for (int i = 0; i < processor.inventories.Count; ++i)
                Assert.AreEqual(0.0, processor.inventories[i].Rate);
        }

        [TestMethod]
        public void TestZeroRates()
        {
            var processor = TestUtil.LoadVessel("regression/zero-rates.cfg");
            processor.ComputeRates();

            Assert.AreNotEqual(0.0, processor.inventories[2].Rate);
        }

        [TestMethod]
        public void TestFertilizerOutOfThinAir()
        {
            var processor = TestUtil.LoadVessel("regression/fertilizer-out-of-thin-air.cfg");
            processor.ComputeRates();

            for (int i = 0; i < processor.inventories.Count; ++i)
            {
                if (processor.inventories[i].ResourceName != "Fertilizer")
                    continue;

                Assert.AreEqual(0.0, processor.inventories[i].Rate);
            }
        }

        [TestMethod]
        public void TestBoiloffIgnored()
        {
            var processor = TestUtil.LoadVessel("regression/boiloff-ignored.cfg");
            processor.ComputeRates();

            for (int i = 0; i < processor.converters.Count; ++i)
            {
                var converter = processor.converters[i];

                if (!converter.Inputs.ContainsKey("ElectricCharge".GetHashCode()))
                    continue;
                if (!converter.Inputs.ContainsKey("BRPCryoTankBoiloff".GetHashCode()))
                    continue;

                Assert.AreNotEqual(0.0, converter.Rate);
            }
        }

        [TestMethod]
        public void TestBoiloffEcIgnored()
        {
            var processor = TestUtil.LoadVessel("regression/boiloff-ec-ignored.cfg");
            processor.ComputeRates();

            for (int i = 0; i < processor.inventories.Count; ++i)
            {
                var inventory = processor.inventories[i];
                if (inventory.ResourceName != "ElectricCharge")
                    continue;

                Assert.AreNotEqual(0.0, inventory.Rate);
            }
        }

        [TestMethod]
        public void TestCrashBadBchoiceAccess()
        {
            var processor = TestUtil.LoadVessel("regression/crash-bad-bchoice-access.cfg");
            processor.ComputeRates();
        }

        [TestMethod]
        public void TestCrashNoProgress()
        {
            var processor = TestUtil.LoadVessel("regression/crash-bad-bchoice-access.cfg");
            processor.ComputeRates();
            var changepoint = processor.ComputeNextChangepoint(0.0);

            Assert.AreNotEqual(0.0, changepoint);
        }

        [TestMethod]
        public void TestCrashCreateSimplexTableau()
        {
            var processor = TestUtil.LoadVessel("regression/crash-create-simplex-tableau.cfg");
            processor.ComputeRates();
        }

        [TestMethod]
        public void TestZeroRankFunction()
        {
            var processor = TestUtil.LoadVessel("regression/crash-create-simplex-tableau.cfg");
            processor.ComputeRates();

            Assert.IsTrue(
                processor.converters.Any(converter => converter.Rate != 0.0),
                "at least some converters should be running"
            );
        }

        [TestMethod]
        public void TestBoiloffUnexpected()
        {
            var processor = TestUtil.LoadVessel("regression/boiloff-unexpected.cfg");
            processor.ComputeRates();

            var h2 = processor.GetResourceStates()["LqdHydrogen"];

            Assert.AreEqual(0.0, h2.rate, "LH2 rate should be 0");
        }
    }
}
