using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using KERBALISM;
using static BackgroundResourceProcessing.Integration.Kerbalism.SymbolUtils;

namespace BackgroundResourceProcessing.Integration.Kerbalism;

public class WrapInventory() : ResourceInfo.Wrap
{
    internal static ResourceInfo.CachedObject<WrapInventory> cache = new();

    private BackgroundResourceProcessor processor;
    private Core.ResourceInventory inventory;
    private int index;

    public override double amount
    {
        get => inventory.Amount;
        set
        {
            inventory.Amount = value;
            processor.MarkInventoryDirty(index);
        }
    }

    public override double maxAmount
    {
        get => inventory.MaxAmount;
        set
        {
            inventory.MaxAmount = value;
            processor.MarkInventoryDirty(index);
        }
    }

    public void Link(BackgroundResourceProcessor processor, int index)
    {
        this.inventory = processor.Inventories[index];
        this.processor = processor;
        this.index = index;
    }

    public override void Reset()
    {
        processor = null;
        inventory = null;
        index = -1;
    }
}

[HarmonyPatch(typeof(VesselResources), nameof(VesselResources.Sync))]
static class VesselResources_Sync_Patch
{
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    static readonly FieldInfo PtsField = typeof(ResourceInfo).GetField("pts", Flags);

    static IEnumerable<CodeInstruction> Transpiler(
        IEnumerable<CodeInstruction> instructions,
        ILGenerator generator
    )
    {
        var matcher = new CodeMatcher(instructions, generator);
        var processor = generator.DeclareLocal(typeof(BackgroundResourceProcessor));

        var protoVesselField = GetFieldInfo(() => default(Vessel).protoVessel);
        var addToSyncSetMethod = SymbolExtensions.GetMethodInfo<ResourceInfo>(info =>
            info.AddToSyncSet(default(ProtoPartResourceSnapshot), 0)
        );
        var enumeratorCurrent = GetPropertyInfo(() =>
            default(List<ProtoPartSnapshot>.Enumerator).Current
        ).GetMethod;

        // Load the resource processor once at the start so we don't need to look it
        // up for every iteration of the loop.
        matcher
            .MatchStartForward(new CodeMatch(OpCodes.Ldfld, protoVesselField))
            .ThrowIfInvalid("Unable to find start of unloaded loop")
            .Insert(
                new CodeInstruction(OpCodes.Dup),
                CodeInstruction.Call<Vessel>(v => GetVesselProcessor(v)),
                new CodeInstruction(OpCodes.Stloc, processor.LocalIndex)
            );

        // Find the local index storing the current ProtoPartSnapshot
        matcher
            .MatchStartForward(new CodeMatch(OpCodes.Call, enumeratorCurrent))
            .ThrowIfInvalid("Unable to find call to List<ProtoPartSnapshot>.Enumerator.get_Current")
            .Advance(1);

        if (!GetStoreIndex(matcher.Instruction, out var partIndex))
            throw new Exception(
                $"Enumerator<ProtoPartSnapshot>.get_Current was not followed by a stloc instruction: {matcher.Instruction}"
            );

        // Finally, replace the existing call to AddToSyncSet with our own that
        // returns a custom wrapper class.
        matcher
            .MatchStartForward(new CodeMatch(OpCodes.Callvirt, addToSyncSetMethod))
            .ThrowIfInvalid("Unable to find call to AddToSyncSet(ProtoPartResourceSnapshot, int)")
            .RemoveInstruction()
            .Insert(
                new CodeInstruction(OpCodes.Ldloc, processor.LocalIndex),
                new CodeInstruction(OpCodes.Ldloc, partIndex),
                CodeInstruction.Call(() => AddToSyncSet(null, null, 0, null, null))
            );

        return matcher.Instructions();
    }

    public static BackgroundResourceProcessor GetVesselProcessor(Vessel v)
    {
        var processor = v.FindVesselModuleImplementing<BackgroundResourceProcessor>();

        // Make sure that the processor is up-to-date before Kerbalism needs to look at it.
        processor.UpdateBackgroundState();

        return processor;
    }

    public static void AddToSyncSet(
        ResourceInfo info,
        ProtoPartResourceSnapshot resource,
        int priority,
        BackgroundResourceProcessor processor,
        ProtoPartSnapshot part
    )
    {
        var id = processor.GetInventoryIndex(new(part.flightID, resource.resourceName));
        if (id is null)
        {
            info.AddToSyncSet(resource, priority);
            return;
        }

        var pts = (ResourceInfo.PriorityTankSets)PtsField.GetValue(info);
        WrapInventory wrap = WrapInventory.cache.Next();
        wrap.Link(processor, (int)id);
        pts.Add(wrap, priority);
    }

    private static bool GetStoreIndex(CodeInstruction inst, out int index)
    {
        index = -1;

        var opcode = inst.opcode;
        if (opcode == OpCodes.Stloc || opcode == OpCodes.Stloc_S)
            index = ((LocalBuilder)inst.operand).LocalIndex;
        else if (opcode == OpCodes.Stloc_0)
            index = 0;
        else if (opcode == OpCodes.Stloc_1)
            index = 1;
        else if (opcode == OpCodes.Stloc_2)
            index = 2;
        else if (opcode == OpCodes.Stloc_3)
            index = 3;
        else
            return false;

        return true;
    }
}

[HarmonyPatch(typeof(ResourceInfo), nameof(ResourceInfo.ResetSyncCaches))]
static class ResourceInfo_ResetSyncCaches_Patch
{
    static void Postfix()
    {
        WrapInventory.cache.Reset();
    }
}
