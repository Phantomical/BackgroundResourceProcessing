using System.Linq;
using BackgroundResourceProcessing.Collections.Burst;
using KSP.Testing;

namespace BackgroundResourceProcessing.Test.Solver
{
    public sealed class RegressionTests : BRPTestBase
    {
        [TestInfo("RegressionTests_TestCrashSplitOutput")]
        public void TestCrashSplitOutput()
        {
            var processor = TestUtil.LoadVessel("regression/crash-split-output.cfg");
            using var solve = processor.ComputeRates();
            solve.Complete();

            Assert.AreNotEqual(0.0, processor.converters[12].Rate);
        }

        [TestInfo("RegressionTests_TestCrashBadVariableSelection")]
        public void TestCrashBadVariableSelection()
        {
            var processor = TestUtil.LoadVessel("regression/crash-bad-var-selection.cfg");
            using var solve = processor.ComputeRates();
            solve.Complete();
        }

        [TestInfo("RegressionTests_TestCrashSelectedSlack")]
        public void TestCrashSelectedSlack()
        {
            var processor = TestUtil.LoadVessel("regression/crash-selected-slack.cfg");
            using var solve = processor.ComputeRates();
            solve.Complete();
        }

        [TestInfo("RegressionTests_TestFullDumpExcess")]
        public void TestFullDumpExcess()
        {
            var processor = TestUtil.LoadVessel("regression/crash-full-dump-excess.cfg");
            using var solve = processor.ComputeRates();
            solve.Complete();
        }

        [TestInfo("RegressionTests_TestDisjunctionInstability")]
        public void TestDisjunctionInstability()
        {
            var processor = TestUtil.LoadVessel("regression/disjunction-instability.cfg");
            using var solve = processor.ComputeRates();
            solve.Complete();

            // Converter 0 is an EL workshop with negative crew productivity, so
            // its BRPELWorkHours output ratio is negative. Its two work-hour
            // inventories are zero-capacity, which pins their rate to exactly 0,
            // and a negative output cannot satisfy that at any nonzero rate --
            // so the workshop has to be off.
            //
            // This is also what keeps negative output ratios from being clamped
            // the way negative *inputs* are: doing so here would zero the ratio,
            // leave the rate unconstrained, and report an idle workshop as
            // running at 100%.
            Assert.AreEqual(0.0, processor.converters[0].Rate, 0.0);
        }

        [TestInfo("RegressionTests_TestUnderflowInstability")]
        public void TestUnderflowInstability()
        {
            var processor = TestUtil.LoadVessel("regression/underflow-instability.cfg");
            using var solve = processor.ComputeRates();
            solve.Complete();

            // All inventories in this test case should have a rate of 0.
            for (int i = 0; i < processor.inventories.Count; ++i)
                Assert.AreEqual(0.0, processor.inventories[i].Rate);
        }

        [TestInfo("RegressionTests_TestZeroRates")]
        public void TestZeroRates()
        {
            var processor = TestUtil.LoadVessel("regression/zero-rates.cfg");
            using var solve = processor.ComputeRates();
            solve.Complete();

            Assert.AreNotEqual(0.0, processor.inventories[2].Rate);
        }

        [TestInfo("RegressionTests_TestFertilizerOutOfThinAir")]
        public void TestFertilizerOutOfThinAir()
        {
            var processor = TestUtil.LoadVessel("regression/fertilizer-out-of-thin-air.cfg");
            using var solve = processor.ComputeRates();
            solve.Complete();

            for (int i = 0; i < processor.inventories.Count; ++i)
            {
                if (processor.inventories[i].ResourceName != "Fertilizer")
                    continue;

                Assert.AreEqual(0.0, processor.inventories[i].Rate);
            }
        }

