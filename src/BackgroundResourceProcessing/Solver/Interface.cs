using System.Collections.Generic;
using BackgroundResourceProcessing.Core;

namespace BackgroundResourceProcessing.Solver
{
    public interface ISolver
    {
        public Dictionary<InventoryId, double> ComputeInventoryRates(ResourceProcessor processor);
    }
}
