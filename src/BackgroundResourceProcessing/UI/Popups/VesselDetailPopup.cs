using System.Collections.Generic;
using System.Linq;
using BackgroundResourceProcessing.Core;
using BackgroundResourceProcessing.UI.Components;
using BackgroundResourceProcessing.UI.Screens;
using KSP.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BackgroundResourceProcessing.UI.Popups;

internal static class VesselDetailPopup
{
    /// <summary>
    /// Create and show a popup window with detailed vessel state.
    /// </summary>
    internal static GameObject Create(Vessel vessel)
    {
        var vesselName = vessel.GetDisplayName();
        var (window, contentArea, closeButton) = DebugUIManager.InstantiateWindow(
            vesselName,
            MainCanvasUtil.MainCanvas.transform,
            new Vector2(550, 500)
        );

        if (window == null)
            return null;

        closeButton?.onClick.AddListener(() => Object.Destroy(window));

        var processor = vessel.FindVesselModuleImplementing<BackgroundResourceProcessor>();
        if (processor == null)
        {
            DebugUIManager.CreateLabel(contentArea, "No BackgroundResourceProcessor on vessel.");
            return window;
        }

        // Single scroll rect for all content
        var scrollContent = BuildScrollContainer(contentArea);

        var updater = window.AddComponent<ChangepointUpdater>();
        updater.Processor = processor;

        BuildVesselInfo(scrollContent, vessel, processor, updater);
        var (resourceContent, resourceTable) = BuildResourcesSection(scrollContent, processor);
        updater.ResourceContent = resourceContent;
        updater.ResourceTable = resourceTable;
        updater.ConverterContent = BuildConvertersSection(scrollContent, processor);

        return window;
    }

    static Transform BuildScrollContainer(Transform parent)
    {
        var scrollGo = new GameObject("ContentScroll", typeof(RectTransform));
        scrollGo.transform.SetParent(parent, false);
        var scrollLE = scrollGo.AddComponent<LayoutElement>();
        scrollLE.flexibleHeight = 1f;
        scrollLE.flexibleWidth = 1f;

        var scrollRect = scrollGo.AddComponent<ScrollRect>();
        scrollRect.horizontal = false;
        scrollRect.vertical = true;
        scrollRect.scrollSensitivity = 20f;
        scrollRect.movementType = ScrollRect.MovementType.Clamped;

        var viewportGo = new GameObject("Viewport", typeof(RectTransform));
        viewportGo.transform.SetParent(scrollGo.transform, false);
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

        DebugUIManager.CreateScrollbar(scrollGo.transform, scrollRect);

        return contentGo.transform;
    }

    static void BuildVesselInfo(
        Transform parent,
        Vessel vessel,
        BackgroundResourceProcessor processor,
        ChangepointUpdater updater
    )
    {
        DebugUIManager.CreateHeader(parent, "Vessel Info");
        DebugUIManager.CreateSeparator(parent);

        DebugUIManager.CreateTableRow(parent, "Name", vessel.GetDisplayName());
        DebugUIManager.CreateTableRow(parent, "Type", vessel.vesselType.ToString());
        DebugUIManager.CreateTableRow(parent, "Status", vessel.loaded ? "Loaded" : "Unloaded");
        DebugUIManager.CreateTableRow(parent, "Situation", vessel.SituationString);
        var shadow = processor.ShadowState;
        updater.SunlightLabel = DebugUIManager.CreateTableRow(
            parent,
            "Sunlight",
            shadow.HasValue ? (shadow.Value.InShadow ? "In Shadow" : "In Sunlight") : "Unknown"
        );
        var lastCp = DebugUIManager.CreateTimeRow(
            parent,
            "Last Changepoint",
            processor.LastChangepoint
        );
        var nextCp = DebugUIManager.CreateTimeRow(
            parent,
            "Next Changepoint",
            processor.NextChangepoint
        );
        updater.LastChangepoint = lastCp;
        updater.NextChangepoint = nextCp;

        var actionRow = DebugUIManager.CreateHorizontalLayout(parent);
        var dumpBtn = DebugUIManager.CreateButton(actionRow.transform, "Dump Vessel");
        dumpBtn.onClick.AddListener(() => DumpVessel(vessel, processor));

        DebugUIManager.CreateSpacer(parent);
    }

