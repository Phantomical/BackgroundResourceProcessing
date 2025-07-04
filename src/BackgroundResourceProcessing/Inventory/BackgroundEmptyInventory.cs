using System.Collections.Generic;
using BackgroundResourceProcessing.Collections;
using BackgroundResourceProcessing.Core;

namespace BackgroundResourceProcessing.Inventory;

/// <summary>
/// This creates an empty inventory with the specified resource name.
///
/// It's not useful on its own but is useful as a component of linking
/// together a larger network of converters.
/// </summary>
public sealed class BackgroundEmptyInventory : BackgroundInventory
{
    public List<string> ResourceNames;

    public override List<FakePartResource> GetResources(PartModule module)
    {
        if (ResourceNames == null || ResourceNames.Count == 0)
            return null;

        var list = new List<FakePartResource>(ResourceNames.Count);
        foreach (var resource in ResourceNames)
            list.Add(new() { resourceName = resource });

        return list;
    }

    public override void UpdateResource(PartModule module, ResourceInventory inventory) { }

    public override void Load(ConfigNode node)
    {
        base.Load(node);

        ResourceNames = [.. node.GetValues("ResourceName")];
        ResourceNames.Sort();
        ResourceNames.Deduplicate();
    }
}
