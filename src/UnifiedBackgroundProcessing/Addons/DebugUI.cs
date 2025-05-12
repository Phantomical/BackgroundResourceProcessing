using System.IO;
using System.Reflection;
using KSP.UI.Screens;
using UnifiedBackgroundProcessing.VesselModules;
using UnityEngine;

namespace UnifiedBackgroundProcessing
{
    [KSPAddon(KSPAddon.Startup.AllGameScenes, false)]
    internal class BackgroundResourceProcessingDebugGUI : MonoBehaviour
    {
        static ApplicationLauncherButton button;

        bool initialized = false;
        bool showGUI = false;
        bool exportButtonState = false;

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

            initialized = true;
            window = new Rect(Screen.width / 2 - 450 / 2, Screen.height / 2 - 50, 450, 100);

            Texture buttonTexture = GameDatabase.Instance.GetTexture(
                "UnifiedBackgroundProcessing/Textures/ToolbarButton",
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

        public void ShowToolbarGUI()
        {
            showGUI = true;
        }

        public void HideToolbarGUI()
        {
            showGUI = false;
        }

        void Nothing() { }

        void OnGUI()
        {
            if (!showGUI)
                return;

            window = GUILayout.Window(
                GetInstanceID(),
                window,
                DrawWindow,
                "Background Resource Procecssing GUI",
                HighLogic.Skin.window
            );
        }

        void DrawWindow(int windowId)
        {
            GUILayout.BeginVertical();

            var prevButtonState = exportButtonState;
            exportButtonState = GUILayout.Button(
                "Export Ship Resource Graph",
                HighLogic.Skin.button
            );
            if (exportButtonState && !prevButtonState)
                DumpCurrentVessel();

            GUILayout.Label(
                "The resource graph will be exported to GameData/BackgroundResourceProcessing/Exports",
                HighLogic.Skin.label
            );
            GUILayout.EndVertical();

            /// Must be last or buttons won't work.
            GUI.DragWindow();
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

            module.RecordVesselState();

            ConfigNode root = new();
            ConfigNode node = root.AddNode("BRP_SHIP");
            module.Save(node);

            module.ClearVesselState();

            var name = vessel.GetDisplayName();
            var outputPath = Path.GetFullPath(Path.Combine(exportDir, $"{name}.cfg.export"));
            root.Save(outputPath);

            ScreenMessages.PostScreenMessage($"Ship resource graph exported to {outputPath}");
        }
    }
}
