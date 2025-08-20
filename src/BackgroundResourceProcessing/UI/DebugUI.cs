using System;
using BackgroundResourceProcessing.Core;
using KSP.UI.Screens;
using UnityEngine;

namespace BackgroundResourceProcessing.UI;

[KSPAddon(KSPAddon.Startup.Flight, false)]
internal partial class DebugUI : MonoBehaviour
{
    internal interface IGUIWindowProvider
    {
        Rect GUILayoutWindow(
            int id,
            Rect screenRect,
            GUI.WindowFunction func,
            string text,
            GUIStyle style
        );
    }

    internal static IGUIWindowProvider WindowProvider = new KSPWindowProvider();

    const int DefaultWidth = 600;
    const int DefaultHeight = 100;
    static int CloseButtonSize = 15;
    static int CloseButtonMargin = 5;

    enum Submenu
    {
        Resources,
        Converter,
    }

    static bool InitializedStatics = false;
    static GUIStyle HeaderStyle;
    static GUIStyle CellStyle;
    static GUIStyle ButtonActive;
    static Texture ButtonTexture;

    static ApplicationLauncherButton button;

    bool showGUI = false;

    Submenu submenu = Submenu.Resources;
    BackgroundResourceProcessor processor;

    Rect window = new(100, 100, DefaultWidth, DefaultWidth);
    bool resetHeight = false;

    void Awake()
    {
        if (HighLogic.LoadedScene != GameScenes.FLIGHT)
            Destroy(this);
    }

    void Start()
    {
        InitStatics();
        window = new Rect(
            Screen.width / 2 - DefaultWidth / 2,
            Screen.height / 2 - DefaultHeight / 2,
            DefaultWidth,
            DefaultHeight
        );

        GameEvents.OnGameSettingsApplied.Add(OnGameSettingsApplied);

        var settings = HighLogic.CurrentGame.Parameters.CustomParams<DebugSettings>();
        if (settings.DebugUI)
            CreateApplication();
    }

    void OnDestroy()
    {
        GameEvents.OnGameSettingsApplied.Remove(OnGameSettingsApplied);

        if (button)
            ApplicationLauncher.Instance.RemoveModApplication(button);
    }

    void InitStatics()
    {
        if (InitializedStatics)
            return;

        HeaderStyle = new(HighLogic.Skin.label) { fontStyle = FontStyle.Bold };
        CellStyle = new(HighLogic.Skin.label) { alignment = TextAnchor.MiddleRight };
        ButtonActive = new(HighLogic.Skin.button) { onNormal = HighLogic.Skin.button.onActive };
        ButtonTexture = GameDatabase.Instance.GetTexture(
            "BackgroundResourceProcessing/Textures/ToolbarButton",
            false
        );

        InitializedStatics = true;
    }

    void CreateApplication()
    {
        if (button != null)
            return;

        button = ApplicationLauncher.Instance.AddModApplication(
            ShowToolbarGUI,
            HideToolbarGUI,
            Nothing,
            Nothing,
            Nothing,
            Nothing,
            ApplicationLauncher.AppScenes.FLIGHT | ApplicationLauncher.AppScenes.MAPVIEW,
            ButtonTexture
        );
    }

    void OnGameSettingsApplied()
    {
        if (button != null)
            return;

        var settings = HighLogic.CurrentGame.Parameters.CustomParams<DebugSettings>();
        if (!settings.DebugUI)
            return;

        CreateApplication();
    }

    void ShowToolbarGUI()
    {
        showGUI = true;
        processor = GetMainVesselProcessor();

        converterTab = new(this);
    }

    void HideToolbarGUI()
    {
        showGUI = false;
        processor = null;

        converterTab = null;
    }

    void Nothing() { }

    void OnGUI()
    {
        if (!showGUI)
            return;

        window = WindowProvider.GUILayoutWindow(
            GetInstanceID(),
            window,
            DrawWindow,
            "Background Resource Processing",
            HighLogic.Skin.window
        );

        if (resetHeight)
            window.height = DefaultHeight;
        resetHeight = false;
    }

    void DrawWindow(int windowId)
    {
        using var skin = new PushGUISkin(HighLogic.Skin);

        var closeButtonRect = new Rect(
            window.width - CloseButtonSize - CloseButtonMargin,
            CloseButtonMargin,
            CloseButtonSize,
            CloseButtonSize
        );
        if (GUI.Button(closeButtonRect, "X"))
            HideToolbarGUI();

        GUILayout.BeginVertical();

        DrawSubmenuSelector();

        switch (submenu)
        {
            case Submenu.Resources:
                DrawResourceState();
                break;
            case Submenu.Converter:
                DrawConverterTab();
                break;
        }

        if (submenu != Submenu.Converter)
            converterTab.CancelPartSelection();

        GUILayout.EndVertical();

        // Must be last or buttons won't work.
        GUI.DragWindow();
    }

    void DrawSubmenuSelector()
    {
        var prev = submenu;
        submenu = (Submenu)
            GUILayout.Toolbar(
                (int)submenu,
                ["Resources", "Debug Inspector"],
                GUILayout.ExpandWidth(true)
            );

        if (prev != submenu)
            resetHeight = true;
    }

    BackgroundResourceProcessor GetMainVesselProcessor()
    {
        var vessel = FlightGlobals.ActiveVessel;
        if (vessel == null)
            return null;

        if (!vessel.loaded)
            return null;

        var processor = vessel.FindVesselModuleImplementing<BackgroundResourceProcessor>();
        processor?.DebugRecordVesselState();
        return processor;
    }

    readonly struct PushGUISkin : IDisposable
    {
        readonly GUISkin prev;

        public PushGUISkin(GUISkin skin)
        {
            prev = GUI.skin;
            GUI.skin = skin;
        }

        public readonly void Dispose()
        {
            GUI.skin = prev;
        }
    }

    readonly ref struct PushVerticalGroup : IDisposable
    {
        public PushVerticalGroup()
        {
            GUILayout.BeginVertical();
        }

        public readonly void Dispose()
        {
            GUILayout.EndVertical();
        }
    }

    readonly ref struct PushHorizontalGroup : IDisposable
    {
        public PushHorizontalGroup()
        {
            GUILayout.BeginHorizontal();
        }

        public readonly void Dispose()
        {
            GUILayout.EndHorizontal();
        }
    }

    readonly ref struct PushEnabeled : IDisposable
    {
        readonly bool prev;

        public PushEnabeled(bool enabled)
        {
            prev = GUI.enabled;
            GUI.enabled = enabled;
        }

        public void Dispose()
        {
            GUI.enabled = prev;
        }
    }

    readonly ref struct PushDepth : IDisposable
    {
        readonly int depth;

        public PushDepth(int depth)
        {
            this.depth = GUI.depth;
            GUI.depth = depth;
        }

        public void Dispose()
        {
            GUI.depth = depth;
        }
    }

    class KSPWindowProvider : IGUIWindowProvider
    {
        public Rect GUILayoutWindow(
            int id,
            Rect screenRect,
            GUI.WindowFunction func,
            string text,
            GUIStyle style
        )
        {
            return GUILayout.Window(id, screenRect, func, text, style);
        }
    }
}
