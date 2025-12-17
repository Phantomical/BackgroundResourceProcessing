using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using BackgroundResourceProcessing.Collections;
using BackgroundResourceProcessing.Core;

namespace BackgroundResourceProcessing.Utils;

[method: MethodImpl(MethodImplOptions.AggressiveInlining)]
internal struct ResourceInventoryEnumerator(
    IResourceProcessor processor,
    int resourceId,
    bool includeModuleInventories
) : IEnumerator<ResourceInventory>
{
    StableListView<ResourceInventory>.Enumerator enumerator = processor.Inventories.GetEnumerator();
    readonly int resourceId = resourceId;
    readonly bool includeModuleInventories = includeModuleInventories;

    public ResourceInventory Current
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get { return enumerator.Current; }
    }
    object IEnumerator.Current => Current;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool MoveNext()
    {
        while (enumerator.MoveNext())
        {
            var inventory = enumerator.Current;
            if (inventory.ResourceId != resourceId)
                continue;
            if (!includeModuleInventories && inventory.ModuleId is not null)
                continue;

            return true;
        }

        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Reset()
    {
        CallReset(ref enumerator);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Dispose()
    {
        enumerator.Dispose();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void CallReset<T>(ref T value)
        where T : struct, IEnumerator<ResourceInventory>
    {
        value.Reset();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly ResourceInventoryEnumerator GetEnumerator()
    {
        return this;
    }
}
