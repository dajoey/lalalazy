using System.Text.Json;
using Lalalazy.Crucible;

namespace LazyCrucible.Policy;

/// <summary> The Crucible selection surfaces LazyCrucible can act on, as one flag each. </summary>
[Flags]
public enum CrucibleSurface : ushort
{
    None = 0,
    /// <summary> The run roster on the Bentbranch Meadows entry menu. </summary>
    Roster = 1 << 0,
    /// <summary> The three Battlehorn slots before each fight. </summary>
    Horns = 1 << 1,
    /// <summary> The board shop. </summary>
    Shop = 1 << 2,
    /// <summary> The Beast Feed picker a feed purchase opens. </summary>
    Feed = 1 << 3,
    /// <summary> The treasure coffer. </summary>
    Treasure = 1 << 4,
    /// <summary> The spoils after a battle. </summary>
    Spoils = 1 << 5,
    /// <summary> The campsite's rest picker. </summary>
    Camp = 1 << 6,
    All = Roster | Horns | Shop | Feed | Treasure | Spoils | Camp,
}

/// <summary> AutoDuty's own Crucible settings for the profile AutoDuty uses (read from its config file, never written). </summary>
public sealed record AutoDutyCrucibleConfig(
    string Profile,
    string TeamMode,
    string LevelingMode,
    string Degree,
    bool FightPicks,
    bool Loot,
    bool Treasure,
    bool Shop,
    bool Rest);

/// <summary>
///     Working WITH AutoDuty. PURE. AutoDuty picks its own familiars when it is set to; LazyCrucible picks when it is not; when
///     AutoDuty picks, the suite makes the best of the team it chose. So LazyCrucible stands by on exactly the surfaces AutoDuty
///     drives and acts as usual on the rest. What AutoDuty drives comes from its own Crucible settings
///     (<c>profileData[..].Config.Meta.Crucible</c> in <c>pluginConfigs/AutoDuty/AutoDutyConfigV2.json</c>, read-only; AutoDuty has
///     no IPC for them), matched to its menu code: the team is built in every team mode, the battlehorns only with Fight picks on
///     (with it off AutoDuty neither picks nor commences), and the shop (with the feed picker it opens), treasure, spoils and the
///     campsite each only with their own toggle on. An unreadable config means every surface stands by, as before. The
///     need-coverage corrections that once wrote on top of AutoDuty's picks are retired: they reached the fight in 0 of 40
///     AutoDuty-driven fights, because AutoDuty re-asserted its own picks every time.
/// </summary>
internal static class AutoDutyCrucible
{
    /// <summary> AutoDuty's CrucibleTeamMode values in order (Recommended = 0). </summary>
    private static readonly string[] TeamModes = ["Recommended", "Leveling", "Custom"];

