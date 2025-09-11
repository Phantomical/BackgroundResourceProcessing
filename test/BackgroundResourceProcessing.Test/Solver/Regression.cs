using BackgroundResourceProcessing.Solver;

namespace BackgroundResourceProcessing.Test.Solver
{
    [TestClass]
    public sealed class RegressionTests
    {
        [TestMethod]
        public void TestCrashSplitOutput()
        {
            var processor = TestUtil.LoadVessel("regression/crash-split-output.cfg");
            var solver = new BackgroundResourceProcessing.Solver.Solver();

            var rates = solver.ComputeInventoryRates(processor);

            Assert.AreNotEqual(0.0, rates.converterRates[12]);
        }

        [TestMethod]
        public void TestCrashBadVariableSelection()
        {
            var processor = TestUtil.LoadVessel("regression/crash-bad-var-selection.cfg");
            var solver = new BackgroundResourceProcessing.Solver.Solver();

            solver.ComputeInventoryRates(processor);
        }

        [TestMethod]
        public void TestCrashSelectedSlack()
        {
            var processor = TestUtil.LoadVessel("regression/crash-selected-slack.cfg");
            var solver = new BackgroundResourceProcessing.Solver.Solver();

            solver.ComputeInventoryRates(processor);
        }

        [TestMethod]
        public void TestFullDumpExcess()
        {
            var processor = TestUtil.LoadVessel("regression/crash-full-dump-excess.cfg");
            var solver = new BackgroundResourceProcessing.Solver.Solver();

            solver.ComputeInventoryRates(processor);
        }

        [TestMethod]
        public void TestDisjunctionInstability()
        {
            var processor = TestUtil.LoadVessel("regression/disjunction-instability.cfg");
            var solver = new BackgroundResourceProcessing.Solver.Solver();

            var rates = solver.ComputeInventoryRates(processor);

            // The first inventory here has ElectricCharge and its rate of
            // change should be exactly 0.
            Assert.AreEqual(0.0, rates.inventoryRates[0], 0.0);
        }

        [TestMethod]
        public void TestUnderflowInstability()
        {
            var processor = TestUtil.LoadVessel("regression/underflow-instability.cfg");
            var solver = new BackgroundResourceProcessing.Solver.Solver();

            var rates = solver.ComputeInventoryRates(processor);

            // All inventories in this test case should have a rate of 0.
            for (int i = 0; i < rates.inventoryRates.Length; ++i)
                Assert.AreEqual(0.0, rates.inventoryRates[i], 0.0);
        }

        [TestMethod]
        public void TestZeroRates()
        {
            var processor = TestUtil.LoadVessel("regression/zero-rates.cfg");
            var solver = new BackgroundResourceProcessing.Solver.Solver();
            var rates = solver.ComputeInventoryRates(processor);

            Assert.AreNotEqual(0.0, rates.inventoryRates[2]);
        }

        [TestMethod]
        public void TestFertilizerOutOfThinAir()
        {
            var processor = TestUtil.LoadVessel("regression/fertilizer-out-of-thin-air.cfg");
            var solver = new BackgroundResourceProcessing.Solver.Solver();
            var rates = solver.ComputeInventoryRates(processor);

            for (int i = 0; i < processor.inventories.Count; ++i)
            {
                if (processor.inventories[i].ResourceName != "Fertilizer")
                    continue;

                Assert.AreEqual(0.0, rates.inventoryRates[i]);
            }
        }

        [TestMethod]
        public void TestBoiloffIgnored()
        {
            var processor = TestUtil.LoadVessel("regression/boiloff-ignored.cfg");
            var solver = new BackgroundResourceProcessing.Solver.Solver();
            var rates = solver.ComputeInventoryRates(processor);

            for (int i = 0; i < processor.converters.Count; ++i)
            {
                var converter = processor.converters[i];

                if (!converter.Inputs.ContainsKey("ElectricCharge".GetHashCode()))
                    continue;
                if (!converter.Inputs.ContainsKey("BRPCryoTankBoiloff".GetHashCode()))
                    continue;

                Assert.AreNotEqual(0.0, rates.converterRates[i]);
            }
        }

        [TestMethod]
        public void TestBoiloffEcIgnored()
        {
            var processor = TestUtil.LoadVessel("regression/boiloff-ec-ignored.cfg");
            var solver = new BackgroundResourceProcessing.Solver.Solver();
            var rates = solver.ComputeInventoryRates(processor);

            for (int i = 0; i < processor.inventories.Count; ++i)
            {
                var inventory = processor.inventories[i];
                if (inventory.ResourceName != "ElectricCharge")
                    continue;

                Assert.AreNotEqual(0.0, rates.inventoryRates[i]);
            }
        }

        [TestMethod]
        public void TestCrashBadBchoiceAccess()
        {
            var processor = TestUtil.LoadVessel("regression/crash-bad-bchoice-access.cfg");
            var solver = new BackgroundResourceProcessing.Solver.Solver();
            solver.ComputeInventoryRates(processor);
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
    }
}
