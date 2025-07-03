using System;
using System.Collections;
using System.Collections.Generic;
using System.Xml;
using BackgroundResourceProcessing.Converter;
using Highlighting;
using UnityEngine;
using UnityEngine.Analytics;

namespace BackgroundResourceProcessing.UI
{
    internal partial class DebugUI
    {
        ConverterTab converterTab;

        private void DrawConverterTab()
        {
            converterTab.Draw();
        }

        float GetPopupRectX(int width)
        {
            if (Screen.width - window.xMax > width + 50)
                return window.xMax;
            if (window.xMin > width + 50)
                return window.xMin - width;
            return window.xMin;
        }

        private class ConverterTab(DebugUI ui)
        {
            readonly DebugUI ui = ui;

            Part part = null;
            PartModule module = null;
            List<ConverterInfo> infos = null;
            Exception exception = null;

            public ModuleSelectorPopup popup = null;
            bool selectorActive = false;
            bool cancelSelection = false;

            Vector2 scroll = new();

            readonly GUIStyle exceptionStyle = new(HighLogic.Skin.label)
            {
                alignment = TextAnchor.UpperLeft,
            };
            readonly GUIStyle rightHeader = new(HeaderStyle) { alignment = TextAnchor.MiddleRight };

            public void Draw()
            {
                GUILayout.BeginVertical();
                DrawSelectionButtons();
                GUILayout.Space(5);
                DrawSelectionInfo();
                GUILayout.Space(5);
                GUILayout.EndVertical();

                scroll = GUILayout.BeginScrollView(
                    scroll,
                    GUILayout.ExpandHeight(true),
                    GUILayout.ExpandWidth(true),
                    GUILayout.MinHeight(400)
                );
                DrawRecipeInfo();
                GUILayout.EndScrollView();
            }

            void DrawSelectionButtons()
            {
                using var group = new PushHorizontalGroup();

                if (!selectorActive)
                {
                    if (GUILayout.Button("Select Part"))
                    {
                        cancelSelection = false;
                        ui.StartCoroutine(PauseTimeWhile(SelectPart));
                    }
                }
                else
                {
                    if (GUILayout.Button("Cancel Part Selection"))
                        cancelSelection = true;
                }

                {
                    using var disabled = new PushEnabeled(popup == null && part != null);
                    if (GUILayout.Button("Select Part Module"))
                    {
                        var x = ui.GetPopupRectX(100);
                        var y = ui.window.yMin;

                        if (popup != null)
                            popup.Close();

                        popup = ui.gameObject.AddComponent<ModuleSelectorPopup>();
                        popup.ui = this;
                        popup.modules = GetValidPartModules(part);
                        popup.SetPosition(x, y);
                    }
                }

                {
                    using var disabled = new PushEnabeled(module != null);
                    if (GUILayout.Button("Refresh"))
                        SetModule(module);
                }
            }

            void DrawSelectionInfo()
            {
                string message;

                message = "No part selected";
                if (part != null)
                    message = $"Selected Part: {part.name}";

                GUILayout.Label(message, GUILayout.ExpandWidth(true));

                message = "No module selected";
                if (module != null)
                    message = $"Selected Module: {module.GetType().Name}";

                GUILayout.Label(message, GUILayout.ExpandWidth(true));
            }

