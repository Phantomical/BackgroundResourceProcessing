using System.Collections.Generic;
using System.Linq;
using BackgroundResourceProcessing.Core;
using BackgroundResourceProcessing.UI.Components;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BackgroundResourceProcessing.UI.Screens;

internal class CurrentVesselScreenContent : MonoBehaviour
{
    [SerializeField]
    Transform _resourceContent;

    [SerializeField]
    Transform _converterContent;

    [SerializeField]
    TextMeshProUGUI _vesselNameLabel;

    [SerializeField]
    TextMeshProUGUI _vesselStatusLabel;

    [SerializeField]
    TextMeshProUGUI _sunlightLabel;

    [SerializeField]
    RelativeTimeLabel _lastChangepoint;

    [SerializeField]
    RelativeTimeLabel _nextChangepoint;

    [SerializeField]
    TextMeshProUGUI _sceneGuardLabel;

    [SerializeField]
    GameObject _mainContent;

    [SerializeField]
    TableLayoutGroup _resourceTable;

    internal static RectTransform CreatePrefab()
    {
        var rt = DebugUIManager.CreateScreenPrefab<CurrentVesselScreenContent>(
            "BRP_CurrentVesselScreen"
        );
        var content = rt.GetComponent<CurrentVesselScreenContent>();
        content.BuildUI(rt);
        return rt;
    }

    void BuildUI(Transform parent)
    {
        // Scene guard label (shown when not in flight)
        _sceneGuardLabel = DebugUIManager.CreateLabel(
            parent,
            "Current vessel data is only available in Flight scene."
        );
        _sceneGuardLabel.transform.parent.gameObject.SetActive(false);

        // Main content: single scroll view wrapping everything
        _mainContent = new GameObject("MainScroll", typeof(RectTransform));
        _mainContent.transform.SetParent(parent, false);
        var scrollLE = _mainContent.AddComponent<LayoutElement>();
        scrollLE.flexibleHeight = 1f;
        scrollLE.flexibleWidth = 1f;

        var scrollRect = _mainContent.AddComponent<ScrollRect>();
        scrollRect.horizontal = false;
        scrollRect.vertical = true;
        scrollRect.scrollSensitivity = 20f;
        scrollRect.movementType = ScrollRect.MovementType.Clamped;

        var viewportGo = new GameObject("Viewport", typeof(RectTransform));
        viewportGo.transform.SetParent(_mainContent.transform, false);
        var vp = viewportGo.GetComponent<RectTransform>();
        vp.anchorMin = Vector2.zero;
        vp.anchorMax = Vector2.one;
        vp.pivot = Vector2.zero;
        vp.offsetMin = Vector2.zero;
        vp.offsetMax = Vector2.zero;
        viewportGo.AddComponent<Image>();
        viewportGo.AddComponent<Mask>().showMaskGraphic = false;
        scrollRect.viewport = vp;

        var contentGo = new GameObject("Content", typeof(RectTransform));
        contentGo.transform.SetParent(viewportGo.transform, false);
        var cr = contentGo.GetComponent<RectTransform>();
        cr.anchorMin = new Vector2(0, 1);
        cr.anchorMax = new Vector2(1, 1);
        cr.pivot = new Vector2(0f, 1f);
        cr.offsetMin = Vector2.zero;
        cr.offsetMax = Vector2.zero;

        var vlg = contentGo.AddComponent<VerticalLayoutGroup>();
        vlg.childAlignment = TextAnchor.UpperLeft;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        vlg.spacing = 4f;

        var csf = contentGo.AddComponent<ContentSizeFitter>();
        csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        scrollRect.content = cr;

        DebugUIManager.CreateScrollbar(_mainContent.transform, scrollRect);

        var scrollContent = contentGo.transform;

        // ── Vessel Info ──────────────────────────────────────────────────
        DebugUIManager.CreateHeader(scrollContent, "Vessel Info");
        DebugUIManager.CreateSeparator(scrollContent);

        _vesselNameLabel = DebugUIManager.CreateTableRow(scrollContent, "Vessel", "—");
        _vesselStatusLabel = DebugUIManager.CreateTableRow(scrollContent, "Status", "—");
        _sunlightLabel = DebugUIManager.CreateTableRow(scrollContent, "Sunlight", "—");
        _lastChangepoint = DebugUIManager.CreateTimeRow(
            scrollContent,
            "Last Changepoint",
            double.PositiveInfinity
        );
        _nextChangepoint = DebugUIManager.CreateTimeRow(
            scrollContent,
            "Next Changepoint",
            double.PositiveInfinity
        );

        DebugUIManager.CreateSpacer(scrollContent);

        // ── Resources ────────────────────────────────────────────────────
        DebugUIManager.CreateHeader(scrollContent, "Resources");
        DebugUIManager.CreateSeparator(scrollContent);

        // Resource table container (populated dynamically)
        var resourceGo = new GameObject("ResourceTable", typeof(RectTransform));
        resourceGo.transform.SetParent(scrollContent, false);
        _resourceTable = resourceGo.AddComponent<TableLayoutGroup>();
        _resourceTable.MinimumColumnWidth = 0f;
        _resourceTable.ColumnSpacing = 8f;
        _resourceTable.RowSpacing = 2f;
        _resourceContent = resourceGo.transform;

        DebugUIManager.CreateSpacer(scrollContent);

        // ── Converters ───────────────────────────────────────────────────
        DebugUIManager.CreateHeader(scrollContent, "Converters");
        DebugUIManager.CreateSeparator(scrollContent);

        // Converter list container (populated dynamically)
        var converterGo = new GameObject("ConverterList", typeof(RectTransform));
        converterGo.transform.SetParent(scrollContent, false);
        var cvlg = converterGo.AddComponent<VerticalLayoutGroup>();
        cvlg.childAlignment = TextAnchor.UpperLeft;
        cvlg.childControlWidth = true;
        cvlg.childControlHeight = true;
        cvlg.childForceExpandWidth = true;
        cvlg.childForceExpandHeight = false;
        cvlg.spacing = 4f;
        _converterContent = converterGo.transform;

        DebugUIManager.CreateSpacer(scrollContent);

        // ── Actions ──────────────────────────────────────────────────────
        var actionRow = DebugUIManager.CreateHorizontalLayout(scrollContent);
        DebugUIManager.CreateButton<RefreshButton>(actionRow.transform, "Refresh");
        DebugUIManager.CreateButton<ExportShipButton>(actionRow.transform, "Export Ship");
    }

