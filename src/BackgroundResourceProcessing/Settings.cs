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

    /// <summary>
    /// Setting this will automatically break unity code, since the test
    /// allocator never actually frees anything.
    /// </summary>
    internal bool UseTestAllocator = false;

    public override bool Enabled(MemberInfo member, GameParameters parameters)
    {
        return member.Name switch
        {
            // We only show the solver trace flag if debug logging is enabled.
            "SolverTrace" => DebugLogging,
            _ => true,
        };
    }

    /// <summary>
    /// Configure the debug settings to disable anything that cannot be used
    /// when running outside of unity.
    /// </summary>
    internal void ConfigureUnityExternal()
    {
        EnableBurst = false;
        UseTestAllocator = true;
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

    [GameParameters.CustomParameterUI(
        "Enable Orbit Shadows",
        toolTip = "Simulate the effect of planet shadows on orbiting ships."
    )]
    public bool EnableOrbitShadows = true;

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

    [GameParameters.CustomParameterUI("Enable USI-LS Integration")]
    public bool EnableUSILSIntegration = true;

    [GameParameters.CustomParameterUI(
        "Enable SpaceDust Integration",
        toolTip = "Patch SpaceDust's background simulation to interact with "
            + "Background Resource Processing."
    )]
    public bool EnableSpaceDustIntegration = true;

    [GameParameters.CustomParameterUI(
        "Enable Wild Blue Integrations",
        toolTip = "Patch background simulation in the various WBI mods to use Background Resource Processing. "
            + "Includes support for Snacks! and WildBlueTools."
    )]
    public bool EnableWildBlueIntegration = true;

    [GameParameters.CustomParameterUI(
        "Enable TAC-LS Integration",
        toolTip = "Show resource depletion times in TAC-LS monitoring window based on Background Resource Processing simulation. "
            + "Provides more accurate predictions for unloaded vessels."
    )]
    public bool EnableTACLSIntegration = true;

    [GameParameters.CustomParameterUI(
        "Enable Persistent Thrust Integration",
        toolTip = "Patch Persistent Thrust's background processing to make use "
            + "of Background Resource Processing"
    )]
    public bool EnablePersistentThrustIntegration = true;

    [GameParameters.CustomParameterUI(
        "Enable BonVoyage Integration",
        toolTip = "Patch BonVoyage to use Background Resource Processing's simulation"
    )]
    public bool EnableBonVoyageIntegration = true;
}
