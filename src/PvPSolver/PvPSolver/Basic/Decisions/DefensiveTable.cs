using System.Collections.Concurrent;

namespace RotationSolver.Decisions;

/// <summary>What a defensive does for the character; used to keep two of the same kind from stacking.</summary>
public enum DefensiveKind
{
    /// <summary>A barrier that absorbs damage.</summary>
    Shield,

    /// <summary>A percentage reduction of damage taken.</summary>
    Mitigation,

    /// <summary>A heal on the character.</summary>
    SelfHeal,
}

/// <summary>How the generic stage treats a row.</summary>
public enum DefensiveMode
{
    /// <summary>The stage uses the action on the character when the HP test passes.</summary>
    Fire,

    /// <summary>The base code already uses the action; the row only adds an HP condition to that existing call.</summary>
    Gate,

    /// <summary>
    ///     A job file already uses the action with a fixed HP number; the row turns that number into a setting.
    ///     The generic stage never uses these rows.
    /// </summary>
    Param,
}

/// <summary>
///     One defensive of the generic table. Pure data (no Dalamud or game types): action ids are the game's ids,
///     status ids are the game's status ids.
/// </summary>
/// <param name="ActionName">The <c>ActionID</c> enum name of the PvP action (the tests tie it to <paramref name="ActionId"/>).</param>
/// <param name="ActionId">The game's action id.</param>
/// <param name="Job">The job key ("WHM"), a role key ("TANK", "HEALER") or "*" for every job.</param>
/// <param name="Kind">Shield, mitigation or self-heal.</param>
/// <param name="Mode">Fire, Gate or Param.</param>
/// <param name="DefaultEnabled">Whether the row is on when the setting was never touched.</param>
/// <param name="DefaultPercent">The HP ratio (0 to 1) used when the setting was never touched.</param>
/// <param name="MinPercent">The lowest HP ratio the setting may take.</param>
/// <param name="UsedUp">Pass <c>usedUp: true</c> to the action's CanUse (multi-charge actions).</param>
/// <param name="SkipTargetStatusNeed">Pass <c>skipTargetStatusNeedCheck: true</c> to the action's CanUse.</param>
/// <param name="ProvidesStatus">Status ids the action puts on the character; the row is not used while one is active.</param>
/// <param name="SuppressWhileStatus">Status ids that keep this row silent while active on the character.</param>
/// <param name="Label">Plain-English name for the window.</param>
/// <param name="Tooltip">Plain-English explanation for the window.</param>
/// <param name="DefersTo">The existing behaviour that keeps priority over this row ("" when none).</param>
public sealed record DefensiveRow(
    string ActionName,
    uint ActionId,
    string Job,
    DefensiveKind Kind,
    DefensiveMode Mode,
    bool DefaultEnabled,
    float DefaultPercent,
    float MinPercent,
    bool UsedUp,
    bool SkipTargetStatusNeed,
    IReadOnlyList<uint> ProvidesStatus,
    IReadOnlyList<uint> SuppressWhileStatus,
    string Label,
    string Tooltip,
    string DefersTo);

/// <summary>
///     Fork-only data (no game types): the 20 rows of the generic per-job defensive table and the pure rules that
///     read it. Table order is priority order: a job's own rows come before the role rows (Rampart, Stoneskin II).
/// </summary>
public static class DefensiveTable
{
    /// <summary>
    ///     The worst case, said in every Fire row's tooltip: the 2.5 second pause and the same-kind check limit
    ///     chaining, they do not remove it.
    /// </summary>
    public const string WorstCase =
        "Worst case: after one automatic defensive is used, no other one is used for 2.5 seconds, and none is used " +
        "while a barrier, damage reduction or heal of the same kind from another turned-on defensive is still " +
        "active. With several turned on, more than one can still be used during one long stretch of low HP.";

    private static readonly uint[] None = [];

    // Status ids (game Status sheet, 2026.09.15 data; the tests tie each to the fork's StatusID enum).
    private const uint Rampart = 4168, StoneskinIi = 4481, Aquaveil = 3086, WreathOfIce = 4316, TemperaCoat = 4114;
    private const uint Forte = 4320, RadiantAegis = 3224, CrestOfTimeBorrowed = 2861, HardenedScales = 4096;
    private const uint BlackestNight = 1308, HolySheltron = 3026, StemTheTide = 3031, HeartOfCorundumStatus = 4295;
    private const uint EarthResonance = 3171, HoningDance = 3162;

    /// <summary>Statuses that silence the whole generic stage: Undead Redemption (HP is forced to 1) and Hidden.</summary>
    public static readonly IReadOnlyList<uint> SilenceStatuses = [3039, 1316];

