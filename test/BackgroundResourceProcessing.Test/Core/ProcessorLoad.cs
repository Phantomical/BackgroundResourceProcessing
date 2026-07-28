using BackgroundResourceProcessing.Core;
using BackgroundResourceProcessing.Utils;
using KSP.Testing;

namespace BackgroundResourceProcessing.Test.Core;

/// <summary>
/// Tests for loading a persisted <see cref="ResourceProcessor"/> whose save
/// data has been edited or corrupted.
/// </summary>
public sealed class ProcessorLoadTests : BRPTestBase
{
    // The default test log sink throws on any error. Dropping a dangling
    // inventory reference is an expected error here, so count them instead.
    sealed class CountingSink : LogUtil.ILogSink
    {
        public int Errors;

        public void Error(string message) => Errors++;

        public void Log(string message) { }

        public void Warn(string message) { }
    }

    static ResourceProcessor LoadVessel(string name, out int errors)
    {
        var saved = LogUtil.Sink;
        var sink = new CountingSink();
        LogUtil.Sink = sink;

        try
        {
            var processor = TestUtil.LoadVessel(name);
            errors = sink.Errors;
            return processor;
        }
        finally
        {
            LogUtil.Sink = saved;
        }
    }

    static ResourceProcessor Load(ConfigNode node, out int errors)
    {
        var saved = LogUtil.Sink;
        var sink = new CountingSink();
        LogUtil.Sink = sink;

        try
        {
            ResourceProcessor processor = new();
            processor.Load(node);
            errors = sink.Errors;
            return processor;
        }
        finally
        {
            LogUtil.Sink = saved;
        }
    }

    /// <summary>
    /// Inventory indices that refer to an inventory that isn't there used to be
    /// dereferenced unguarded while loading, which threw and aborted the load of
    /// the whole vessel module.
    /// </summary>
    [TestInfo("ProcessorLoadTests_DropsDanglingInventoryIndices")]
    public void DropsDanglingInventoryIndices()
    {
        var processor = LoadVessel("regression/dangling-inventory-index.cfg", out var errors);
        var converter = processor.converters[0];

        Assert.IsTrue(errors > 0, "dropping a dangling inventory reference should be logged");

        Assert.IsTrue(converter.Push.Contains(1));
        Assert.IsFalse(converter.Push.Contains(9));
        Assert.IsTrue(converter.Pull.Contains(0));
        Assert.IsTrue(converter.Constraint.Contains(1));
        Assert.IsFalse(converter.Constraint.Contains(7));

        // The reference that survived still evaluates against the real
        // inventory: 9000 EC is above the 5000 EC AT_LEAST requirement.
        Assert.AreEqual(ConstraintState.ENABLED, converter.ConstraintState);
    }

    /// <summary>
    /// Removing an inventory leaves a hole behind, because the indices of the
    /// inventories that follow it must not shift. Saving records the indices
    /// that survived, so they are sparse, and loading has to put everything
    /// back exactly where it was.
    /// </summary>
    [TestInfo("ProcessorLoadTests_PreservesSparseIndices")]
    public void PreservesSparseIndices()
    {
        var original = TestUtil.LoadVessel("constraint/at-least-enabled.cfg");

        // Drop the LiquidFuel inventory at index 0, the way
        // BackgroundResourceProcessor.RemoveInventory does.
        foreach (var conv in original.converters)
        {
            conv.Push[0] = false;
            conv.Pull[0] = false;
            conv.Constraint[0] = false;
        }
        original.inventories.RemoveAt(0);

        ConfigNode node = new();
        original.Save(node);

        var processor = Load(node, out var errors);

        Assert.AreEqual(0, errors, "a sparse save should load without complaint");
        Assert.AreEqual(2, processor.inventories.Count);
        Assert.IsTrue(processor.inventories[0] is null, "index 0 should still be a hole");

        var inventory = processor.inventories[1];
        Assert.IsTrue(inventory is not null, "the inventory should reload at index 1");
        Assert.AreEqual("ElectricCharge", inventory.ResourceName);

        // The id -> index map has to agree with where the inventory landed,
        // otherwise every lookup by InventoryId resolves to the wrong slot.
        Assert.AreEqual(1, processor.inventoryIds[inventory.Id]);

        var converter = processor.converters[0];
        Assert.IsTrue(converter.Constraint.Contains(1));
        Assert.AreEqual(ConstraintState.ENABLED, converter.ConstraintState);
    }
}