    float _refreshTimer;

    void OnEnable()
    {
        Populate();
    }

    void Update()
    {
        _refreshTimer += Time.unscaledDeltaTime;
        if (_refreshTimer < 1f)
            return;
        _refreshTimer = 0f;
        Populate();
    }

    void OnDisable()
    {
        ClearDynamic(_resourceContent);
        ClearDynamic(_converterContent);
    }

    void Populate()
    {
        if (!HighLogic.LoadedSceneIsFlight)
        {
            _sceneGuardLabel.transform.parent.gameObject.SetActive(true);
            _mainContent.SetActive(false);
            return;
        }

        _sceneGuardLabel.transform.parent.gameObject.SetActive(false);
        _mainContent.SetActive(true);

        var vessel = FlightGlobals.ActiveVessel;
        if (vessel == null)
        {
            _vesselNameLabel.text = "No active vessel";
            _vesselStatusLabel.text = "—";
            _sunlightLabel.text = "—";
            _lastChangepoint.UT = double.PositiveInfinity;
            _nextChangepoint.UT = double.PositiveInfinity;
            return;
        }

        var processor = vessel.FindVesselModuleImplementing<BackgroundResourceProcessor>();
        if (processor == null)
        {
            _vesselNameLabel.text = vessel.GetDisplayName();
            _vesselStatusLabel.text = "No processor";
            _sunlightLabel.text = "—";
            _lastChangepoint.UT = double.PositiveInfinity;
            _nextChangepoint.UT = double.PositiveInfinity;
            return;
        }

        processor.DebugRecordVesselState();

        _vesselNameLabel.text = vessel.GetDisplayName();
        _vesselStatusLabel.text = vessel.loaded ? "Loaded" : "Unloaded";
        var shadow = processor.ShadowState;
        _sunlightLabel.text = shadow.HasValue
            ? (shadow.Value.InShadow ? "In Shadow" : "In Sunlight")
            : "Unknown";
        _lastChangepoint.UT = processor.LastChangepoint;
        _nextChangepoint.UT = processor.NextChangepoint;

        PopulateResources(processor);
        PopulateConverters(processor);
    }

