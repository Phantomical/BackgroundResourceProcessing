using BackgroundResourceProcessing.UI;
using ClickThroughFix;
using UnityEngine;

namespace BackgroundResourceProcessing.Integration.ClickThroughBlocker
{
    [KSPAddon(KSPAddon.Startup.Instantly, true)]
    internal class Loader : MonoBehaviour
    {
        void Start()
        {
            DebugUI.WindowProvider = new ClickThroughBlockerWindowProvider();
        }

        internal class ClickThroughBlockerWindowProvider : DebugUI.IGUIWindowProvider
        {
            public Rect GUILayoutWindow(
                int id,
                Rect screenRect,
                GUI.WindowFunction func,
                string text,
                GUIStyle style
            )
            {
                return ClickThruBlocker.GUILayoutWindow(id, screenRect, func, text, style);
            }
        }
    }
}
