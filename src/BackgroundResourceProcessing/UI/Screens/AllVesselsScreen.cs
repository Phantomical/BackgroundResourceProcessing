using System.Collections.Generic;
using System.Linq;
using BackgroundResourceProcessing.UI.Components;
using BackgroundResourceProcessing.UI.Popups;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BackgroundResourceProcessing.UI.Screens;

internal class AllVesselsScreenContent : MonoBehaviour
{
    static readonly Color StatusGood = new(0.35f, 1.0f, 0.35f);

    [SerializeField]
    Transform _scrollContent;

    [SerializeField]
    TextMeshProUGUI _sceneGuardLabel;

    [SerializeField]
    GameObject _scrollContainer;

    readonly List<VesselEntry> _entries = [];

    internal static RectTransform CreatePrefab()
    {
        var rt = DebugUIManager.CreateScreenPrefab<AllVesselsScreenContent>("BRP_AllVesselsScreen");
        var content = rt.GetComponent<AllVesselsScreenContent>();
        content.BuildUI(rt);
        return rt;
    }

    void BuildUI(Transform parent)
    {
        // Scene guard label
        _sceneGuardLabel = DebugUIManager.CreateLabel(
            parent,
            "Vessel data not available in this scene."
        );
        _sceneGuardLabel.transform.parent.gameObject.SetActive(false);

        // ScrollRect container
        _scrollContainer = new GameObject("VesselListScroll", typeof(RectTransform));
        _scrollContainer.transform.SetParent(parent, false);
        var scrollLE = _scrollContainer.AddComponent<LayoutElement>();
        scrollLE.flexibleHeight = 1f;
        scrollLE.flexibleWidth = 1f;

        var scrollRect = _scrollContainer.AddComponent<ScrollRect>();
        scrollRect.horizontal = false;
        scrollRect.vertical = true;
        scrollRect.scrollSensitivity = 20f;
        scrollRect.movementType = ScrollRect.MovementType.Clamped;

        var viewportGo = new GameObject("Viewport", typeof(RectTransform));
        viewportGo.transform.SetParent(_scrollContainer.transform, false);
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
        vlg.padding = new RectOffset(4, 4, 4, 4);

        var csf = contentGo.AddComponent<ContentSizeFitter>();
        csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        scrollRect.content = cr;

        _scrollContent = contentGo.transform;

        DebugUIManager.CreateScrollbar(_scrollContainer.transform, scrollRect);
    }

    void OnEnable()
    {
        BackgroundResourceProcessor.onAfterVesselChangepoint.Add(OnChangepoint);
        GameEvents.onVesselDestroy.Add(OnVesselDestroy);
        Populate();
    }

    void OnDisable()
    {
        BackgroundResourceProcessor.onAfterVesselChangepoint.Remove(OnChangepoint);
        GameEvents.onVesselDestroy.Remove(OnVesselDestroy);
        ClearDynamic();
    }

    void OnChangepoint(BackgroundResourceProcessor _)
    {
        foreach (var entry in _entries)
            entry.Refresh();
    }

    void OnVesselDestroy(Vessel vessel)
    {
        for (int i = _entries.Count - 1; i >= 0; i--)
        {
            if (_entries[i].Vessel == vessel)
            {
                _entries[i].Destroy();
                _entries.RemoveAt(i);
            }
        }
    }

    void Populate()
    {
        ClearDynamic();

        if (FlightGlobals.Vessels == null)
        {
            _sceneGuardLabel.transform.parent.gameObject.SetActive(true);
            _scrollContainer.SetActive(false);
            return;
        }

        _sceneGuardLabel.transform.parent.gameObject.SetActive(false);
        _scrollContainer.SetActive(true);

        // Gather vessels with processors that have inventories or converters
        var grouped =
            new SortedDictionary<
                VesselType,
                List<(Vessel vessel, BackgroundResourceProcessor processor)>
            >();

        foreach (var vessel in FlightGlobals.Vessels)
        {
            var processor = vessel.FindVesselModuleImplementing<BackgroundResourceProcessor>();
            if (processor == null)
                continue;

            if (processor.Inventories.Count == 0 && processor.Converters.Count == 0)
                continue;

            if (!grouped.TryGetValue(vessel.vesselType, out var list))
            {
                list = [];
                grouped[vessel.vesselType] = list;
            }

            list.Add((vessel, processor));
        }

        if (grouped.Count == 0)
        {
            DebugUIManager.CreateLabel(_scrollContent, "No tracked vessels.");
            return;
        }

        foreach (var group in grouped)
        {
            DebugUIManager.CreateHeader(_scrollContent, group.Key.ToString());
            DebugUIManager.CreateSeparator(_scrollContent);

            foreach (var entry in group.Value)
            {
                _entries.Add(BuildVesselEntry(entry.vessel, entry.processor));
            }

            DebugUIManager.CreateSpacer(_scrollContent);
        }
    }

