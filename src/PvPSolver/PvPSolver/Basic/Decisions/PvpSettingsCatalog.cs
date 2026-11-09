namespace RotationSolver.Decisions;

/// <summary>The kind of control a catalog setting is drawn with.</summary>
public enum SettingControl
{
    /// <summary>A checkbox bound to a bool (a <c>ConditionBoolean</c> or a plain bool property).</summary>
    Toggle,

    /// <summary>A whole-percent slider bound to a float ratio (0.15 shown as 15%).</summary>
    Percent,

    /// <summary>A whole-number drag bound to a float number of seconds.</summary>
    Seconds,

    /// <summary>A set of radio buttons or a combo bound to an enum property, by member name.</summary>
    Choice,
}

/// <summary>One value of a <see cref="SettingControl.Choice"/> setting.</summary>
/// <param name="Name">The enum member name.</param>
/// <param name="Label">The plain-English label.</param>
public sealed record SettingChoice(string Name, string Label);

/// <summary>
///     One global setting of the settings window: where it lives (tab and section), the property of
///     <c>Service.Config</c> it is bound to, its plain label, the help line printed under it and its default, so a
///     test can compare the default shown to the default in the code.
/// </summary>
/// <param name="Tab">The tab it is drawn on.</param>
/// <param name="Section">The heading it is drawn under.</param>
/// <param name="Property">The <c>Configs</c> property it is bound to.</param>
/// <param name="Control">How it is drawn.</param>
/// <param name="Label">Plain-English label.</param>
/// <param name="Help">One or two plain sentences printed under the control.</param>
/// <param name="DefaultOn">The default of a toggle.</param>
/// <param name="DefaultNumber">The default of a percent (a ratio) or seconds setting.</param>
/// <param name="Min">The lowest value of a percent (ratio) or seconds setting.</param>
/// <param name="Max">The highest value of a percent (ratio) or seconds setting.</param>
/// <param name="DefaultChoice">The default member name of a choice setting.</param>
/// <param name="Choices">The values of a choice setting.</param>
/// <param name="Parent">The toggle property that must be on for this setting to be editable ("" when none).</param>
/// <param name="PerJob">True when the value is stored for each job (read and written through the job-keyed accessors).</param>
public sealed record SettingSpec(
    string Tab,
    string Section,
    string Property,
    SettingControl Control,
    string Label,
    string Help,
    bool DefaultOn,
    float DefaultNumber,
    float Min,
    float Max,
    string DefaultChoice,
    IReadOnlyList<SettingChoice> Choices,
    string Parent,
    bool PerJob);

/// <summary>
///     Fork-only data (no game types): the global settings of the PvP settings window, in display order. The window
///     draws these from the list; the harness reads the same list and checks every property name, type and default
///     against the real <c>Configs</c> class. Settings that need their own widgets (the targeting list, the teaching
///     colour, the job options and the defensive rows) are not in it.
/// </summary>
public static class PvpSettingsCatalog
{
    /// <summary>Tab names.</summary>
    public const string Match = "Match", Survival = "Survival", Targeting = "Targeting", Display = "Display", Advanced = "Advanced";

    private const string WindowsSection = "Windows (shown only in a PvP zone; settings can be changed anywhere)";

    private static readonly IReadOnlyList<SettingChoice> NoChoices = [];

    private static SettingSpec Toggle(string tab, string section, string property, string label, string help, bool on,
        string parent = "", bool perJob = false) =>
        new(tab, section, property, SettingControl.Toggle, label, help, on, 0f, 0f, 0f, string.Empty, NoChoices, parent, perJob);

    private static SettingSpec Percent(string tab, string section, string property, string label, string help, float ratio,
        float min = 0f, float max = 1f) =>
        new(tab, section, property, SettingControl.Percent, label, help, false, ratio, min, max, string.Empty, NoChoices, string.Empty, false);

    private static SettingSpec Seconds(string tab, string section, string property, string label, string help, float seconds,
        float min, float max, string parent = "") =>
        new(tab, section, property, SettingControl.Seconds, label, help, false, seconds, min, max, string.Empty, NoChoices, parent, false);

    private static SettingSpec Choice(string tab, string section, string property, string label, string help, string defaultChoice,
        IReadOnlyList<SettingChoice> choices, string parent = "") =>
        new(tab, section, property, SettingControl.Choice, label, help, false, 0f, 0f, 0f, defaultChoice, choices, parent, false);