    private static DefensiveRow Fire(string name, uint id, string job, DefensiveKind kind, float percent, string label,
        string what, string defersTo = "", uint[]? provides = null, uint[]? suppress = null, bool usedUp = false, bool skipTsn = false) =>
        new(name, id, job, kind, DefensiveMode.Fire, false, percent, 0f, usedUp, skipTsn,
            provides ?? None, suppress ?? None, label, what + " Off by default. " + WorstCase, defersTo);

    /// <summary>The table, in priority order: job rows, role rows, the Recuperate gate, then the six parameter rows.</summary>
    public static readonly IReadOnlyList<DefensiveRow> Rows =
    [
        // ---- job rows (new, off by default) ----
        Fire("AquaveilPvP", 29227, "WHM", DefensiveKind.Shield, 0.70f, "Aquaveil: barrier",
            "Barrier of 10,000 healing potency for 10 seconds, doubled when it also removes a status that Purify removes. The existing cleansing use of Aquaveil keeps priority.",
            "WHM_Default.PVP.cs: the Purify-status branches of Aquaveil run first", [Aquaveil], skipTsn: true),
        Fire("WreathOfIcePvP", 41478, "BLM", DefensiveKind.Mitigation, 0.70f, "Wreath of Ice: 20% less damage taken",
            "Reduces damage taken by 20% for 10 seconds and counterattacks each time damage is taken. Only available while Elemental Weave has turned into Wreath of Ice (under Umbral Ice).",
            "BLM_Default.PVP.cs: the manual Defense command still offers Wreath of Ice", [WreathOfIce]),
        Fire("TemperaCoatPvP", 39211, "PCT", DefensiveKind.Shield, 0.70f, "Tempera Coat: barrier",
            "Barrier of 12,000 healing potency for 10 seconds.",
            "PCT_Default.PVP.cs: the manual Defense command still offers Tempera Coat", [TemperaCoat]),
        Fire("FortePvP", 41496, "RDM", DefensiveKind.Mitigation, 0.60f, "Forte: 50% less damage taken and a barrier",
            "Reduces damage taken by 50% and adds a 4,000-potency barrier, but only for 5 seconds, so a use triggered by low HP lands late.",
            "RDM_Default.PVP.cs: the manual Defense command still offers Forte", [Forte]),
        Fire("RadiantAegisPvP", 29670, "SMN", DefensiveKind.Shield, 0.70f, "Radiant Aegis: barrier and 25% less damage taken",
            "Barrier of 12,000 healing potency and 25% less damage taken for 10 seconds, always cast on the character itself.",
            "SMN_Default.PVP.cs: the manual Defense command still offers Radiant Aegis", [RadiantAegis]),
        Fire("ArcaneCrestPvP", 29552, "RPR", DefensiveKind.Shield, 0.70f, "Arcane Crest: barrier",
            "Barrier of 12,000 healing potency for 10 seconds; when it is fully absorbed, a heal over time reaches nearby party members.",
            "RPR_Default.PVP.cs: the manual Defense command still offers Arcane Crest", [CrestOfTimeBorrowed]),
        Fire("SnakeScalesPvP", 39185, "VPR", DefensiveKind.Mitigation, 0.45f, "Snake Scales: 50% less damage taken, cannot move",
            "Reduces damage taken by 50% and adds a barrier for 4 seconds, and nullifies stun, heavy, bind, silence and knockback, but the character cannot move while it lasts. The solver never used it before.",
            "", [HardenedScales]),
        Fire("TheBlackestNightPvP", 29093, "DRK", DefensiveKind.Shield, 0.70f, "The Blackest Night: barrier (two charges)",
            "Barrier of 10,000 healing potency for 8 seconds, cast on the character itself. It has two charges; a second one is not spent while the barrier is up.",
            "DRK_Default.PVP.cs: the two-charge Emergency use and the manual Defense command keep their own conditions", [BlackestNight], usedUp: true),
        Fire("HolySheltronPvP", 29067, "PLD", DefensiveKind.Shield, 0.70f, "Holy Sheltron: barrier",
            "Barrier of 8,000 healing potency for 4 seconds, with a damage burst on expiry and a damage reduction when fully absorbed.",
            "PLD_Default.PVP.cs: the manual Defense command still offers Holy Sheltron", [HolySheltron]),
        Fire("BloodwhettingPvP", 29082, "WAR", DefensiveKind.Shield, 0.65f, "Bloodwhetting: barrier and healing from attacks",
            "Barrier equal to 20% of maximum HP and healing from weaponskill damage for 10 seconds.",
            "WAR_Default.PVP.cs: the manual Defense command still offers Bloodwhetting", [StemTheTide]),
        Fire("CuringWaltzPvP", 29429, "DNC", DefensiveKind.SelfHeal, 0.50f, "Curing Waltz: heal",
            "Heals the character and nearby party members (10,000 healing potency). Not used during Honing Dance.",
            "DNC_Default.PVP.cs: the existing party-heal use of Curing Waltz stays", suppress: [HoningDance]),

        // ---- role rows (new, off by default): used only when the role action is the selected PvP role action ----
        Fire("RampartPvP", 43244, "TANK", DefensiveKind.Mitigation, 0.60f, "Rampart: 50% less damage taken",
            "May be used, not will be used: Rampart is a PvP role action and is available only while it is the selected role action. Reduces damage taken by 50% for 15 seconds, 60 second recast.",
            "The manual Defense command still offers Rampart", [Rampart]),
        Fire("StoneskinIiPvP", 43256, "HEALER", DefensiveKind.Shield, 0.70f, "Stoneskin II: barrier on self and allies",
            "May be used, not will be used: Stoneskin II is a PvP role action and is available only while it is the selected role action. Barrier of 12,000 healing potency on the character and nearby party members for 10 seconds, 30 second recast.",
            "The manual Defense command still offers Stoneskin II", [StoneskinIi]),

        // ---- the gate on the existing Recuperate call (not used by the generic stage) ----
        new DefensiveRow("RecuperatePvP", 29711, "*", DefensiveKind.SelfHeal, DefensiveMode.Gate, false, 0.60f, 0.50f, false, false,
            None, None, "Recuperate: heal, only when HP is below this",
            "May be used, not will be used: Recuperate restores 16,000 HP and costs 2,000 MP. It is already used automatically when at least 15,000 HP are missing. Turning this on adds a second condition: it is used only while HP is below the chosen percentage. The lowest value is 50%, because attacks are held back below 50% HP while waiting for Recuperate. Off keeps the existing rule. " + WorstCase,
            "CustomRotation_Ability.cs and CustomRotation_GCD.cs: the existing Recuperate call and its missing-HP rule stay; this only adds a condition"),

        // ---- parameter rows (existing behaviour; the fixed HP number becomes a setting, default on at today's number) ----
        new DefensiveRow("HeartOfCorundumPvP", 41443, "GNB", DefensiveKind.Mitigation, DefensiveMode.Param, true, 0.30f, 0f, false, false,
            [HeartOfCorundumStatus], None, "Heart of Corundum: use when HP is at or below this",
            "Used on the Gunbreaker itself while HP is at or below the chosen percentage, ahead of Guard. Default 30%, the previous fixed value.",
            "GNB_Default.PVP.cs EmergencyAbility: the branch stays where it is; only its number is read from this setting"),
        new DefensiveRow("RiddleOfEarthPvP", 29482, "MNK", DefensiveKind.SelfHeal, DefensiveMode.Param, true, 0.80f, 0f, false, false,
            [EarthResonance], None, "Riddle of Earth: use when HP is below this",
            "Used in combat while HP is below the chosen percentage; the heal comes from Earth's Reply when the effect ends. Default 80%, the previous fixed value.",
            "MNK_Default.PVP.cs EmergencyAbility: the branch stays where it is; only its number is read from this setting"),
        new DefensiveRow("LadyOfCrownsPvP", 41504, "AST", DefensiveKind.SelfHeal, DefensiveMode.Param, true, 0.60f, 0f, false, false,
            None, None, "Lady of Crowns: use when HP is below this",
            "Heals the character and nearby party members while HP is below the chosen percentage, once the Lady of Crowns has been drawn. Default 60%, the previous fixed value.",
            "AST_Default.PVP.cs EmergencyAbility: the branch stays where it is; only its number is read from this setting"),
        new DefensiveRow("MicrocosmosPvP", 29254, "AST", DefensiveKind.SelfHeal, DefensiveMode.Param, true, 0.60f, 0f, false, false,
            None, None, "Microcosmos: use when HP is below this",
            "Triggers the heal of Macrocosmos while HP is below the chosen percentage. Default 60%, the previous fixed value.",
            "AST_Default.PVP.cs EmergencyAbility: the branch stays where it is; only its number is read from this setting"),
        new DefensiveRow("MeisuiPvP", 29508, "NIN", DefensiveKind.SelfHeal, DefensiveMode.Param, true, 0.50f, 0f, false, false,
            None, None, "Meisui: use when HP is below this",
            "Heals while HP is below the chosen percentage, only after the ninjutsu sequence has turned into Meisui. Default 50%, the previous fixed value.",
            "NIN_Default.PVP.cs GeneralGCD: the branch stays where it is; only its number is read from this setting"),
        new DefensiveRow("ImpalementPvP", 41438, "DRK", DefensiveKind.SelfHeal, DefensiveMode.Param, true, 0.60f, 0f, false, false,
            None, None, "Impalement: attack that heals, use when HP is below this",
            "A damaging attack that returns the damage dealt as HP, used while HP is below the chosen percentage. A high value removes damage. Default 60%, the previous fixed value.",
            "DRK_Default.PVP.cs GeneralGCD: the branch stays where it is; only its number is read from this setting"),
    ];

