using System.Collections.Generic;
using UnifiedBackgroundProcessing.Core;

namespace UnifiedBackgroundProcessing.Solver
{
    public interface ISolver
    {
        public Dictionary<InventoryId, double> ComputeInventoryRates(ResourceProcessor processor);
    }
}
