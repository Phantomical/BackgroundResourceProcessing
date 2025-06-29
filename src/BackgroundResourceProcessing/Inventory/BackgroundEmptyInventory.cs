using System.Collections.Generic;
using BackgroundResourceProcessing.Core;

namespace BackgroundResourceProcessing.Inventory
{
    /// <summary>
    /// This creates an empty inventory with the specified resource name.
    ///
    /// It's not useful on its own but is useful as a component of linking
    /// together a larger network of converters.
    /// </summary>
    public class BackgroundEmptyInventory : BackgroundInventory
    {
        [KSPField]
        public string ResourceName;

        public override List<FakePartResource> GetResources(PartModule module)
        {
            if (ResourceName == null)
                return null;

            return [new() { resourceName = ResourceName }];
        }

        public override void UpdateResource(PartModule module, ResourceInventory inventory) { }
    }
}