    /// <summary>The Recuperate gate row.</summary>
    public static readonly DefensiveRow Recuperate = Named("RecuperatePvP");

    /// <summary>The Gunbreaker's Heart of Corundum parameter row (previously a fixed 30).</summary>
    public static readonly DefensiveRow HeartOfCorundum = Named("HeartOfCorundumPvP");

    /// <summary>The Monk's Riddle of Earth parameter row (previously a fixed 0.8).</summary>
    public static readonly DefensiveRow RiddleOfEarth = Named("RiddleOfEarthPvP");

    /// <summary>The Astrologian's Lady of Crowns parameter row (previously a fixed 0.6).</summary>
    public static readonly DefensiveRow LadyOfCrowns = Named("LadyOfCrownsPvP");

    /// <summary>The Astrologian's Microcosmos parameter row (previously a fixed 0.6).</summary>
    public static readonly DefensiveRow Microcosmos = Named("MicrocosmosPvP");

    /// <summary>The Ninja's Meisui parameter row (previously a fixed 0.5).</summary>
    public static readonly DefensiveRow Meisui = Named("MeisuiPvP");

    /// <summary>The Dark Knight's Impalement parameter row (previously a fixed 60).</summary>
    public static readonly DefensiveRow Impalement = Named("ImpalementPvP");