            void DrawRecipeInfo()
            {
                using var h1 = new PushHorizontalGroup();

                if (exception != null)
                {
                    GUILayout.Label(
                        exception.ToString(),
                        exceptionStyle,
                        GUILayout.ExpandWidth(true)
                    );
                    return;
                }

                if (infos == null)
                    return;

                DrawColumn(() =>
                {
                    GUILayout.Label("Type", HeaderStyle, GUILayout.ExpandWidth(true));

                    var index = 0;
                    foreach (var info in infos)
                    {
                        var name = info.converter.GetType().Name;

                        GUILayout.Space(5);

                        // Draw a label that overflows the current column
                        //
                        // We reserve no horizontal space and then use GUI.Layout
                        // to draw in a box that stretches across the rest of the
                        // window.
                        var rect = GUILayoutUtility.GetRect(
                            new GUIContent(""),
                            HeaderStyle,
                            GUILayout.ExpandWidth(true)
                        );
                        rect.width = ui.window.width - rect.xMin;

                        GUI.Label(rect, $"{index} - {name}", HeaderStyle);

                        foreach (var input in info.resources.Inputs)
                            GUILayout.Label("INPUT", GUILayout.ExpandWidth(true));
                        foreach (var output in info.resources.Outputs)
                            GUILayout.Label("OUTPUT", GUILayout.ExpandWidth(true));
                        foreach (var required in info.resources.Requirements)
                            GUILayout.Label("REQUIRED", GUILayout.ExpandWidth(true));

                        index += 1;
                    }
                });

                DrawColumn(() =>
                {
                    GUILayout.Label("Resource", HeaderStyle, GUILayout.ExpandWidth(true));

                    foreach (var info in infos)
                    {
                        GUILayout.Space(5);
                        GUILayout.Label("");

                        foreach (var input in info.resources.Inputs)
                            GUILayout.Label(input.ResourceName, GUILayout.ExpandWidth(true));
                        foreach (var output in info.resources.Outputs)
                            GUILayout.Label(output.ResourceName, GUILayout.ExpandWidth(true));
                        foreach (var required in info.resources.Requirements)
                            GUILayout.Label(required.ResourceName, GUILayout.ExpandWidth(true));
                    }
                });

                DrawColumn(() =>
                {
                    GUILayout.Label("Ratio", rightHeader, GUILayout.ExpandWidth(true));

                    foreach (var info in infos)
                    {
                        GUILayout.Space(5);
                        GUILayout.Label("");

                        foreach (var input in info.resources.Inputs)
                            GUILayout.Label(
                                FormatCellNumber(input.Ratio),
                                CellStyle,
                                GUILayout.ExpandWidth(true)
                            );
                        foreach (var output in info.resources.Outputs)
                            GUILayout.Label(
                                FormatCellNumber(output.Ratio),
                                CellStyle,
                                GUILayout.ExpandWidth(true)
                            );
                        foreach (var required in info.resources.Requirements)
                            GUILayout.Label(
                                FormatCellNumber(required.Amount),
                                CellStyle,
                                GUILayout.ExpandWidth(true)
                            );
                    }
                });

                DrawColumn(() =>
                {
                    GUILayout.Label("Constraint", rightHeader, GUILayout.ExpandWidth(true));

                    foreach (var info in infos)
                    {
                        GUILayout.Space(5);
                        GUILayout.Label("");

                        foreach (var input in info.resources.Inputs)
                            GUILayout.Label(
                                input.DumpExcess ? "DUMP EXCESS" : "",
                                CellStyle,
                                GUILayout.ExpandWidth(true)
                            );
                        foreach (var output in info.resources.Outputs)
                            GUILayout.Label(
                                output.DumpExcess ? "DUMP EXCESS" : "",
                                CellStyle,
                                GUILayout.ExpandWidth(true)
                            );
                        foreach (var required in info.resources.Requirements)
                            GUILayout.Label(
                                required.Constraint.ToString(),
                                CellStyle,
                                GUILayout.ExpandWidth(true)
                            );
                    }
                });

                DrawColumn(() =>
                {
                    GUILayout.Label("Flow Mode", rightHeader, GUILayout.ExpandWidth(true));

                    foreach (var info in infos)
                    {
                        GUILayout.Space(5);
                        GUILayout.Label("");

                        foreach (var input in info.resources.Inputs)
                            GUILayout.Label(
                                input.FlowMode.ToString(),
                                CellStyle,
                                GUILayout.ExpandWidth(true)
                            );
                        foreach (var output in info.resources.Outputs)
                            GUILayout.Label(
                                output.FlowMode.ToString(),
                                CellStyle,
                                GUILayout.ExpandWidth(true)
                            );
                        foreach (var required in info.resources.Requirements)
                            GUILayout.Label(
                                required.FlowMode.ToString(),
                                CellStyle,
                                GUILayout.ExpandWidth(true)
                            );
                    }
                });
            }

            void DrawColumn(Action draw)
            {
                using var v1 = new PushVerticalGroup();
                draw();
            }

            public void SetPart(Part part)
            {
                this.part = part;

                module = null;
                infos = null;
                if (popup != null)
                    popup.Close();
            }

            public void SetModule(PartModule module)
            {
                this.module = module;
                this.infos = null;
                this.exception = null;

                try
                {
                    var adapter = BackgroundConverter.GetConverterForModule(module);
                    if (adapter == null)
                        return;

                    List<ConverterInfo> infos = [];
                    var behaviour = adapter.GetBehaviour(module);
                    if (behaviour == null)
                    {
                        this.infos = [];
                        return;
                    }

                    var state = new VesselState()
                    {
                        Vessel = module.vessel,
                        CurrentTime = Planetarium.GetUniversalTime(),
                    };

                    foreach (var converter in behaviour.Converters)
                    {
                        var resources = converter.GetResources(state);
                        resources.Inputs ??= [];
                        resources.Outputs ??= [];
                        resources.Requirements ??= [];

                        infos.Add(new() { converter = converter, resources = resources });
                    }

                    this.infos = infos;
                }
                catch (Exception e)
                {
                    LogUtil.Error($"Evaluating adapter behaviours threw an exception: {e}");

                    this.infos = null;
                    this.exception = e;
                }
            }

