using System;
using System.Collections.Generic;
using System.Linq;
using BackgroundResourceProcessing.Core;
using UnityEngine;

namespace BackgroundResourceProcessing.UI
{
    internal partial class DebugUI
    {
        internal struct ResourceTab()
        {
            public bool refreshButton = false;
        }

        ResourceTab resourceTab = new();

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

            var prevState = resourceTab.refreshButton;
            resourceTab.refreshButton = GUILayout.Button("Refresh");
            if (resourceTab.refreshButton && !prevState)
                processor = GetMainVesselProcessor();
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
    }
}
