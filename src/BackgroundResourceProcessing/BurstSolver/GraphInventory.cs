namespace BackgroundResourceProcessing.BurstSolver;

using System;
using BackgroundResourceProcessing.Core;
using Unity.Burst.CompilerServices;
using InventoryState = BackgroundResourceProcessing.Solver.InventoryState;

internal struct GraphInventory
{
    public InventoryState state;
    public int baseId;
    public int resourceId;
    public double amount;
    public double maxAmount;

    public GraphInventory(ResourceInventory inventory, int id)
    {
        resourceId = inventory.ResourceId;
        baseId = id;
        amount = inventory.Amount;
        maxAmount = inventory.MaxAmount;

        state = InventoryState.Unconstrained;
        if (inventory.Full)
            state |= InventoryState.Full;
        if (inventory.Empty)
            state |= InventoryState.Empty;
    }

    public void Merge(in GraphInventory other)
    {
        if (resourceId != other.resourceId)
            ThrowInvalidMergeException();

        state &= other.state;
        amount += other.amount;
        maxAmount += other.maxAmount;
        baseId = Math.Min(baseId, other.baseId);
    }

    [IgnoreWarning(1370)]
    private static void ThrowInvalidMergeException() =>
        throw new InvalidOperationException(
            $"Attempted to merge two inventories with different resources)"
        );
}
