using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BackgroundResourceProcessing.Core;
using UnityEngine;

namespace BackgroundResourceProcessing.UI
{
    internal partial class DebugUI
    {
        private void DrawResourceState()
        {
            List<KeyValuePair<string, InventoryState>> totals = [];
            if (processor != null)
                totals = [.. processor.GetResourceTotals()];

            totals.Sort((a, b) => a.Key.CompareTo(b.Key));
            var defs = PartResourceLibrary.Instance.resourceDefinitions;

            GUILayout.BeginHorizontal();
            GUILayout.BeginVertical();
            GUILayout.Label("Resource", HeaderStyle, GUILayout.ExpandWidth(true));
            foreach (var resource in totals.Select(entry => entry.Key))
            {
                var def = defs[resource];
                var name = def != null ? def.displayName : resource;
                GUILayout.Label(name, GUILayout.ExpandWidth(true));
            }
            GUILayout.EndVertical();

            var values = totals.Select(entry => entry.Value);
            DrawColumn("Amount", values.Select(state => state.amount));
            DrawColumn("Capacity", values.Select(state => state.maxAmount));
            DrawColumn("Rate", values.Select(state => state.rate));

            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Refresh"))
                processor = GetMainVesselProcessor();
            if (GUILayout.Button("Export Ship"))
                DumpCurrentVessel();
            GUILayout.EndHorizontal();
        }

        void DrawColumn(string label, IEnumerable<double> values)
        {
            using var group = new PushVerticalGroup();
            var style = new GUIStyle(HeaderStyle) { alignment = TextAnchor.MiddleRight };

            GUILayout.Label(label, style, GUILayout.ExpandWidth(true));
            foreach (var value in values)
                GUILayout.Label(FormatCellNumber(value), CellStyle, GUILayout.ExpandWidth(true));
        }

        static string FormatCellNumber(double n)
        {
            if (Math.Abs(n) < 0.1)
                return $"{n:g3}";
            return $"{n:N}";
        }

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
