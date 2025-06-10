using System;
using ClickThroughFix;
using HarmonyLib;
using UnityEngine;

namespace BackgroundResourceProcessing.Integration.ClickThroughBlocker
{
    [KSPAddon(KSPAddon.Startup.Instantly, true)]
    internal class Loader : MonoBehaviour
    {
        static Type GetDebugUIType()
        {
            var assembly = typeof(BackgroundResourceProcessor).Assembly;
            return assembly.GetType("BackgroundResourceProcessing.UI.DebugUI");
        }

        void Awake()
        {
            Harmony.DEBUG = true;

            var harmony = new Harmony(
                "BackgroundResourceProcessing.Integration.ClickThroughBlocker"
            );

            var type = GetDebugUIType();
            if (type == null)
            {
                Debug.LogWarning(
                    $"Unable to find DebugUI type within BackgroundResourceProcessing assembly"
                );
                return;
            }

            var method = AccessTools.Method(type, "GUIWindowLayout");
            if (method == null)
            {
                Debug.LogWarning($"DebugUI has no method named GUIWindowLayout");
                return;
            }

            var prefix = AccessTools.Method(GetType(), nameof(GUILayoutWindowPrefix));
            harmony.Patch(method, new HarmonyMethod(prefix));
        }

        static bool GUILayoutWindowPrefix(
            ref Rect __result,
            int id,
            Rect screenRect,
            GUI.WindowFunction func,
            string text,
            GUIStyle style
        )
        {
            __result = ClickThruBlocker.GUIWindow(id, screenRect, func, text, style);
            return true;
        }
    }
}