    VesselEntry BuildVesselEntry(Vessel vessel, BackgroundResourceProcessor processor)
    {
        // Header row: [View] VesselName   Loaded/Unloaded
        var headerRow = DebugUIManager.CreateHorizontalLayout(_scrollContent);
        headerRow.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = false;

        // View button
        var viewBtn = DebugUIManager.CreateButton(headerRow.transform, "View");
        var viewBtnLE = viewBtn.GetComponent<LayoutElement>();
        if (viewBtnLE == null)
            viewBtnLE = viewBtn.gameObject.AddComponent<LayoutElement>();
        viewBtnLE.preferredWidth = 50f;
        viewBtnLE.flexibleWidth = 0f;
        viewBtnLE.minHeight = 24f;

        var capturedVessel = vessel;
        viewBtn.onClick.AddListener(() => VesselDetailPopup.Create(capturedVessel));

        // Vessel name
        var nameLabel = DebugUIManager.CreateLabel(headerRow.transform, vessel.GetDisplayName());
        nameLabel.fontStyle = FontStyles.Bold;

        // Status
        var statusLabel = DebugUIManager.CreateLabel(
            headerRow.transform,
            vessel.loaded ? "Loaded" : "Unloaded"
        );
        if (vessel.loaded)
            statusLabel.color = StatusGood;
        var statusLE = statusLabel.transform.parent.GetComponent<LayoutElement>();
        if (statusLE != null)
        {
            statusLE.preferredWidth = 80f;
            statusLE.flexibleWidth = 0f;
        }

        // Resource detail container
        var detailGo = new GameObject("VesselDetail", typeof(RectTransform));
        detailGo.transform.SetParent(_scrollContent, false);
        var detailVlg = detailGo.AddComponent<VerticalLayoutGroup>();
        detailVlg.childAlignment = TextAnchor.UpperLeft;
        detailVlg.childControlWidth = true;
        detailVlg.childControlHeight = true;
        detailVlg.childForceExpandWidth = true;
        detailVlg.childForceExpandHeight = false;
        detailVlg.spacing = 2f;

        var entry = new VesselEntry
        {
            Vessel = vessel,
            Processor = processor,
            HeaderRow = headerRow,
            StatusLabel = statusLabel,
            DetailContent = detailGo.transform,
        };
        entry.Refresh();
        return entry;
    }

    void ClearDynamic()
    {
        _entries.Clear();
        if (_scrollContent == null)
            return;
        for (int i = _scrollContent.childCount - 1; i >= 0; i--)
            Destroy(_scrollContent.GetChild(i).gameObject);
    }
}

internal class VesselEntry
{
    internal Vessel Vessel;
    internal BackgroundResourceProcessor Processor;
    internal GameObject HeaderRow;
    internal TextMeshProUGUI StatusLabel;
    internal Transform DetailContent;

    static readonly Color StatusGood = new(0.35f, 1.0f, 0.35f);

    internal void Refresh()
    {
        if (Processor == null || DetailContent == null)
            return;

        // Update status
        if (StatusLabel != null)
        {
            var loaded = Processor.Vessel != null && Processor.Vessel.loaded;
            StatusLabel.text = loaded ? "Loaded" : "Unloaded";
            StatusLabel.color = loaded ? StatusGood : Color.white;
        }

        // Clear and rebuild resource lines
        for (int i = DetailContent.childCount - 1; i >= 0; i--)
            Object.Destroy(DetailContent.GetChild(i).gameObject);

        var states = Processor.GetCurrentResourceStates();
        var defs = PartResourceLibrary.Instance.resourceDefinitions;
        foreach (var kvp in states.OrderBy(kvp => kvp.Key))
        {
            var def = defs[kvp.Key];
            var displayName = def != null ? def.displayName : kvp.Key;
            var state = kvp.Value;
            var rateStr = state.rate != 0.0 ? $" ({DebugUI.FormatCellNumber(state.rate)}/s)" : "";

            DebugUIManager.CreateLabel(
                DetailContent,
                $"    {displayName}: {DebugUI.FormatCellNumber(state.amount)} / {DebugUI.FormatCellNumber(state.maxAmount)}{rateStr}"
            );
        }

        // Converter/changepoint info
        var convCount = Processor.Converters.Count;
        var cpLabel = DebugUIManager.CreateLabel(
            DetailContent,
            $"    {convCount} converter(s), next changepoint: "
        );
        var cpTime = cpLabel.transform.parent.gameObject.AddComponent<RelativeTimeLabel>();
        cpTime.UT = Processor.NextChangepoint;
        cpTime.Prefix = $"    {convCount} converter(s), next changepoint: ";
    }

    internal void Destroy()
    {
        Object.Destroy(HeaderRow);
        Object.Destroy(DetailContent.gameObject);
    }
}
