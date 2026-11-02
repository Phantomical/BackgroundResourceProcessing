using System.Text;
using BackgroundResourceProcessing.Utils;
using TMPro;
using UnityEngine;

namespace BackgroundResourceProcessing.UI.Components;

internal class RelativeTimeLabel : MonoBehaviour
{
    internal double UT;
    internal string Prefix;

    TextMeshProUGUI _label;
    int _updateSeconds;
    bool _showingNever;

    void Awake()
    {
        _label = GetComponentInChildren<TextMeshProUGUI>();
    }

    void Update()
    {
        if (_showingNever && !MathUtil.IsFinite(UT))
            return;

        var now = Planetarium.GetUniversalTime();
        var seconds = (int)(UT - now);
        if (seconds == _updateSeconds)
            return;

        Refresh(now);
    }

    void OnEnable()
    {
        Refresh(Planetarium.GetUniversalTime());
    }

    void Refresh(double now)
    {
        if (_label == null)
            return;

        if (!MathUtil.IsFinite(UT))
        {
            _label.text = "Never";
            _showingNever = true;
            return;
        }

        _showingNever = false;

        var time = KSPUtil.PrintTime(UT - now, 3, false);
        _label.text = Prefix != null ? Prefix + time : time;
        _updateSeconds = (int)(UT - now);
    }
}
