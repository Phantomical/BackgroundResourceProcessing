namespace BackgroundResourceProcessing.Solver;

public enum InventoryState : byte
{
    /// <summary>
    /// There is no constraint on flow through this inventory.
    /// </summary>
    Unconstrained = 0,

    /// <summary>
    /// This inventory is empty. Inflow must be greater than or equal to outflow.
    /// </summary>
    Empty = 1,

    /// <summary>
    /// This inventory is full. Inflow must be greater than or equal to inflow.
    /// </summary>
    Full = 2,

    /// <summary>
    /// This inventory has size zero. Inflow must equal outflow.
    /// </summary>
    Zero = 3,
}