    /// <summary>The settings, in display order within each tab.</summary>
    public static readonly IReadOnlyList<SettingSpec> All =
    [
        // ---- Match ----
        Toggle(Match, "Start and stop", "AutoOnPvPMatchStart", "Turn on when a match starts",
            "PvP Solver switches itself on while the loading screen of a PvP match is up.", true),
        Toggle(Match, "Start and stop", "AutoOffPvPMatchEnd", "Turn off when a match ends",
            "PvP Solver switches itself off when the end-of-match display appears.", true),
        Toggle(Match, "Start and stop", "AutoOffSwitchClass", "Turn off when I change job",
            "PvP Solver switches itself off when the character's job changes.", true),
        Toggle(Match, "Start and stop", "AutoOffCutScene", "Turn off during cutscenes",
            "PvP Solver switches itself off while a cutscene plays.", true),
        Toggle(Match, "Start and stop", "AutoOffAfterCombat", "Turn off when combat has been over for a while",
            "PvP Solver switches itself off a set time after combat ends, unless combat starts again first. This also happens in the middle of a match.", true),
        Seconds(Match, "Start and stop", "AutoOffAfterCombatTime", "Seconds to wait after combat ends",
            "How long to wait before switching off.", 30f, 0f, 600f, parent: "AutoOffAfterCombat"),

        // ---- Survival ----
        Percent(Survival, "Guard", "HealthForGuard", "Use Guard when HP is at or below",
            "Guard is used automatically at or below this HP. This one value applies to every job.", 0.15f),
        Toggle(Survival, "Guard", "PvpGuardControl", "Do nothing else while Guard is up",
            "While Guard is active, no other action is used.", true),
        Toggle(Survival, "Guard", "PvpGcdLockControl", "Pause attacks below 50% HP while holding 2000 or more MP (experimental)",
            "No weaponskill or spell is started while HP is below 50% and at least 2000 MP is available, which leaves the MP for healing.", true),
        Toggle(Survival, "Purify (removes crowd control)", "PvpPurifyStun", "Stun",
            "Use Purify when the character is stunned.", true),
        Toggle(Survival, "Purify (removes crowd control)", "PvpPurifySilence", "Silence",
            "Use Purify when the character is silenced.", true),
        Toggle(Survival, "Purify (removes crowd control)", "PvpPurifyDeepFreeze", "Deep Freeze",
            "Use Purify when the character is frozen by Deep Freeze.", true),
        Toggle(Survival, "Purify (removes crowd control)", "PvpPurifyMiracleOfNature", "Miracle of Nature",
            "Use Purify when Miracle of Nature is on the character.", true),
        Toggle(Survival, "Purify (removes crowd control)", "PvpPurifyHeavy", "Heavy",
            "Use Purify when the character is slowed by Heavy.", false),
        Toggle(Survival, "Purify (removes crowd control)", "PvpPurifyBind", "Bind",
            "Use Purify when the character is bound and cannot move.", false),
        Toggle(Survival, "Movement", "PvpAllowSprintWithoutTarget", "Allow Sprint when there is no target (experimental)",
            "Sprint can also be used in combat while nothing is targeted.", true),
        Toggle(Survival, "Defensive abilities", "PvpDefensivesMaster", "Use my job's defensive abilities automatically (master switch)",
            "Turns the automatic defensives on or off all at once. Off stops the new HP-based defensives, the HP condition on Recuperate, and also " +
            "the six that were already automatic at a fixed HP before: Heart of Corundum, Riddle of Earth, Lady of Crowns, Microcosmos, Meisui and " +
            "Impalement. Each job's list is in the My Job tab.", true),

        // ---- Targeting ----
        Choice(Targeting, "Attacks", "AoEType", "Area attacks",
            "Full uses every available area attack. Cleave uses only single-target area attacks. Off uses none.", "Full",
            [new("Off", "Off"), new("Cleave", "Cleave"), new("Full", "Full")]),
        Toggle(Targeting, "Attacks", "AutoBurst", "Use burst abilities automatically",
            "Lets the rotation use the abilities it marks as burst.", true),
        Toggle(Targeting, "Staying on a target", "StickyTarget", "Stay on my target while my own debuff is ticking",
            "Keeps targeting an enemy that holds a timed debuff applied by the character (Wildfire and similar), instead of jumping to another enemy.", true),
        Seconds(Targeting, "Staying on a target", "StickyTargetMaxRemaining", "Ignore my debuffs with more than this many seconds left",
            "A debuff with more time left than this does not hold the target.", 30f, 1f, 60f, parent: "StickyTarget"),
        Toggle(Targeting, "Invulnerable enemies and The Shatter", "IgnorePvPInvincibility", "Keep attacking invulnerable enemies",
            "Enemies under an invulnerability effect are normally left out of targeting. This is stored for each job; it applies to the job chosen in the My Job tab.", false, perJob: true),
        Toggle(Targeting, "Invulnerable enemies and The Shatter", "PrioAtomelith", "The Shatter: go for A-tier tomeliths first",
            "In The Shatter, A-tier tomeliths are targeted before other enemies.", false),
        Toggle(Targeting, "Invulnerable enemies and The Shatter", "PrioBtomelith", "The Shatter: go for B-tier tomeliths first",
            "In The Shatter, B-tier tomeliths are targeted before other enemies.", false),

        // ---- Display ----
        Toggle(Display, "Server info bar and messages", "ShowInfoOnDtr", "Show status in the server info bar",
            "Shows the current mode in the server information bar. Clicking the entry cycles the mode.", true),
        Choice(Display, "Server info bar and messages", "DTRType", "Clicking the info bar entry cycles through",
            "Which modes a click on the entry moves between.", "DTRNormal",
            [
                new("DTRNormal", "Cycle between first Auto, Manual, and Off"),
                new("DTRAllAuto", "Cycle between each Auto, Manual, and Off"),
                new("DTRAuto", "Cycle between Auto and Off"),
                new("DTRManual", "Cycle between Manual and Off"),
                new("DTRManualAuto", "Cycle between Manual and Auto"),
            ], parent: "ShowInfoOnDtr"),
        Toggle(Display, "Server info bar and messages", "ShowInfoOnToast", "Show a message when the mode changes",
            "Shows a pop-up message each time the mode changes.", false),
        Toggle(Display, WindowsSection, "ShowControlWindow", "Control window",
            "A small window with the next action and the buttons for mode, targeting, area attacks and burst.", false),
        Toggle(Display, WindowsSection, "IsControlWindowLock", "Lock the control window",
            "Stops the control window from being moved or resized.", false, parent: "ShowControlWindow"),
        Toggle(Display, WindowsSection, "ShowNextActionWindow", "Next action window",
            "A window showing the action PvP Solver will use next.", false),
        Toggle(Display, WindowsSection, "ShowCooldownWindow", "Cooldown window",
            "A window showing the cooldowns of the actions PvP Solver uses.", false),
        Toggle(Display, WindowsSection, "ShowActionTimelineWindow", "Timeline window",
            "A window showing a timeline of the actions used.", false),
        Toggle(Display, WindowsSection, "OnlyShowWithHostileOrInDuty", "Only show these windows when enemies are near or in a duty",
            "Hides the windows when no enemy is within 25 yalms and the character is not in a duty.", false),
        Toggle(Display, "Teaching mode", "TeachingMode", "Teaching mode",
            "Highlights the next action on the hotbars so the rotation can be followed by hand.", false),
        Toggle(Display, "Teaching mode", "TeachingModeAutoTarget", "Switch to the suggested target in combat",
            "While teaching mode is on, the target is switched to the one the rotation suggests.", false, parent: "TeachingMode"),
        Toggle(Display, "Teaching mode", "TeachingModeShowTargetHint", "Show the suggested target in the Next Action window",
            "Shows the suggested target's name under the next action: orange when it is not the current target, green when it is.", false, parent: "TeachingMode"),
        Toggle(Display, "Hotbars and tooltips", "ReddenDisabledHotbarActions", "Tint disabled actions on hotbars",
            "Tints the hotbar buttons of actions that have been disabled in PvP Solver.", false),
        Toggle(Display, "Hotbars and tooltips", "ShowTooltips", "Show tooltips",
            "Shows extra explanations while hovering over settings.", true),

        // ---- Advanced ----
        Toggle(Advanced, "Debugging", "InDebug", "Debug mode",
            "Shows debugging information in the Debug tab of the full Rotation Solver settings. Leave off unless asked to turn it on.", false),
    ];

    /// <summary>The settings of one tab, in display order.</summary>
    public static IEnumerable<SettingSpec> ForTab(string tab) => All.Where(s => s.Tab == tab);

    /// <summary>The settings of one section of a tab, in display order.</summary>
    public static IEnumerable<SettingSpec> ForSection(string tab, string section) => All.Where(s => s.Tab == tab && s.Section == section);

    /// <summary>The distinct section headings of a tab, in first-appearance order.</summary>
    public static IReadOnlyList<string> SectionsOf(string tab) => [.. ForTab(tab).Select(s => s.Section).Distinct()];

    /// <summary>The setting bound to a property, or null.</summary>
    public static SettingSpec? Of(string property) => All.FirstOrDefault(s => s.Property == property);
}