    static void DumpVessel(Vessel vessel, BackgroundResourceProcessor processor)
    {
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

    static (Transform content, TableLayoutGroup table) BuildResourcesSection(
        Transform parent,
        BackgroundResourceProcessor processor
    )
    {
        DebugUIManager.CreateHeader(parent, "Resources");
        DebugUIManager.CreateSeparator(parent);

        var tableGo = new GameObject("ResourceTable", typeof(RectTransform));
        tableGo.transform.SetParent(parent, false);

        var table = tableGo.AddComponent<TableLayoutGroup>();
        table.MinimumColumnWidth = 0f;
        table.ColumnSpacing = 8f;
        table.RowSpacing = 2f;

        PopulateResourceTable(tableGo.transform, table, processor);

        DebugUIManager.CreateSpacer(parent);

        return (tableGo.transform, table);
    }

    internal static void PopulateResourceTable(
        Transform content,
        TableLayoutGroup table,
        BackgroundResourceProcessor processor
    )
    {
        var states = processor.GetCurrentResourceStates();
        var sorted = states.OrderBy(kvp => kvp.Key).ToList();

        if (sorted.Count == 0)
        {
            table.RowHeights = [20f];
            DebugUIManager.CreateDirectLabel(content, "No resources");
            return;
        }

        // +1 for header
        var heights = new float[sorted.Count + 1];
        for (int i = 0; i < heights.Length; i++)
            heights[i] = 20f;
        table.RowHeights = heights;

        var defs = PartResourceLibrary.Instance.resourceDefinitions;

        // Header row
        var h1 = DebugUIManager.CreateDirectLabel(content, "Resource");
        h1.fontStyle = FontStyles.Bold;
        var h2 = DebugUIManager.CreateDirectLabel(content, "Amount");
        h2.fontStyle = FontStyles.Bold;
        h2.alignment = TextAlignmentOptions.MidlineRight;
        var h3 = DebugUIManager.CreateDirectLabel(content, "Capacity");
        h3.fontStyle = FontStyles.Bold;
        h3.alignment = TextAlignmentOptions.MidlineRight;
        var h4 = DebugUIManager.CreateDirectLabel(content, "Rate");
        h4.fontStyle = FontStyles.Bold;
        h4.alignment = TextAlignmentOptions.MidlineRight;

        // Data rows
        foreach (var kvp in sorted)
        {
            var def = defs[kvp.Key];
            var displayName = def != null ? def.displayName : kvp.Key;
            var state = kvp.Value;

            DebugUIManager.CreateDirectLabel(content, displayName);
            var a = DebugUIManager.CreateDirectLabel(
                content,
                DebugUIManager.FormatCellNumber(state.amount)
            );
            a.alignment = TextAlignmentOptions.MidlineRight;
            var c = DebugUIManager.CreateDirectLabel(
                content,
                DebugUIManager.FormatCellNumber(state.maxAmount)
            );
            c.alignment = TextAlignmentOptions.MidlineRight;
            var r = DebugUIManager.CreateDirectLabel(
                content,
                DebugUIManager.FormatCellNumber(state.rate)
            );
            r.alignment = TextAlignmentOptions.MidlineRight;
        }
    }

    static Transform BuildConvertersSection(Transform parent, BackgroundResourceProcessor processor)
    {
        DebugUIManager.CreateHeader(parent, "Converters");
        DebugUIManager.CreateSeparator(parent);

        var converterGo = new GameObject("ConverterList", typeof(RectTransform));
        converterGo.transform.SetParent(parent, false);
        var vlg = converterGo.AddComponent<VerticalLayoutGroup>();
        vlg.childAlignment = TextAnchor.UpperLeft;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        vlg.spacing = 4f;

        PopulateConverterList(converterGo.transform, processor);

        return converterGo.transform;
    }

    internal static void PopulateConverterList(
        Transform content,
        BackgroundResourceProcessor processor
    )
    {
        var converters = processor.Converters;
        if (converters.Count == 0)
        {
            DebugUIManager.CreateLabel(content, "No converters");
            return;
        }

        int index = 0;
        foreach (var converter in converters)
        {
            var behaviour = converter.Behaviour;
            var sourcePart = behaviour?.SourcePart ?? "Unknown";
            var sourceModule = behaviour?.SourceModule ?? "Unknown";

            var headerLabel = DebugUIManager.CreateLabel(
                content,
                $"[{index}] {sourceModule} ({sourcePart})"
            );
            headerLabel.fontStyle = FontStyles.Bold;

            DebugUIManager.CreateLabel(
                content,
                $"  Rate: {DebugUIManager.FormatCellNumber(converter.Rate)}"
            );

            foreach (var input in converter.Inputs)
                DebugUIManager.CreateLabel(
                    content,
                    $"  INPUT: {input.Value.ResourceName} x{DebugUIManager.FormatCellNumber(input.Value.Ratio)}"
                );

            foreach (var output in converter.Outputs)
                DebugUIManager.CreateLabel(
                    content,
                    $"  OUTPUT: {output.Value.ResourceName} x{DebugUIManager.FormatCellNumber(output.Value.Ratio)}"
                );

            DebugUIManager.CreateSpacer(content, 4f);
            index++;
        }
    }
}

internal class ChangepointUpdater : MonoBehaviour
{
    internal BackgroundResourceProcessor Processor;
    internal TextMeshProUGUI SunlightLabel;
    internal RelativeTimeLabel LastChangepoint;
    internal RelativeTimeLabel NextChangepoint;
    internal Transform ResourceContent;
    internal TableLayoutGroup ResourceTable;
    internal Transform ConverterContent;

    float _timer;

    void Update()
    {
        _timer += Time.unscaledDeltaTime;
        if (_timer < 1f)
            return;
        _timer = 0f;

        if (Processor == null)
            return;

        if (SunlightLabel != null)
        {
            var shadow = Processor.ShadowState;
            SunlightLabel.text = shadow.HasValue
                ? (shadow.Value.InShadow ? "In Shadow" : "In Sunlight")
                : "Unknown";
        }

        if (LastChangepoint != null)
            LastChangepoint.UT = Processor.LastChangepoint;
        if (NextChangepoint != null)
            NextChangepoint.UT = Processor.NextChangepoint;

        if (ResourceContent != null && ResourceTable != null)
        {
            ClearChildren(ResourceContent);
            VesselDetailPopup.PopulateResourceTable(ResourceContent, ResourceTable, Processor);
        }

        if (ConverterContent != null)
        {
            ClearChildren(ConverterContent);
            VesselDetailPopup.PopulateConverterList(ConverterContent, Processor);
        }
    }

    static void ClearChildren(Transform parent)
    {
        for (int i = parent.childCount - 1; i >= 0; i--)
            Destroy(parent.GetChild(i).gameObject);
    }
}
