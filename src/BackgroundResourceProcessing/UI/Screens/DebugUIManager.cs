using KSP.UI;
using KSP.UI.Screens.DebugToolbar;
using KSP.UI.Screens.DebugToolbar.Screens;
using KSP.UI.TooltipTypes;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BackgroundResourceProcessing.UI.Screens;

/// <summary>
/// Finds and caches UI prefab templates from existing KSP debug screens so our custom
/// debug screen uses the same visual theme.
/// </summary>
internal static class DebugUIManager
{
    static GameObject _labelPrefab;
    static GameObject _buttonPrefab;
    static GameObject _togglePrefab;
    static GameObject _scrollbarPrefab;
    static GameObject _spacerPrefab;
    static GameObject _windowPrefab;

    static bool _initialized;

    /// <summary>
    /// Must be called after DebugScreenSpawner is set up (e.g. from MainMenu Start()).
    /// </summary>
    public static bool Initialize()
    {
        if (_initialized)
            return true;

        var spawner = DebugScreenSpawner.Instance;
        if (spawner == null)
        {
            Debug.LogWarning(
                "[BackgroundResourceProcessing] DebugUIManager: DebugScreenSpawner.Instance is null"
            );
            return false;
        }

        var screens = spawner.debugScreens?.screens;
        if (screens == null)
        {
            Debug.LogWarning(
                "[BackgroundResourceProcessing] DebugUIManager: No debug screens found"
            );
            return false;
        }

        foreach (var wrapper in screens)
        {
            if (wrapper.screen == null)
                continue;

            var root = wrapper.screen.gameObject;
            switch (wrapper.name)
            {
                case "Debug":
                    FindConsolePrefabs(root);
                    break;
                case "Database":
                    FindDatabasePrefabs(root);
                    break;
                case "Debugging":
                    FindDebuggingPrefabs(root);
                    break;
            }
        }

        // Scrollbar — from sidebar scroll view in the screen prefab
        if (_scrollbarPrefab == null && spawner.screenPrefab != null)
        {
            var scrollbar = spawner.screenPrefab.transform.Find(
                "VerticalLayout/HorizontalLayout/Contents/Contents Scroll View/Scrollbar"
            );
            if (scrollbar != null)
                _scrollbarPrefab = ClonePrefab(scrollbar.gameObject, "BRP_ScrollbarPrefab");
        }

        // Spacer — created from scratch (no suitable prefab)
        if (_spacerPrefab == null)
        {
            _spacerPrefab = new GameObject("BRP_SpacerPrefab", typeof(RectTransform));
            _spacerPrefab.SetActive(false);
            var le = _spacerPrefab.AddComponent<LayoutElement>();
            le.preferredHeight = 8f;
            Object.DontDestroyOnLoad(_spacerPrefab);
        }

        _initialized = _labelPrefab != null && _buttonPrefab != null && _togglePrefab != null;

        if (!_initialized)
            Debug.LogWarning(
                $"[BackgroundResourceProcessing] DebugUIManager: Failed to find all prefabs. "
                    + $"label={_labelPrefab != null}, button={_buttonPrefab != null}, "
                    + $"toggle={_togglePrefab != null}"
            );

        return _initialized;
    }

    /// <summary>
    /// "Debug" console screen — button in BottomBar.
    /// </summary>
    static void FindConsolePrefabs(GameObject root)
    {
        if (_buttonPrefab != null)
            return;

        var bottomBar = root.transform.Find("BottomBar");
        if (bottomBar == null)
            return;

        var buttonGo = bottomBar.Find("Button");
        if (buttonGo != null)
            _buttonPrefab = ClonePrefab(buttonGo.gameObject, "BRP_ButtonPrefab");
    }

    /// <summary>
    /// "Database" screen — TotalLabel for a label template.
    /// </summary>
    static void FindDatabasePrefabs(GameObject root)
    {
        if (_labelPrefab != null)
            return;

        var totalLabel = root.transform.Find("TotalLabel");
        if (totalLabel != null)
            _labelPrefab = ClonePrefab(totalLabel.gameObject, "BRP_LabelPrefab");
    }