    /// <summary>
    ///     The Crucible settings of the profile AutoDuty uses for this character: the profile whose <c>CIDs</c> list the character,
    ///     else the one named by <c>DefaultConfigName</c>, else AutoDuty's bare defaults. Null when the text is not AutoDuty's config
    ///     (empty, truncated, no <c>profileData</c>): the caller then shows nothing and stands by everywhere.
    /// </summary>
    public static AutoDutyCrucibleConfig? Parse(string json, ulong contentId)
    {
        try
        {
            using var doc = JsonDocument.Parse(json.TrimStart('﻿'),
                new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("profileData", out var profiles)
                || profiles.ValueKind != JsonValueKind.Array)
                return null;

            var defaultName = root.TryGetProperty("DefaultConfigName", out var dn) && dn.ValueKind == JsonValueKind.String ? dn.GetString() : null;
            JsonElement? byCid = null, byDefault = null;
            foreach (var p in profiles.EnumerateArray())
            {
                if (p.ValueKind != JsonValueKind.Object)
                    continue;
                if (byCid is null && p.TryGetProperty("CIDs", out var cids) && cids.ValueKind == JsonValueKind.Array)
                    foreach (var c in cids.EnumerateArray())
                        if (c.ValueKind == JsonValueKind.Number && c.TryGetUInt64(out var cid) && cid == contentId && contentId != 0)
                            byCid = p;
                if (byDefault is null && defaultName is not null && Str(p, "Name") == defaultName)
                    byDefault = p;
            }

            if ((byCid ?? byDefault) is not { } chosen)
                return new("(defaults)", "Recommended", "", "", true, true, true, true, true);

            var crucible = chosen.TryGetProperty("Config", out var cfg) && cfg.ValueKind == JsonValueKind.Object
                           && cfg.TryGetProperty("Meta", out var meta) && meta.ValueKind == JsonValueKind.Object
                           && meta.TryGetProperty("Crucible", out var cru) && cru.ValueKind == JsonValueKind.Object
                ? cru
                : default;
            return new(
                Str(chosen, "Name") ?? "",
                Enum(crucible, "TeamMode", TeamModes, "Recommended"),
                Enum(crucible, "LevelingMode", [], ""),
                Enum(crucible, "Degree", [], ""),
                Flag(crucible, "FightPicks"),
                Flag(crucible, "Loot"),
                Flag(crucible, "Treasure"),
                Flag(crucible, "Shop"),
                Flag(crucible, "Rest"));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    /// <summary> An enum written as its name or as its number (named from <paramref name="names"/> when known). </summary>
    private static string Enum(JsonElement e, string name, string[] names, string missing)
    {
        if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(name, out var v))
            return missing;
        if (v.ValueKind == JsonValueKind.String)
            return v.GetString() ?? missing;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n))
            return n >= 0 && n < names.Length ? names[n] : n.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return missing;
    }

    /// <summary> A toggle; missing means AutoDuty's default, which is on for every Crucible toggle. </summary>
    private static bool Flag(JsonElement e, string name) =>
        e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(name, out var v) || v.ValueKind != JsonValueKind.False;

    /// <summary> The surfaces AutoDuty drives while it runs a board with these settings (all of them when the settings are unknown). </summary>
    public static CrucibleSurface Driven(AutoDutyCrucibleConfig? config)
    {
        if (config is null)
            return CrucibleSurface.All;
        var driven = CrucibleSurface.Roster; // AutoDuty builds the team in every team mode
        if (config.FightPicks)
            driven |= CrucibleSurface.Horns;
        if (config.Shop)
            driven |= CrucibleSurface.Shop | CrucibleSurface.Feed;
        if (config.Treasure)
            driven |= CrucibleSurface.Treasure;
        if (config.Loot)
            driven |= CrucibleSurface.Spoils;
        if (config.Rest)
            driven |= CrucibleSurface.Camp;
        return driven;
    }

    /// <summary> Whether LazyCrucible stands by on <paramref name="surface"/>: only while AutoDuty runs and drives that surface. </summary>
    public static bool Yields(bool autoDutyDriving, CrucibleSurface driven, CrucibleSurface surface) =>
        autoDutyDriving && (driven & surface) != 0;

    private static readonly (CrucibleSurface Surface, string Name)[] Names =
    [
        (CrucibleSurface.Roster, "the run roster"),
        (CrucibleSurface.Horns, "the battlehorns"),
        (CrucibleSurface.Shop | CrucibleSurface.Feed, "the shop and feeding"),
        (CrucibleSurface.Treasure, "treasure"),
        (CrucibleSurface.Spoils, "spoils"),
        (CrucibleSurface.Camp, "the campsite"),
    ];

    private static string List(IReadOnlyList<string> items) =>
        items.Count <= 1 ? string.Join("", items) : $"{string.Join(", ", items.Take(items.Count - 1))} and {items[^1]}";

    /// <summary> The one quiet status line for the window while AutoDuty runs; empty when it drives nothing. </summary>
    public static string StandingBy(CrucibleSurface driven)
    {
        if (driven == CrucibleSurface.None)
            return "";
        var by = Names.Where(n => (driven & n.Surface) != 0).Select(n => n.Name).ToList();
        var mine = Names.Where(n => (driven & n.Surface) == 0).Select(n => n.Name).ToList();
        var line = $"AutoDuty is choosing familiars for this run; LazyCrucible is standing by on {List(by)}";
        return mine.Count == 0 ? line + "." : $"{line}, and handles {List(mine)}.";
    }

    /// <summary>
    ///     The needs of a fight the team cannot answer, for the per-fight <c>PS|..|uncovered=</c> grade: the needs the rotation
    ///     answers live (Required, plus Useful ones except a dispel the enemy panel disputes), as <c>I</c>/<c>D</c>/<c>C</c>
    ///     (interrupt, dispel, cleanse) in that order, joined with ','; empty when the team answers every one. A knocked-out
    ///     familiar (HP 0 in <paramref name="hpByRow"/>) answers nothing.
    /// </summary>
    public static string Uncovered(int board, int battle, IReadOnlyList<int> team, IReadOnlyDictionary<int, int>? hpByRow = null)
    {
        var model = CrucibleNeedModel.For(board, battle);
        var needs = model.Required | (model.Useful & ~CrucibleNeeds.Dispel);
        var answered = CrucibleNeeds.None;
        foreach (var row in team)
            if (row is >= 1 and <= BST_Beasts.Count && (hpByRow is null || !hpByRow.TryGetValue(row, out var hp) || hp > 0))
                answered |= BST_CrucibleAdvisor.Answers(row);
        var open = needs & ~answered;
        var letters = new List<string>(3);
        foreach (var kind in BST_CrucibleNeedFirst.Kinds)
            if ((open & kind) != 0)
                letters.Add(kind == CrucibleNeeds.Interrupt ? "I" : kind == CrucibleNeeds.Dispel ? "D" : "C");
        return string.Join(",", letters);
    }
}
