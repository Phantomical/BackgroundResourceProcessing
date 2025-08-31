using System.Reflection;

namespace BackgroundResourceProcessing;

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

    [GameParameters.CustomParameterUI("Enable Burst-Accelerated Methods")]
    public bool EnableBurst = true;

    [GameParameters.CustomParameterUI("Enable Solution Cache")]
    public bool EnableSolutionCache = true;

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

public class Settings : GameParameters.CustomParameterNode
{
    public override string Title => "Settings";
    public override string Section => "BackgroundResourceProcessing";
    public override string DisplaySection => "Background Resource Processing";
    public override int SectionOrder => 0;
    public override bool HasPresets => false;
    public override GameParameters.GameMode GameMode => GameParameters.GameMode.ANY;

    [GameParameters.CustomParameterUI("Enable USI-LS Integration")]
    public bool EnableUSILSIntegration = true;

    [GameParameters.CustomParameterUI(
        "Enable SpaceDust Integration",
        toolTip = "Patch SpaceDust's background simulation to interact with "
            + "Background Resource Processing."
    )]
    public bool EnableSpaceDustIntegration = true;

    [GameParameters.CustomParameterUI(
        "Enable Experimental Orbit Shadows",
        toolTip = "Simulate the effect of planet shadows on orbiting ships. "
            + "This is currently quite buggy, though it won't break your game. "
    )]
    public bool EnableOrbitShadows = false;

    [GameParameters.CustomParameterUI(
        "Enable Day/Night Simulation",
        toolTip = "Simulate Day/Night for landed vessels."
    )]
    public bool EnableLandedShadows = true;

    [GameParameters.CustomParameterUI(
        "Take over background processing of science labs",
        toolTip = "Simulate the conversion of lab data into science. "
            + "If you are encountering bugs with lab data then you can "
            + "disable this to work around it."
    )]
    public bool EnableBackgroundScienceLabProcessing = true;
}

public class ModIntegrationSettings : GameParameters.CustomParameterNode
{
    public override string Title => "Mod Integrations";
    public override string Section => "BackgroundResourceProcessing";
    public override string DisplaySection => "Background Resource Processing";

    public override int SectionOrder => 1;
    public override bool HasPresets => false;

    public override GameParameters.GameMode GameMode => GameParameters.GameMode.ANY;

    [GameParameters.CustomParameterUI(
        "Enable Snacks! Integration",
        toolTip = "Patch Snacks' background simulation to use Background Resource Processing"
    )]
    public bool EnableSnacksIntegration = true;
}