    /// <summary>
    /// "Debugging" screen — PrintErrorsToScreen toggle wrapper.
    /// Strips the KSP-specific DebugScreenToggle component so we can attach our own.
    /// </summary>
    static void FindDebuggingPrefabs(GameObject root)
    {
        if (_togglePrefab != null)
            return;

        var toggleWrapper = root.transform.Find("PrintErrorsToScreen");
        if (toggleWrapper == null)
            return;

        _togglePrefab = ClonePrefab(toggleWrapper.gameObject, "BRP_TogglePrefab");

        var existing = _togglePrefab.GetComponent<DebugScreenToggle>();
        if (existing != null)
            Object.DestroyImmediate(existing);
    }

    /// <summary>
    /// The toggle prefab's inner Toggle child has a fixed width. Stretch it to fill the wrapper.
    /// </summary>
    static void StretchToggleChild(GameObject wrapper)
    {
        var innerToggle = wrapper.GetComponentInChildren<Toggle>(true);
        if (innerToggle == null)
            return;

        var rt = innerToggle.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    static GameObject ClonePrefab(GameObject source, string name)
    {
        var clone = Object.Instantiate(source);
        clone.name = name;
        clone.SetActive(false);
        Object.DontDestroyOnLoad(clone);
        return clone;
    }

    // ── Window prefab (from KSPTextureLoader) ────────────────────────────────

    /// <summary>
    /// Clones the debug screen prefab and strips out debug-specific parts,
    /// producing a reusable window prefab with title bar, close button, and resize handles.
    /// Must be called after Initialize().
    /// </summary>
    public static void BuildWindowPrefab()
    {
        if (_windowPrefab != null)
            return;

        var spawner = DebugScreenSpawner.Instance;
        if (spawner?.screenPrefab == null)
        {
            Debug.LogWarning(
                "[BackgroundResourceProcessing] DebugUIManager: Cannot build window prefab, screenPrefab is null"
            );
            return;
        }

        var go = Object.Instantiate(spawner.screenPrefab.gameObject);
        go.name = "BRP_WindowPrefab";
        go.SetActive(false);
        Object.DontDestroyOnLoad(go);

        // Remove debug-specific components from root
        var debugScreen = go.GetComponent<DebugScreen>();
        if (debugScreen != null)
            Object.DestroyImmediate(debugScreen);

        var addScreens = go.GetComponent<AddDebugScreens>();
        if (addScreens != null)
            Object.DestroyImmediate(addScreens);

        // Remove the root UIWindowArea (whole-window move; title bar has its own)
        var rootWindowArea = go.GetComponent<UIWindowArea>();
        if (rootWindowArea != null)
            Object.DestroyImmediate(rootWindowArea);

        // Remove ToggleContentsButton from title bar
        var titleBar = go.transform.Find("VerticalLayout/TitleBar");
        if (titleBar != null)
        {
            var toggleBtn = titleBar.Find("ToggleContentsButton");
            if (toggleBtn != null)
                Object.DestroyImmediate(toggleBtn.gameObject);

            var titleText = titleBar.Find("TitleText");
            if (titleText != null)
            {
                var tmp = titleText.GetComponent<TextMeshProUGUI>();
                if (tmp != null)
                {
                    tmp.enableWordWrapping = false;
                    tmp.overflowMode = TextOverflowModes.Ellipsis;
                }
            }
        }

        // Destroy the entire HorizontalLayout (sidebar + content area)
        var horizontalLayout = go.transform.Find("VerticalLayout/HorizontalLayout");
        if (horizontalLayout != null)
            Object.DestroyImmediate(horizontalLayout.gameObject);

        var verticalLayout = go.transform.Find("VerticalLayout");
        var vlg = verticalLayout.GetComponent<VerticalLayoutGroup>();
        if (vlg != null)
            vlg.childForceExpandHeight = false;

        // Create a new content area child of VerticalLayout
        var contentGo = new GameObject("Content", typeof(RectTransform));
        contentGo.transform.SetParent(verticalLayout, false);

        var contentRect = contentGo.GetComponent<RectTransform>();
        contentRect.anchorMin = Vector2.zero;
        contentRect.anchorMax = Vector2.one;
        contentRect.offsetMin = Vector2.zero;
        contentRect.offsetMax = Vector2.zero;

        var contentLayout = contentGo.AddComponent<LayoutElement>();
        contentLayout.flexibleHeight = 1f;
        contentLayout.flexibleWidth = 1f;

        var contentVlg = contentGo.AddComponent<VerticalLayoutGroup>();
        contentVlg.childAlignment = TextAnchor.UpperLeft;
        contentVlg.childControlWidth = true;
        contentVlg.childControlHeight = true;
        contentVlg.childForceExpandWidth = true;
        contentVlg.childForceExpandHeight = false;
        contentVlg.padding = new RectOffset(8, 8, 8, 8);
        contentVlg.spacing = 4f;

        _windowPrefab = go;
    }

    /// <summary>
    /// Instantiates a window from the cached prefab, activates it, and returns the components.
    /// </summary>
    public static (GameObject window, Transform contentArea, Button closeButton) InstantiateWindow(
        string title,
        Transform parent,
        Vector2 size
    )
    {
        var result = InstantiateWindowPrefab(title, parent, size);
        if (result.window != null)
            result.window.SetActive(true);
        return result;
    }

    /// <summary>
    /// Instantiates a window from the cached prefab but leaves it inactive.
    /// </summary>
    public static (
        GameObject window,
        Transform contentArea,
        Button closeButton
    ) InstantiateWindowPrefab(string title, Transform parent, Vector2 size)
    {
        if (_windowPrefab == null)
        {
            Debug.LogError(
                "[BackgroundResourceProcessing] DebugUIManager: Window prefab not built"
            );
            return (null, null, null);
        }

        var go = Object.Instantiate(_windowPrefab, parent, false);
        go.name = $"Window_{title}";

        // Set title text
        var titleTextTransform = go.transform.Find("VerticalLayout/TitleBar/TitleText");
        if (titleTextTransform != null)
        {
            var tmp = titleTextTransform.GetComponent<TextMeshProUGUI>();
            if (tmp != null)
                tmp.text = title;
        }

        // Get close button and clear inherited listeners
        var exitButtonTransform = go.transform.Find("VerticalLayout/TitleBar/ExitButton");
        Button closeButton = exitButtonTransform?.GetComponent<Button>();
        if (closeButton != null)
            closeButton.onClick.RemoveAllListeners();

        // Configure UIWindow sizing
        var uiWindow = go.GetComponent<UIWindow>();
        if (uiWindow != null)
        {
            uiWindow.minSize = new Vector2(200f, 150f);
            uiWindow.maxSizeIsScreen = true;
        }

        // Set initial size and center on screen
        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = size;
        rect.anchoredPosition = Vector2.zero;

        // Lock game input while the mouse is over the window
        var inputLock = go.AddComponent<DialogMouseEnterControlLock>();
        inputLock.Setup(
            ControlTypes.ALLBUTCAMERAS,
            $"BackgroundResourceProcessing_{go.GetInstanceID()}"
        );

        var contentArea = go.transform.Find("VerticalLayout/Content");

        return (go, contentArea, closeButton);
    }

    // ── Factory methods ──────────────────────────────────────────────────────

    public static RectTransform CreateScreenPrefab<T>(string name)
        where T : MonoBehaviour
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.SetActive(false);
        Object.DontDestroyOnLoad(go);
        go.AddComponent<T>();

        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        var vlg = go.AddComponent<VerticalLayoutGroup>();
        vlg.childAlignment = TextAnchor.UpperLeft;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        vlg.spacing = 4f;
        vlg.padding = new RectOffset(8, 8, 8, 8);

        return rt;
    }

