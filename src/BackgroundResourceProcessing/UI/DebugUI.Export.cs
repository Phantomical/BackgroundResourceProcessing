using System.IO;
using System.Reflection;
using UnityEngine;

namespace BackgroundResourceProcessing.UI
{
    internal partial class DebugUI
    {
        ExportTab exportTab;

        void DrawExportButton()
        {
            var prevState = exportTab.buttonState;
            exportTab.buttonState = GUILayout.Button("Export Ship Resource Graph");
            GUILayout.Label(
                "The resource graph will be exported to GameData/BackgroundResourceProcessing/Exports"
            );

            if (exportTab.buttonState && !prevState)
                ExportTab.DumpCurrentVessel();
        }

        struct ExportTab()
        {
            internal bool buttonState = false;

            internal static void DumpCurrentVessel()
            {
                var pluginDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                var exportDir = Path.Combine(pluginDir, @"..\Exports");

                Directory.CreateDirectory(exportDir);
                var vessel = FlightGlobals.ActiveVessel;
                if (!vessel)
                {
                    ScreenMessages.PostScreenMessage("Error: There is no active vessel to export.");
                    return;
                }

                var module = vessel.GetComponent<BackgroundResourceProcessor>();
                if (!module)
                {
                    var type = typeof(BackgroundResourceProcessor);
                    ScreenMessages.PostScreenMessage(
                        $"Error: The active vessel does not have a {type.Name} module"
                    );
                    return;
                }

                module.DebugRecordVesselState();

                ConfigNode root = new();
                ConfigNode node = root.AddNode("BRP_SHIP");
                module.Save(node);

                module.DebugClearVesselState();

                var name = vessel.GetDisplayName();
                var outputPath = Path.GetFullPath(Path.Combine(exportDir, $"{name}.cfg.export"));
                root.Save(outputPath);

                ScreenMessages.PostScreenMessage($"Ship resource graph exported to {outputPath}");
            }
        }
    }
}
