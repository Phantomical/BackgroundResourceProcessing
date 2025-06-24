using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BackgroundResourceProcessing.Collections;
using BackgroundResourceProcessing.Core;
using KSP.UI.Screens;
using UnityEngine;

namespace BackgroundResourceProcessing.UI
{
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    internal class DebugUI : MonoBehaviour
    {
        enum Submenu
        {
            Resources,
            Export,
        }

        static GUIStyle HeaderStyle;
        static GUIStyle CellStyle;
        static GUIStyle ButtonActive;

        static ApplicationLauncherButton button;

        bool initialized = false;
        bool showGUI = false;
        bool exportButtonState = false;
        bool refreshButtonState = false;

        Submenu submenu = Submenu.Resources;

        ResourceProcessor processor;

        Rect window = new(100, 100, 450, 600);

        void Awake()
        {
            if (HighLogic.LoadedScene != GameScenes.FLIGHT)
                Destroy(this);
        }

        void Start()
        {
            if (initialized)
                return;

            HeaderStyle = new(HighLogic.Skin.label) { fontStyle = FontStyle.Bold };
            CellStyle = new(HighLogic.Skin.label) { alignment = TextAnchor.MiddleRight };
            ButtonActive = new(HighLogic.Skin.button) { onNormal = HighLogic.Skin.button.onActive };

            initialized = true;
            window = new Rect(Screen.width / 2 - 450 / 2, Screen.height / 2 - 50, 450, 100);

            Texture buttonTexture = GameDatabase.Instance.GetTexture(
                "BackgroundResourceProcessing/Textures/ToolbarButton",
                false
            );
            button = ApplicationLauncher.Instance.AddModApplication(
                ShowToolbarGUI,
                HideToolbarGUI,
                Nothing,
                Nothing,
                Nothing,
                Nothing,
                ApplicationLauncher.AppScenes.FLIGHT | ApplicationLauncher.AppScenes.MAPVIEW,
                buttonTexture
            );
        }

        void OnDestroy()
        {
            if (button)
                ApplicationLauncher.Instance.RemoveModApplication(button);
        }

        void ShowToolbarGUI()
        {
            showGUI = true;
            processor = GetMainVesselProcessor();
        }

        void HideToolbarGUI()
        {
            showGUI = false;
            processor = null;
        }

        void Nothing() { }

        void OnGUI()
        {
            if (!showGUI)
                return;

            window = GUILayoutWindow(
                GetInstanceID(),
                window,
                DrawWindow,
                "Background Resource Processing",
                HighLogic.Skin.window
            );
        }

        // This is a patch point for the ClickThroughBlocker integration to
        // override GUILayout.Window with the equivalent from CTB.
        Rect GUILayoutWindow(
            int id,
            Rect screenRect,
            GUI.WindowFunction func,
            string text,
            GUIStyle style
        )
        {
            return GUILayout.Window(id, screenRect, func, text, style);
        }

        void DrawWindow(int windowId)
        {
            using var skin = new PushGUISkin(HighLogic.Skin);
            GUILayout.BeginVertical();

            DrawSubmenuSelector();

            switch (submenu)
            {
                case Submenu.Resources:
                    DrawResourceState();
                    break;
                case Submenu.Export:
                    DrawExportButton();
                    break;
            }

            GUILayout.EndVertical();

            // Must be last or buttons won't work.
            GUI.DragWindow();
        }

        void DrawSubmenuSelector()
        {
            GUILayout.BeginHorizontal();

            var resources = GUILayout.Button("Resources");
            var export = GUILayout.Button("Ship Export");

            if (resources)
                submenu = Submenu.Resources;
            else if (export)
                submenu = Submenu.Export;

            GUILayout.EndHorizontal();
        }

        void DrawResourceState()
        {
            List<KeyValuePair<string, InventoryState>> totals = [];
            if (processor != null)
                totals = processor.GetResourceTotals().ToList();

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

            var prevState = refreshButtonState;
            refreshButtonState = GUILayout.Button("Refresh");
            if (refreshButtonState && !prevState)
                processor = GetMainVesselProcessor();
        }

        void DrawExportButton()
        {
            var prevButtonState = exportButtonState;
            exportButtonState = GUILayout.Button("Export Ship Resource Graph");
            if (exportButtonState && !prevButtonState)
                DumpCurrentVessel();

            GUILayout.Label(
                "The resource graph will be exported to GameData/BackgroundResourceProcessing/Exports"
            );
        }

        void DrawColumn(string label, IEnumerable<double> values)
        {
            using var group = new PushVerticalGroup();
            var style = new GUIStyle(HeaderStyle) { alignment = TextAnchor.MiddleRight };

            GUILayout.Label(label, style, GUILayout.ExpandWidth(true));
            foreach (var value in values)
                GUILayout.Label(FormatCellNumber(value), CellStyle, GUILayout.ExpandWidth(true));
        }

        ResourceProcessor GetMainVesselProcessor()
        {
            var vessel = FlightGlobals.ActiveVessel;
            if (vessel == null)
                return null;

            var now = Planetarium.GetUniversalTime();

            ResourceProcessor processor = new();
            processor.RecordVesselState(vessel, now);
            processor.ComputeNextChangepoint(now);
            processor.ComputeRates();
            return processor;
        }

        static void DumpCurrentVessel()
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

        static string FormatCellNumber(double n)
        {
            if (Math.Abs(n) < 0.1)
                return $"{n:g3}";
            return $"{n:N}";
        }

        struct PushGUISkin : IDisposable
        {
            GUISkin prev;

            public PushGUISkin(GUISkin skin)
            {
                prev = GUI.skin;
                GUI.skin = skin;
            }

            public void Dispose()
            {
                GUI.skin = prev;
            }
        }

        struct PushVerticalGroup : IDisposable
        {
            public PushVerticalGroup()
            {
                GUILayout.BeginVertical();
            }

            public void Dispose()
            {
                GUILayout.EndVertical();
            }
        }

        struct PushHorizontalGroup : IDisposable
        {
            public PushHorizontalGroup()
            {
                GUILayout.BeginHorizontal();
            }

            public void Dispose()
            {
                GUILayout.EndHorizontal();
            }
        }
    }
}
