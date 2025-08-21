using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace BackgroundResourceProcessing.Integration.AutomaticLabHousekeeper;

[KSPAddon(KSPAddon.Startup.Instantly, true)]
public class Loader : MonoBehaviour
{
    void Awake()
    {
        Harmony harmony = new("BackgroundResourceProcessing.Integration.AutomaticLabHousekeeper");
        harmony.PatchAll(typeof(Loader).Assembly);
    }
}

[HarmonyPatch(typeof(ALH.AutomaticLabHousekeeper))]
[HarmonyPatch("SimulateScienceProcessingForUnloadedLab")]
public static class AutomaticLabHousekeeper_SimulateScienceProcessingForUnloadedLab_Patch
{
    static bool Prefix(Vessel vessel)
    {
        var settings = HighLogic.CurrentGame.Parameters.CustomParams<Settings>();
        if (!settings.EnableBackgroundScienceLabProcessing)
            return true;

        Debug.Log(
            $"[AutomaticLabHousekeeper] Simulation of unloaded lab in vessel {vessel.vesselName} suppressed by BackgroundResourceProcessing"
        );

        // BRP already implements the background simulation so we just tell it
        // to sync those changes with current part modules.
        var processor = vessel.FindVesselModuleImplementing<BackgroundResourceProcessor>();
        processor.UpdateBackgroundState();

        // Nothing else to do in this case.
        return false;
    }
}

[HarmonyPatch(typeof(ALH.AutomaticLabHousekeeper))]
[HarmonyPatch("PullDataIntoUnloadedLab")]
public static class AutomaticLabHousekeeper_PullDataIntoUnloadedLab_Patch
{
    static IEnumerable<CodeInstruction> Transpiler(
        IEnumerable<CodeInstruction> instructions,
        ILGenerator generator
    )
    {
        // This transpiler patches the following method:
        // https://github.com/ItWorkedInCAD/AutomaticLabHousekeeper/blob/main/source/AutomaticLabHousekeeper/ALH_Main.cs#L446

        // All we really want to do here is insert a method call at the very
        // end of the method, so it only gets run if ALH actually attempted to
        // add science to the lab.

        var matcher = new CodeMatcher(instructions, generator);

        // matcher
        //     .MatchEndBackwards(new CodeMatch(OpCodes.Blt))
        //     .ThrowIfInvalid("Could not find a final blt instruction")
        //     .Advance(1);

        matcher.End();
        matcher.Insert(
            [
                // Vessel vessel
                new(OpCodes.Ldarg_1),
                // ProtoPartSnapshot protoPart
                new(OpCodes.Ldarg_2),
                // Now actually call our patch method
                CodeInstruction.Call(() => UpdateLabData(null, null)),
            ]
        );

        return matcher.Instructions();
    }

    public static void UpdateLabData(Vessel vessel, ProtoPartSnapshot protoPart)
    {
        var lab = protoPart.modules.FirstOrDefault(m => m.moduleName == "ModuleScienceLab");
        var processor = vessel.FindVesselModuleImplementing<BackgroundResourceProcessor>();

        if (lab == null)
            return;

        uint moduleId = 0;
        if (!lab.moduleValues.TryGetValue("persistentId", ref moduleId))
            return;

        double dataStored = 0.0;
        if (!lab.moduleValues.TryGetValue("dataStored", ref dataStored))
            return;

        var inventory = processor.GetInventoryById(
            new(protoPart.flightID, "BRPScienceLabData", moduleId)
        );

        if (inventory == null)
            return;

        if (inventory.Amount != dataStored)
        {
            inventory.Amount = dataStored;
            processor.MarkDirty();
        }
    }
}

[HarmonyPatch(typeof(ALH.AutomaticLabHousekeeper))]
[HarmonyPatch("TransferScienceFromUnloadedLab")]
public static class AutomaticLabHouseKeeper_TransferScienceFromUnloadedLab_Patch
{
    static IEnumerable<CodeInstruction> Transpiler(
        IEnumerable<CodeInstruction> instructions,
        ILGenerator generator
    )
    {
        // This transpiler targets the following method:
        // https://github.com/ItWorkedInCAD/AutomaticLabHousekeeper/blob/main/source/AutomaticLabHousekeeper/ALH_Main.cs#L261

        // We replace the call to ResearchAndDevelopment.AddScience with our own
        // shim that also updates the BRP inventory on the vessel.

        var method = typeof(ResearchAndDevelopment).GetMethod(
            nameof(ResearchAndDevelopment.AddScience)
        );

        var matcher = new CodeMatcher(instructions, generator);
        matcher
            .MatchStartForward(new CodeMatch(new CodeInstruction(OpCodes.Callvirt, method)))
            .ThrowIfInvalid("Unable to find call to ResearchAndDevelopment.AddScience");

        matcher.RemoveInstruction();
        matcher.Insert(
            [
                // At this point the top 3 values on the stack are:
                // - A ResearchAndDevelopment object
                // - A float containing the amount of science to transmit to R&D
                // - A TransactionReasons enum value
                //
                // We also need to add the 3 arguments to the function.

                // Vessel vessel
                new(OpCodes.Ldarg_1),
                // ProtoPartSnapshot protoPart
                new(OpCodes.Ldarg_2),
                // ProtoPartModuleSnapshot labModule
                new(OpCodes.Ldarg_3),
                // And now we add our own call instruction
                CodeInstruction.Call(() => AddScienceToRnd(null, 0.0f, default, null, null, null)),
            ]
        );

        return matcher.Instructions();
    }

    public static void AddScienceToRnd(
        ResearchAndDevelopment rnd,
        float science,
        TransactionReasons reason,
        Vessel vessel,
        ProtoPartSnapshot part,
        ProtoPartModuleSnapshot lab
    )
    {
        rnd.AddScience(science, reason);

        uint moduleId = 0;
        if (!lab.moduleValues.TryGetValue("persistentId", ref moduleId))
            return;

        var processor = vessel.FindVesselModuleImplementing<BackgroundResourceProcessor>();
        var inventory = processor.GetInventoryById(new(part.flightID, "BRPScience", moduleId));
        if (inventory == null)
            return;

        inventory.Amount -= science;
        processor.MarkDirty();
    }
}