        [TestInfo("RegressionTests_TestBoiloffIgnored")]
        public void TestBoiloffIgnored()
        {
            var processor = TestUtil.LoadVessel("regression/boiloff-ignored.cfg");
            using var solve = processor.ComputeRates();
            solve.Complete();

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

        [TestInfo("RegressionTests_TestBoiloffEcIgnored")]
        public void TestBoiloffEcIgnored()
        {
            var processor = TestUtil.LoadVessel("regression/boiloff-ec-ignored.cfg");
            using var solve = processor.ComputeRates();
            solve.Complete();

            for (int i = 0; i < processor.inventories.Count; ++i)
            {
                var inventory = processor.inventories[i];
                if (inventory.ResourceName != "ElectricCharge")
                    continue;

                Assert.AreNotEqual(0.0, inventory.Rate);
            }
        }

        [TestInfo("RegressionTests_TestCrashBadBchoiceAccess")]
        public void TestCrashBadBchoiceAccess()
        {
            var processor = TestUtil.LoadVessel("regression/crash-bad-bchoice-access.cfg");
            using var solve = processor.ComputeRates();
            solve.Complete();
        }

        [TestInfo("RegressionTests_TestCrashNoProgress")]
        public void TestCrashNoProgress()
        {
            var processor = TestUtil.LoadVessel("regression/crash-bad-bchoice-access.cfg");
            using var solve = processor.ComputeRates();
            solve.Complete();
            var changepoint = processor.ComputeNextChangepoint(0.0);

            Assert.AreNotEqual(0.0, changepoint);
        }

        [TestInfo("RegressionTests_TestCrashCreateSimplexTableau")]
        public void TestCrashCreateSimplexTableau()
        {
            var processor = TestUtil.LoadVessel("regression/crash-create-simplex-tableau.cfg");
            using var solve = processor.ComputeRates();
            solve.Complete();
        }

        [TestInfo("RegressionTests_TestZeroRankFunction")]
        public void TestZeroRankFunction()
        {
            var processor = TestUtil.LoadVessel("regression/crash-create-simplex-tableau.cfg");
            using var solve = processor.ComputeRates();
            solve.Complete();

            Assert.IsTrue(
                processor.converters.Any(converter => converter.Rate != 0.0),
                "at least some converters should be running"
            );
        }

        [TestInfo("RegressionTests_TestBoiloffUnexpected")]
        public void TestBoiloffUnexpected()
        {
            var processor = TestUtil.LoadVessel("regression/boiloff-unexpected.cfg");
            using var solve = processor.ComputeRates();
            solve.Complete();

            var h2 = processor.GetResourceStates()["LqdHydrogen"];

            Assert.AreEqual(0.0, h2.rate, "LH2 rate should be 0");
        }

        [TestInfo("RegressionTests_TestSolarPanelZeroRate")]
        public void TestSolarPanelZeroRate()
        {
            var processor = TestUtil.LoadVessel("regression/solar-panel-zero-rate.cfg");
            using var solve = processor.ComputeRates();
            solve.Complete();

            // Solar panels are converters 6-9. They should have nonzero rates
            // since there are EC consumers (radiators, command, drill) and there
            // is room for the solar panels to produce EC to compensate.
            for (int i = 6; i <= 9; ++i)
            {
                Assert.AreNotEqual(
                    0.0,
                    processor.converters[i].Rate,
                    $"Solar panel converter {i} should have a nonzero rate"
                );
            }
        }

        [TestInfo("RegressionTests_TestSolarPanelZeroRateWithHalfFullTanks")]
        public void TestSolarPanelZeroRateWithHalfFullTanks()
        {
            // Same vessel but with LF/Ox tanks at 50% (representing the
            // state when rates were originally computed, before tanks filled up).
            // EC is still full. Solar panels should produce EC to offset ISRU consumption.
            var processor = TestUtil.LoadVessel("regression/solar-panel-zero-rate-2.cfg");
            using var solve = processor.ComputeRates();
            solve.Complete();

            // ISRU should be running (Ox and LF tanks not full)
            Assert.AreNotEqual(
                0.0,
                processor.converters[0].Rate,
                "ISRU should be running since LF/Ox tanks are not full"
            );

            // Solar panels should be running to offset EC consumption
            for (int i = 6; i <= 9; ++i)
            {
                Assert.AreNotEqual(
                    0.0,
                    processor.converters[i].Rate,
                    $"Solar panel converter {i} should have a nonzero rate"
                );
            }
        }

        [TestInfo("RegressionTests_TestEmptyInventoryCharges")]
        public void TestEmptyInventoryCharges()
        {
            // An empty EC store with a producer (+10) and a consumer (-1).
            // Net production is +9, so the store must charge. A solver bug
            // negated the empty-inventory rate equation in place, so the
            // store read back as draining and stayed pinned at 0.
            var processor = TestUtil.LoadVessel("regression/empty-inventory-charge.cfg");
            using var solve = processor.ComputeRates();
            solve.Complete();

            Assert.AreEqual(9.0, processor.inventories[0].Rate);
        }

        [TestInfo("RegressionTests_TestDumpExcessSplitOutput")]
        public void TestDumpExcessSplitOutput()
        {
            // Converter 0 dumps excess Ore into a full tank, which is what
            // creates a dRates entry for that tank. Converter 1 then splits its
            // Ore output between the same full tank and an empty one.
            //
            // dRates tracks everything that is *not* dumped, so converter 1's
            // contribution to the full tank is the amount it routes there --
            // the per-inventory split variable, not the converter's whole
            // output. Adding the aggregate makes the full tank's dRate <= 0
            // constraint read as "converter 1 must be off" instead of
            // "converter 1 must route its Ore elsewhere", so it shuts down
            // despite having an empty tank to fill.
            var processor = TestUtil.LoadVessel("regression/dump-excess-split-output.cfg");
            using var solve = processor.ComputeRates();
            solve.Complete();

            Assert.AreEqual(
                1.0,
                processor.converters[1].Rate,
                1e-6,
                "converter 1 should run at full rate, filling the empty tank"
            );
            Assert.AreEqual(
                2.0,
                processor.inventories[1].Rate,
                1e-6,
                "all of converter 1's output should flow into the empty tank"
            );
        }

        [TestInfo("RegressionTests_TestNegativeInputRatio")]
        public void TestNegativeInputRatio()
        {
            // A negative input ratio is nonsense, but nothing between the
            // behaviour and the LP checks the sign. Subtracting it from the
            // inventory rate flips it into net production, so the converter
            // fabricates the resource it claims to consume.
            //
            // Note this is specific to inputs. A negative *output* is a
            // legitimate way to express a net drain and is left alone -- see
            // TestDisjunctionInstability, whose EL workshop depends on it.
            var processor = TestUtil.LoadVessel("regression/negative-input-ratio.cfg");
            using var solve = processor.ComputeRates();
            solve.Complete();

            Assert.AreEqual(
                0.0,
                processor.inventories[0].Rate,
                "a negative input ratio must not produce resources"
            );
        }
    }
}