    void PopulateResources(BackgroundResourceProcessor processor)
    {
        ClearDynamic(_resourceContent);

        var states = processor.GetCurrentResourceStates();
        var sorted = states.OrderBy(kvp => kvp.Key).ToList();

        if (sorted.Count == 0)
        {
            _resourceTable.RowHeights = [20f];
            DebugUIManager.CreateDirectLabel(_resourceContent, "No resources");
            return;
        }

        // +1 for header row
        var heights = new float[sorted.Count + 1];
        for (int i = 0; i < heights.Length; i++)
            heights[i] = 20f;
        _resourceTable.RowHeights = heights;

        var defs = PartResourceLibrary.Instance.resourceDefinitions;

        // Header row
        var headerName = DebugUIManager.CreateDirectLabel(_resourceContent, "Resource");
        headerName.fontStyle = FontStyles.Bold;
        var headerAmount = DebugUIManager.CreateDirectLabel(_resourceContent, "Amount");
        headerAmount.fontStyle = FontStyles.Bold;
        headerAmount.alignment = TextAlignmentOptions.MidlineRight;
        var headerCapacity = DebugUIManager.CreateDirectLabel(_resourceContent, "Capacity");
        headerCapacity.fontStyle = FontStyles.Bold;
        headerCapacity.alignment = TextAlignmentOptions.MidlineRight;
        var headerRate = DebugUIManager.CreateDirectLabel(_resourceContent, "Rate");
        headerRate.fontStyle = FontStyles.Bold;
        headerRate.alignment = TextAlignmentOptions.MidlineRight;

        // Data rows
        foreach (var kvp in sorted)
        {
            var def = defs[kvp.Key];
            var displayName = def != null ? def.displayName : kvp.Key;
            var state = kvp.Value;

            DebugUIManager.CreateDirectLabel(_resourceContent, displayName);
            var amountLabel = DebugUIManager.CreateDirectLabel(
                _resourceContent,
                DebugUIManager.FormatCellNumber(state.amount)
            );
            amountLabel.alignment = TextAlignmentOptions.MidlineRight;
            var capLabel = DebugUIManager.CreateDirectLabel(
                _resourceContent,
                DebugUIManager.FormatCellNumber(state.maxAmount)
            );
            capLabel.alignment = TextAlignmentOptions.MidlineRight;
            var rateLabel = DebugUIManager.CreateDirectLabel(
                _resourceContent,
                DebugUIManager.FormatCellNumber(state.rate)
            );
            rateLabel.alignment = TextAlignmentOptions.MidlineRight;
        }
    }

    void PopulateConverters(BackgroundResourceProcessor processor)
    {
        ClearDynamic(_converterContent);

        var converters = processor.Converters;
        if (converters.Count == 0)
        {
            DebugUIManager.CreateLabel(_converterContent, "No converters");
            return;
        }

        int index = 0;
        foreach (var converter in converters)
        {
            var behaviour = converter.Behaviour;
            var sourcePart = behaviour?.SourcePart ?? "Unknown";
            var sourceModule = behaviour?.SourceModule ?? "Unknown";

            var headerLabel = DebugUIManager.CreateLabel(
                _converterContent,
                $"[{index}] {sourceModule} ({sourcePart})"
            );
            headerLabel.fontStyle = FontStyles.Bold;

            DebugUIManager.CreateLabel(
                _converterContent,
                $"  Rate: {DebugUIManager.FormatCellNumber(converter.Rate)}"
            );

            foreach (var input in converter.Inputs)
                DebugUIManager.CreateLabel(
                    _converterContent,
                    $"  INPUT: {input.Value.ResourceName} x{DebugUIManager.FormatCellNumber(input.Value.Ratio)}"
                );

            foreach (var output in converter.Outputs)
                DebugUIManager.CreateLabel(
                    _converterContent,
                    $"  OUTPUT: {output.Value.ResourceName} x{DebugUIManager.FormatCellNumber(output.Value.Ratio)}"
                );

            DebugUIManager.CreateSpacer(_converterContent, 4f);
            index++;
        }
    }

    static void ClearDynamic(Transform parent)
    {
        if (parent == null)
            return;
        for (int i = parent.childCount - 1; i >= 0; i--)
            Destroy(parent.GetChild(i).gameObject);
    }
}

internal class RefreshButton : DebugScreenButton
{
    protected override void OnClick()
    {
        GetComponentInParent<CurrentVesselScreenContent>()?.SendMessage("Populate");
    }
}

internal class ExportShipButton : DebugScreenButton
{
    protected override void OnClick()
    {
        var vessel = FlightGlobals.ActiveVessel;
        if (vessel == null)
        {
            ScreenMessages.PostScreenMessage("Error: No active vessel.");
            return;
        }

        var processor = vessel.FindVesselModuleImplementing<BackgroundResourceProcessor>();
        if (processor == null)
        {
            ScreenMessages.PostScreenMessage("Error: No BackgroundResourceProcessor on vessel.");
            return;
        }

        var pluginDir = System.IO.Path.GetDirectoryName(
            System.Reflection.Assembly.GetExecutingAssembly().Location
        );
        var exportDir = System.IO.Path.Combine(pluginDir, @"..\Exports");
        System.IO.Directory.CreateDirectory(exportDir);

        ConfigNode root = new();
        ConfigNode node = root.AddNode("BRP_SHIP");
        processor.Save(node);

        var name = vessel.GetDisplayName();
        var outputPath = System.IO.Path.GetFullPath(
            System.IO.Path.Combine(exportDir, $"{name}.cfg.export")
        );
        root.Save(outputPath);

        ScreenMessages.PostScreenMessage($"Ship resource graph exported to {outputPath}");
    }
}