    /// <summary>
    /// Creates a label with TMP directly on the GO (no wrapper), so the layout group
    /// sees TMP's own ILayoutElement and auto-sizes to text content.
    /// </summary>
    public static TextMeshProUGUI CreateDirectLabel(Transform parent, string text)
    {
        var sourceTmp = _labelPrefab.GetComponentInChildren<TextMeshProUGUI>();
        var go = new GameObject("Label", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.font = sourceTmp.font;
        tmp.fontSize = sourceTmp.fontSize;
        tmp.color = sourceTmp.color;
        tmp.overflowMode = sourceTmp.overflowMode;
        tmp.text = text;
        return tmp;
    }

    public static TextMeshProUGUI CreateLabel(Transform parent, string text)
    {
        var go = Object.Instantiate(_labelPrefab, parent, false);
        go.SetActive(true);
        go.name = "Label";

        var layout = go.GetComponent<LayoutElement>();
        if (layout != null)
        {
            layout.preferredWidth = -1;
            layout.flexibleWidth = 1;
            layout.minHeight = 20f;
        }

        var tmp = go.GetComponentInChildren<TextMeshProUGUI>();
        var textRect = tmp.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;

        tmp.text = text;
        return tmp;
    }

    public static TextMeshProUGUI CreateHeader(Transform parent, string text)
    {
        var tmp = CreateLabel(parent, text);
        tmp.fontStyle = FontStyles.Bold;
        tmp.fontSize *= 1.2f;
        return tmp;
    }

    public static T CreateToggle<T>(Transform parent, string label)
        where T : DebugScreenToggle
    {
        var go = Object.Instantiate(_togglePrefab, parent, false);
        go.name = "Toggle";

        var layout = go.GetComponent<LayoutElement>();
        if (layout != null)
        {
            layout.preferredWidth = -1;
            layout.flexibleWidth = 1;
        }

        StretchToggleChild(go);

        var component = go.AddComponent<T>();
        component.toggle = go.GetComponentInChildren<Toggle>();
        var labelTransform = component.toggle?.transform.Find("Label");
        if (labelTransform != null)
            component.toggleText = labelTransform.GetComponent<TextMeshProUGUI>();
        component.text = label;

        go.SetActive(true);
        return component;
    }

    public static GameObject CreateHelpButton(Transform parent, string tooltip)
    {
        var go = Object.Instantiate(_buttonPrefab, parent, false);
        go.name = "HelpButton";
        go.SetActive(true);

        var tmp = go.GetComponentInChildren<TextMeshProUGUI>();
        if (tmp != null)
            tmp.text = "?";

        var layout = go.GetComponent<LayoutElement>();
        if (layout == null)
            layout = go.AddComponent<LayoutElement>();
        layout.preferredWidth = 24f;
        layout.preferredHeight = 24f;
        layout.minHeight = -1f;
        layout.flexibleWidth = 0f;

        if (!string.IsNullOrEmpty(tooltip))
        {
            var tooltipPrefab = UISkinManager
                .GetPrefab("UISliderPrefab")
                .GetComponent<TooltipController_Text>()
                .prefab;
            var controller = go.AddComponent<TooltipController_Text>();
            controller.prefab = tooltipPrefab;
            controller.textString = tooltip;
        }

        return go;
    }

    public static T CreateButton<T>(Transform parent, string text)
        where T : DebugScreenButton
    {
        var go = Object.Instantiate(_buttonPrefab, parent, false);
        go.name = "Button";

        SetupButtonLayout(go);

        var tmp = go.GetComponentInChildren<TextMeshProUGUI>();
        if (tmp != null)
            tmp.text = text;

        var component = go.AddComponent<T>();
        component.button = go.GetComponent<Button>();

        go.SetActive(true);
        return component;
    }

    public static Button CreateButton(Transform parent, string text)
    {
        var go = Object.Instantiate(_buttonPrefab, parent, false);
        go.name = "Button";

        SetupButtonLayout(go);

        var tmp = go.GetComponentInChildren<TextMeshProUGUI>();
        if (tmp != null)
            tmp.text = text;

        var btn = go.GetComponent<Button>();
        btn.onClick.RemoveAllListeners();

        go.SetActive(true);
        return btn;
    }

    static void SetupButtonLayout(GameObject go)
    {
        var layout = go.GetComponent<LayoutElement>();
        if (layout != null)
        {
            layout.preferredWidth = -1;
            layout.flexibleWidth = 1;
            layout.minHeight = 30f;
        }
    }

    public static void CreateSpacer(Transform parent, float height = 8f)
    {
        var go = Object.Instantiate(_spacerPrefab, parent, false);
        go.SetActive(true);
        go.name = "Spacer";
        var layout = go.GetComponent<LayoutElement>();
        layout.preferredHeight = height;
        layout.minHeight = height;
    }

    public static void CreateSeparator(Transform parent)
    {
        var go = new GameObject("Separator", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var img = go.AddComponent<Image>();
        img.color = new Color(0.4f, 0.4f, 0.4f);
        var le = go.AddComponent<LayoutElement>();
        le.preferredHeight = 1f;
        le.minHeight = 1f;
        le.flexibleWidth = 1f;
    }

    public static GameObject CreateHorizontalLayout(Transform parent, float spacing = 8f)
    {
        var go = new GameObject("HorizontalLayout", typeof(RectTransform));
        go.transform.SetParent(parent, false);

        var hlg = go.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing = spacing;
        hlg.childAlignment = TextAnchor.MiddleLeft;
        hlg.childControlWidth = true;
        hlg.childControlHeight = true;
        hlg.childForceExpandWidth = true;
        hlg.childForceExpandHeight = false;

        var layout = go.AddComponent<LayoutElement>();
        layout.minHeight = 30f;

        return go;
    }

    /// <summary>
    /// Creates a table row with a label on the left and a value label on the right.
    /// Returns the value TextMeshProUGUI so it can be updated at runtime.
    /// </summary>
    public static TextMeshProUGUI CreateTableRow(
        Transform parent,
        string name,
        string initialValue = ""
    )
    {
        var row = new GameObject("Row", typeof(RectTransform));
        row.transform.SetParent(parent, false);

        var hlg = row.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing = 8f;
        hlg.childAlignment = TextAnchor.MiddleLeft;
        hlg.childControlWidth = true;
        hlg.childControlHeight = true;
        hlg.childForceExpandWidth = true;
        hlg.childForceExpandHeight = false;

        var rowLayout = row.AddComponent<LayoutElement>();
        rowLayout.minHeight = 20f;

        // Name label (left half)
        var nameTmp = CreateLabel(row.transform, name);
        nameTmp.fontStyle = FontStyles.Normal;

        // Value label (right half)
        var valueTmp = CreateLabel(row.transform, initialValue);
        valueTmp.fontStyle = FontStyles.Normal;
        valueTmp.alignment = TextAlignmentOptions.MidlineRight;

        return valueTmp;
    }

    public static Scrollbar CreateScrollbar(Transform parent, ScrollRect scrollRect)
    {
        if (_scrollbarPrefab == null)
            return null;

        var go = Object.Instantiate(_scrollbarPrefab, parent, false);
        go.SetActive(true);
        go.name = "Scrollbar";

        var scrollbar = go.GetComponent<Scrollbar>();
        scrollbar.direction = Scrollbar.Direction.BottomToTop;

        scrollRect.verticalScrollbar = scrollbar;
        scrollRect.verticalScrollbarVisibility = ScrollRect
            .ScrollbarVisibility
            .AutoHideAndExpandViewport;
        scrollRect.verticalScrollbarSpacing = 0f;

        return scrollbar;
    }
}
