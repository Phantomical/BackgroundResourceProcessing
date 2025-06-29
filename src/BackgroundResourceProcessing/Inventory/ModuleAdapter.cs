using System.Collections.Generic;
using BackgroundResourceProcessing.Core;
using BackgroundResourceProcessing.Module;

namespace BackgroundResourceProcessing.Inventory
{
    /// <summary>
    /// An inventory adapter that defers to the implementation of the
    /// <see cref="IBackgroundInventory"/> interface on the part module.
    /// </summary>
    public class ModuleAdapter : BackgroundInventory<IBackgroundInventory>
    {
        public override List<FakePartResource> GetResources(IBackgroundInventory module)
        {
            return module.GetResources();
        }

        public override void UpdateResource(
            IBackgroundInventory module,
            ResourceInventory inventory
        )
        {
            module.UpdateResource(inventory);
        }
    }
}