            public void CancelPartSelection()
            {
                cancelSelection = true;
            }

            private static List<PartModule> GetValidPartModules(Part part)
            {
                List<PartModule> modules = [];
                var list = part.Modules;
                for (int i = 0; i < list.Count; i++)
                {
                    var module = list[i];
                    var adapter = BackgroundConverter.GetConverterForModule(module);
                    if (adapter == null)
                        continue;
                    modules.Add(module);
                }
                return modules;
            }

            private IEnumerator SelectPart()
            {
                try
                {
                    selectorActive = true;
                    yield return ui.StartCoroutine(DoSelectPart());
                }
                finally
                {
                    selectorActive = false;
                    cancelSelection = false;
                }
            }

            private IEnumerator DoSelectPart()
            {
                part = null;

                Part hovered = null;

                while (true)
                {
                    yield return null;
                    var current = Mouse.HoveredPart;
                    if (cancelSelection)
                        break;

                    if (!ReferenceEquals(current, hovered))
                    {
                        hovered?.SetHighlightColor();
                        hovered?.SetHighlight(active: false, recursive: false);

                        current?.SetHighlightColor(Highlighter.colorPartEditorActionHighlight);
                        current?.SetHighlight(active: true, recursive: false);
                    }

                    hovered = current;
                    if (hovered == null)
                        continue;

                    if (Mouse.Left.GetButtonDown())
                    {
                        SetPart(hovered);
                        break;
                    }
                }

                hovered?.SetHighlightColor();
                hovered?.SetHighlight(active: false, recursive: false);
            }

            private IEnumerator PauseTimeWhile(Func<IEnumerator> action)
            {
                yield return null;

                var previousTimeScale = Time.timeScale;

                try
                {
                    Time.timeScale = 0;
                    GameEvents.onGamePause.Fire();

                    yield return ui.StartCoroutine(action());
                }
                finally
                {
                    Time.timeScale = previousTimeScale;
                    GameEvents.onGameUnpause.Fire();
                }
            }
        }

        private class ModuleSelectorPopup : MonoBehaviour
        {
            public ConverterTab ui;
            public List<PartModule> modules = [];
            public int depth;

            Rect window;

            public ModuleSelectorPopup()
            {
                depth = GUI.depth - 1;
            }

            public void SetPosition(float x, float y)
            {
                window = new(x, y, 100, 100);
            }

            public void Close()
            {
                Destroy(this);
                ui.popup = null;
            }

            void OnGUI()
            {
                window = WindowProvider.GUILayoutWindow(
                    GetInstanceID(),
                    window,
                    DrawWindow,
                    "Select Part Module",
                    HighLogic.Skin.window
                );
            }

            void DrawWindow(int windowId)
            {
                using var g1 = new PushGUISkin(HighLogic.Skin);
                using var g2 = new PushDepth(depth);

                DrawModuleList();
                GUI.DragWindow();
            }

            void DrawModuleList()
            {
                using var g2 = new PushVerticalGroup();

                var closeButtonRect = new Rect(
                    window.width - CloseButtonSize - CloseButtonMargin,
                    CloseButtonMargin,
                    CloseButtonSize,
                    CloseButtonSize
                );
                if (GUI.Button(closeButtonRect, "X"))
                    Close();

                GUILayout.Space(5);

                if (modules.Count != 0)
                {
                    GUILayout.Label(
                        "Modules are listed in their order on the part",
                        GUILayout.ExpandWidth(true),
                        GUILayout.MinWidth(100)
                    );

                    GUILayout.Space(5);

                    foreach (var module in modules)
                    {
                        var name = module.GetType().Name;
                        if (GUILayout.Button(name, GUILayout.ExpandWidth(true)))
                        {
                            Close();
                            ui.SetModule(module);
                        }
                    }
                }
                else
                {
                    GUILayout.Label(
                        "This part has no modules with background behaviour.",
                        GUILayout.ExpandHeight(true)
                    );
                }
            }
        }

        private struct ConverterInfo
        {
            public ConverterBehaviour converter;
            public ConverterResources resources;
        }
    }
}
