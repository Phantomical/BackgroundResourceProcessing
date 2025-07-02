using System.Reflection;

namespace BackgroundResourceProcessing
{
    // See "Modders notes for KSP 1.2" for a description of how to use this.
    // https://forum.kerbalspaceprogram.com/topic/147576-modders-notes-for-ksp-12/

    /// <summary>
    /// Debug-related settings for background resource processing.
    /// </summary>
    public class DebugSettings : GameParameters.CustomParameterNode
    {
        // Some of the settings here are used for logging so accessing them has
        // to be fast. EventRouter is responsible for updating this when the
        // settings change.
        internal static DebugSettings Instance { get; set; } = new();

        public override string Title => "Debug Settings";
        public override string Section => "BackgroundResourceProcessing";
        public override string DisplaySection => "Background Resource Processing";
        public override int SectionOrder => 99;
        public override bool HasPresets => false;
        public override GameParameters.GameMode GameMode => GameParameters.GameMode.ANY;

        [GameParameters.CustomParameterUI("Enable the Debug UI?")]
        public bool DebugUI = false;

        [GameParameters.CustomParameterUI(
            "Enable Debug Logging?",
            toolTip = "Enable verbose debug output to KSP.log."
        )]
        public bool DebugLogging = false;

        [GameParameters.CustomParameterUI(
            "Enable Solver Trace Logging?",
            toolTip = "Enable extraordinarily verbose logging of internal solver states. You don't need this unless you are debugging BRP itself."
        )]
        public bool SolverTrace = false;

        public override bool Enabled(MemberInfo member, GameParameters parameters)
        {
            return member.Name switch
            {
                // We only show the solver trace flag if debug logging is enabled.
                "SolverTrace" => DebugLogging,
                _ => true,
            };
        }
    }
}
