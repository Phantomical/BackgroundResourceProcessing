using System.Security.Principal;
using BackgroundResourceProcessing.Collections;
using BackgroundResourceProcessing.Solver;
using BackgroundResourceProcessing.Solver.V1;

namespace BackgroundResourceProcessing.Test.Solver.V1
{
    [TestClass]
    public sealed class V1SolverTest
    {
        V1Solver solver = new V1Solver();

        [TestMethod]
        public void SolveSolarPanelOnly()
        {
            var module = TestUtil.LoadVessel("solar-panels-only.cfg");
            var rates = solver.ComputeInventoryRates(module);

            foreach (var (id, rate) in rates.KSPEnumerate())
            {
                if (id.resourceName != "ElectricCharge")
                    continue;

                Assert.AreEqual(0.0, rate);
            }
        }

        [TestMethod]
        public void SolveISRU1()
        {
            var module = TestUtil.LoadVessel("isru-1/default.cfg");
            var instance = new SolverInstance(module);
            var constraints = instance.Constraints;

            var iRates = instance.ComputeInventoryRates();
            var cRates = instance.GetConverterRatesById();

            Assert.AreEqual(InventoryState.Empty, constraints["Ore"]);
            Assert.AreEqual(InventoryState.Unconstrained, constraints["ElectricCharge"]);
            Assert.AreEqual(InventoryState.Unconstrained, constraints["LiquidFuel"]);
            Assert.AreEqual(InventoryState.Unconstrained, constraints["Oxidizer"]);

            // The only converter not running full-tilt is the LFO+Ox converter
            Assert.IsTrue(cRates[0] < 1.0);
            Assert.IsTrue(cRates[0] > 0.0);

            for (int i = 1; i < 10; ++i)
                Assert.AreEqual(1.0, cRates[i]);
        }

        [TestMethod]
        public void SolveISRU1WithZeroEC()
        {
            var module = TestUtil.LoadVessel("isru-1/zero-ec.cfg");
            var instance = new SolverInstance(module);
            var constraints = instance.Constraints;

            var iRates = instance.ComputeInventoryRates();
            var cRates = instance.GetConverterRatesById();

            Assert.AreEqual(InventoryState.Empty, constraints["Ore"]);
            Assert.AreEqual(InventoryState.Empty, constraints["ElectricCharge"]);
            Assert.AreEqual(InventoryState.Unconstrained, constraints["LiquidFuel"]);
            Assert.AreEqual(InventoryState.Unconstrained, constraints["Oxidizer"]);

            Assert.IsTrue(instance.RatesSatisfyConstraints());

            // The only converter not running full-tilt is the LFO+Ox converter
            Assert.IsTrue(cRates[0] < 1.0);
            Assert.IsTrue(cRates[0] > 0.0);

            for (int i = 1; i < 10; ++i)
                Assert.AreEqual(1.0, cRates[i]);
        }

        [TestMethod]
        public void SolveISRU1WithZeroECAndFullOre()
        {
            var module = TestUtil.LoadVessel("isru-1/zero-ec-full-ore.cfg");
            var instance = new SolverInstance(module);
            var constraints = instance.Constraints;

            instance.ComputeInventoryRates();
            var cRates = instance.GetConverterRatesById();

            Assert.AreEqual(InventoryState.Full, constraints["Ore"]);
            Assert.AreEqual(InventoryState.Empty, constraints["ElectricCharge"]);
            Assert.AreEqual(InventoryState.Unconstrained, constraints["LiquidFuel"]);
            Assert.AreEqual(InventoryState.Unconstrained, constraints["Oxidizer"]);

            Assert.IsTrue(instance.RatesSatisfyConstraints());
        }

        [TestMethod]
        public void SolveISRU1WithFullECAndFullOre()
        {
            var module = TestUtil.LoadVessel("isru-1/full-ec-full-ore.cfg");
            var instance = new SolverInstance(module);
            var constraints = instance.Constraints;

            instance.ComputeInventoryRates();
            var cRates = instance.GetConverterRatesById();

            Assert.AreEqual(InventoryState.Full, constraints["Ore"]);
            Assert.AreEqual(InventoryState.Full, constraints["ElectricCharge"]);
            Assert.AreEqual(InventoryState.Unconstrained, constraints["LiquidFuel"]);
            Assert.AreEqual(InventoryState.Unconstrained, constraints["Oxidizer"]);

            Assert.IsTrue(instance.RatesSatisfyConstraints());
        }

        [TestMethod]
        public void SolveISRU1Full()
        {
            var module = TestUtil.LoadVessel("isru-1/all-full.cfg");
            var instance = new SolverInstance(module);
            var constraints = instance.Constraints;

            instance.ComputeInventoryRates();
            var cRates = instance.GetConverterRatesById();

            Assert.AreEqual(InventoryState.Full, constraints["Ore"]);
            Assert.AreEqual(InventoryState.Full, constraints["ElectricCharge"]);
            Assert.AreEqual(InventoryState.Full, constraints["LiquidFuel"]);
            Assert.AreEqual(InventoryState.Full, constraints["Oxidizer"]);

            // All the solar panels should be running full-blast since they
            // have DumpExcess = true
            Assert.AreEqual(1.0, cRates[7]);
            Assert.AreEqual(1.0, cRates[8]);
            Assert.AreEqual(1.0, cRates[9]);

            // Everything else should not be running.
            for (int i = 0; i < 7; ++i)
                Assert.AreEqual(0.0, cRates[i]);
        }
    }
}