    private static DefensiveRow Named(string actionName) => Rows.Single(r => r.ActionName == actionName);

    private static readonly ConcurrentDictionary<string, DefensiveRow[]> ByJob = new();

    /// <summary>The row of an action id, or null.</summary>
    public static DefensiveRow? ById(uint actionId)
    {
        foreach (DefensiveRow row in Rows)
        {
            if (row.ActionId == actionId)
            {
                return row;
            }
        }

        return null;
    }

    /// <summary>"TANK" for Paladin, Warrior, Dark Knight and Gunbreaker, "HEALER" for the four healers, otherwise "".</summary>
    public static string RoleKeyOf(string jobKey) => jobKey switch
    {
        "PLD" or "WAR" or "DRK" or "GNB" => "TANK",
        "WHM" or "SCH" or "AST" or "SGE" => "HEALER",
        _ => string.Empty,
    };

    /// <summary>Whether a row is part of the table the job sees: its own rows, its role's rows and the shared rows.</summary>
    public static bool AppliesTo(DefensiveRow row, string jobKey) =>
        row.Job == "*" || row.Job == jobKey || (jobKey.Length > 0 && row.Job == RoleKeyOf(jobKey));

    /// <summary>The rows that apply to a job, in table (priority) order. The result is cached per job key.</summary>
    public static IReadOnlyList<DefensiveRow> ForJob(string jobKey) =>
        ByJob.GetOrAdd(jobKey, key => [.. Rows.Where(r => AppliesTo(r, key))]);

    /// <summary>The stored enabled flag, or the row's default when the setting was never touched.</summary>
    public static bool ResolveEnabled(DefensiveRow row, bool? stored) => stored ?? row.DefaultEnabled;

    /// <summary>
    ///     The stored HP ratio clamped into [MinPercent, 1], or the row's default when the setting was never touched
    ///     or holds NaN or infinity.
    /// </summary>
    public static float ResolvePercent(DefensiveRow row, float? stored)
    {
        if (stored is not { } value || !float.IsFinite(value))
        {
            return row.DefaultPercent;
        }

        return Math.Clamp(value, row.MinPercent, 1f);
    }

    /// <summary>
    ///     An HP ratio on the 0 to 100 scale, rounded to two decimals so that a setting of 0.3 reads as exactly 30
    ///     (0.3f * 100f is 30.000002f). Used where a fixed number was written on the 0 to 100 scale.
    /// </summary>
    public static float ToPercentPoints(float ratio) => MathF.Round(ratio * 100f, 2);
}
