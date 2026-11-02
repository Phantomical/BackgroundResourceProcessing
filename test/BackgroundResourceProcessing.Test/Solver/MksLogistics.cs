using KSP.Testing;

namespace BackgroundResourceProcessing.Test.Solver;

/// <summary>
/// Golden tests for the converter+constraint model the MKS Planetary Logistics
/// integration builds: a net producer/consumer whose surplus/deficit is moved
/// to and from an infinite pool inventory through a transfer converter gated on
/// the real tank's fill level (MKS's 25%/75% triggers).
/// </summary>
public class MksLogisticsTests : BRPTestBase
{
    // A net producer at the 75% offload trigger should hold the tank at the
    // trigger and route the full surplus into the pool.
    [TestInfo("MksLogisticsTests_TestOffloadHoldsTankAndFillsPool")]
    public void TestOffloadHoldsTankAndFillsPool()
    {
        var processor = TestUtil.LoadVessel("mks/offload.cfg");
        using var solve = processor.ComputeRates();
        solve.Complete();

        // Producer and push transfer both run at full rate.
        Assert.AreEqual(1.0, processor.converters[0].Rate);
        Assert.AreEqual(1.0, processor.converters[1].Rate);

        // The tank is pinned at the trigger; the surplus flows into the pool.
        Assert.AreEqual(0.0, processor.inventories[0].Rate);
        Assert.AreEqual(10.0, processor.inventories[1].Rate);
    }

    // A net consumer at the 25% refill trigger should hold the tank at the
    // trigger and draw the deficit from the pool while it has balance.
    [TestInfo("MksLogisticsTests_TestRefillHoldsTankAndDrainsPool")]
    public void TestRefillHoldsTankAndDrainsPool()
    {
        var processor = TestUtil.LoadVessel("mks/refill.cfg");
        using var solve = processor.ComputeRates();
        solve.Complete();

        // Consumer and pull transfer both run at full rate.
        Assert.AreEqual(1.0, processor.converters[0].Rate);
        Assert.AreEqual(1.0, processor.converters[1].Rate);

        // The tank is pinned at the trigger; the deficit drains the pool.
        Assert.AreEqual(0.0, processor.inventories[0].Rate);
        Assert.AreEqual(-10.0, processor.inventories[1].Rate);
    }

    // The pool has a finite balance (100) draining at 10/s, so the vessel's next
    // changepoint should be when the pool empties at t = 10.
    [TestInfo("MksLogisticsTests_TestRefillPoolEmptyChangepoint")]
    public void TestRefillPoolEmptyChangepoint()
    {
        var processor = TestUtil.LoadVessel("mks/refill.cfg");
        using var solve = processor.ComputeRates();
        solve.Complete();

        Assert.AreEqual(10.0, processor.UpdateNextChangepoint(0.0));
    }
}
