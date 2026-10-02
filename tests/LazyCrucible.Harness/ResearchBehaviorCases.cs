using System.Text.RegularExpressions;
using Lalalazy.Crucible;

namespace LazyCrucible.Harness;

/// <summary>
///     Research to behaviour (task tasks-20261002-crucible-horn-picks-by-needed-abilities-01, amendment B): every item of the fight
///     guide (counters, bring lines, kill orders, mechanics, hits, unresolved questions) has exactly one row in
///     <c>ResearchBehavior.tsv</c> saying what the plugin does about it, with a status. This file is the single source of the
///     notebook table (Projects/BST Rebuild 2026-09/Crucible Research To Behavior). The checks below make the table impossible to
///     let rot: a guide item without a row, a row for an item that is gone or whose text changed, a status without its reason,
///     and an <c>implemented</c> row whose proof is not the title of a harness case that exists.
///     Columns (tab separated): key, fight, item text, status, behaviour, where, proof (cases joined by ' ;; '), log.
///     Status: implemented | differs (the plugin does something else on purpose, or the behaviour exists but is off by default) |
///     manual (not automated, with the reason) | unknown (the research is unresolved or unverified; the log that settles it) |
///     deferred (automatable, not built in this release; the owner task is named in the log column).
/// </summary>
internal static class ResearchBehaviorCases
{
    private static void Check(string what, bool ok, string? detail = null) => Program.Check(what, ok, detail);

    private sealed record Row(string Key, string Fight, string Text, string Status, string Behavior, string Where, string[] Proof, string Log);

    public static void Run()
    {
        Console.WriteLine("-- research to behaviour --");
        var guide = CrucibleGuide.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "CrucibleGuide.json")));
        GuideClaims(guide);
        Table(guide);
    }

    // ------------------------------------------------------------------ claims the guide makes about data the plugin already has

    private static void GuideClaims(CrucibleGuide guide)
    {
        // Every guide line that names an enemy's weakness agrees with the sheet (the picker scores that element).
        (int Board, int Battle, string Enemy, CrucibleWeakness Weak)[] weak =
        [
            (2, 5, "elder tablitaur", CrucibleWeakness.Fire), (3, 4, "catoblepas", CrucibleWeakness.Wind), (3, 5, "Lakhamu", CrucibleWeakness.Water),
            (3, 5, "golem", CrucibleWeakness.Water), (3, 6, "siren", CrucibleWeakness.Piercing), (3, 6, "shambling", CrucibleWeakness.Fire),
            (3, 0, "Thanatos", CrucibleWeakness.Lightning), (4, 1, "Strix", CrucibleWeakness.Piercing), (4, 3, "corpse flower", CrucibleWeakness.Wind),
            (4, 4, "ice dragon", CrucibleWeakness.Fire), (4, 4, "ice sprite", CrucibleWeakness.Fire), (4, 5, "gargoyle", CrucibleWeakness.None),
            (4, 6, "administrator", CrucibleWeakness.Lightning), (4, 7, "golem", CrucibleWeakness.Water), (4, 0, "Borgny", CrucibleWeakness.Earth),
            (4, 0, "toxic mass", CrucibleWeakness.Water), (5, 1, "Flauros", CrucibleWeakness.Earth), (5, 9, "sphinx", CrucibleWeakness.Slashing),
        ];
        var bad = new List<string>();
        foreach (var (board, battle, enemy, w) in weak)
        {
            var e = BST_CrucibleData.Enemies.FirstOrDefault(x => x.Board == board && x.Battle == battle && x.Name.Contains(enemy, StringComparison.OrdinalIgnoreCase));
            if (e.NameId == 0 || e.Weakness != w)
                bad.Add($"{board}:{battle} {enemy}: sheet {(e.NameId == 0 ? "missing" : e.Weakness)} vs guide {w}");
            else if (w != CrucibleWeakness.None && !BST_CrucibleData.BeastProfiles.Skip(1).Any(p => p.AutoElement == w))
                bad.Add($"{board}:{battle} {enemy}: no familiar scores {w}");
        }
        Check("guide weakness claims: every guide line that names an enemy weakness matches the sheet", bad.Count == 0, string.Join(" | ", bad.Take(5)));

        // Every resist item the guide names resists what the guide says (item table), and its fight lists that threat, so the shop / spoils policy values it.
        (string Item, CrucibleStatus Resists)[] items =
        [
            ("Crimson Ribbon", CrucibleStatus.Paralysis | CrucibleStatus.Blind | CrucibleStatus.Petrify), ("Genji Gloves", CrucibleStatus.Paralysis),
            ("Angel Robe", CrucibleStatus.Poison), ("Soulreaper's Armor", CrucibleStatus.Doom), ("Black Cowl", CrucibleStatus.Sleep),
            ("G1 Antipoison Soul Serum", CrucibleStatus.Poison), ("G1 Antiparalysis Soul Serum", CrucibleStatus.Paralysis),
            ("G1 Antiblind Soul Serum", CrucibleStatus.Blind), ("G1 Antipetrification Soul Serum", CrucibleStatus.Petrify),
            ("Potion of Breathtaking Swiftness", CrucibleStatus.Poison), ("Porcini Simular", CrucibleStatus.Poison), ("Morel Simular", CrucibleStatus.Paralysis),
        ];
        var itemBad = new List<string>();
        foreach (var (name, resists) in items)
        {
            var it = CrucibleItems.All.Skip(1).FirstOrDefault(i => i.Name == name);
            if (it.Name != name || (it.Resists & resists) != resists)
                itemBad.Add($"{name}: table {it.Resists} vs claimed {resists}");
        }
        (int Board, int Battle, string Threat)[] threats =
        [
            (1, 4, "poison"), (1, 0, "poison"), (1, 0, "sleep"), (2, 2, "doom"), (2, 6, "paralysis"), (2, 0, "blind"), (3, 4, "petrify"), (3, 5, "blind"),
            (3, 0, "paralysis"), (4, 2, "paralysis"), (4, 0, "poison"), (5, 1, "paralysis"), (5, 3, "blind"), (5, 3, "petrify"), (5, 13, "doom"), (5, 0, "paralysis"),
        ];
        foreach (var (board, battle, threat) in threats)
            if (!guide.Fight(board, battle)!.Threats.Contains(threat))
                itemBad.Add($"{board}:{battle} lacks the '{threat}' threat the item lines rely on");
        Check("guide item claims: every resist item the guide names resists what the guide says and its fight lists that threat", itemBad.Count == 0, string.Join(" | ", itemBad.Take(5)));
    }

    // ------------------------------------------------------------------ the table

    private static string? RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !(Directory.Exists(Path.Combine(dir.FullName, "tests")) && Directory.Exists(Path.Combine(dir.FullName, "src"))))
            dir = dir.Parent;
        return dir?.FullName;
    }

    private static readonly Regex Literal = new("\"((?:[^\"\\\\]|\\\\.)*)\"", RegexOptions.Compiled);

    private static HashSet<string> HarnessStrings(string root)
    {
        var all = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(Path.Combine(root, "tests"), "*.cs", SearchOption.AllDirectories)
                     .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")))
            foreach (Match m in Literal.Matches(File.ReadAllText(file)))
                all.Add(m.Groups[1].Value.Replace("\\\"", "\"").Replace("\\\\", "\\"));
        return all;
    }

    private static List<Row> Read()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "ResearchBehavior.tsv");
        var rows = new List<Row>();
        foreach (var line in File.ReadAllLines(path).Skip(1).Where(l => l.Length > 0))
        {
            var c = line.Split('\t');
            rows.Add(new Row(c[0], c[1], c[2], c[3], c[4], c[5], c[6].Length == 0 ? [] : c[6].Split(" ;; "), c.Length > 7 ? c[7] : ""));
        }
        return rows;
    }

    /// <summary> Every guide item as (key, text): counters, bring, killOrder, mechanics, hits, unknown. </summary>
    private static List<(string Key, string Text)> GuideItems(CrucibleGuide guide)
    {
        var list = new List<(string, string)>();
        foreach (var f in guide.File.Fights)
        {
            var fk = $"{f.Board}.{f.Battle}";
            for (var i = 0; i < f.Counters.Count; i++) list.Add(($"{fk}/c{i}", $"{f.Counters[i].Need}: {f.Counters[i].What}"));
            for (var i = 0; i < f.Bring.Count; i++) list.Add(($"{fk}/b{i}", f.Bring[i].Text));
            if (f.KillOrder is { } ko) list.Add(($"{fk}/k0", ko.Text));
            for (var i = 0; i < f.Mechanics.Count; i++) list.Add(($"{fk}/m{i}", $"{f.Mechanics[i].Name} | {f.Mechanics[i].Tell} | {f.Mechanics[i].Do}"));
            for (var i = 0; i < f.Hits.Count; i++) list.Add(($"{fk}/h{i}", $"{f.Hits[i].Name} | {f.Hits[i].Tell} | {f.Hits[i].Do}"));
            for (var i = 0; i < f.Unknown.Count; i++) list.Add(($"{fk}/u{i}", f.Unknown[i]));
        }
        return list;
    }

    private static void Table(CrucibleGuide guide)
    {
        var rows = Read();
        var items = GuideItems(guide);
        var byKey = rows.GroupBy(r => r.Key).ToDictionary(g => g.Key, g => g.ToList());

        Check($"research table: every one of the guide's {items.Count} items (counters, bring, kill orders, mechanics, hits, unknowns) has exactly one row",
            items.All(i => byKey.TryGetValue(i.Key, out var l) && l.Count == 1) && rows.Count == items.Count,
            $"rows {rows.Count}, items {items.Count}, missing {string.Join(",", items.Where(i => !byKey.ContainsKey(i.Key)).Select(i => i.Key).Take(5))}, extra {string.Join(",", rows.Where(r => items.All(i => i.Key != r.Key)).Select(r => r.Key).Take(5))}");
        var stale = items.Where(i => byKey.TryGetValue(i.Key, out var l) && l[0].Text != i.Text).Select(i => i.Key).ToList();
        Check("research table: every row carries the guide's current text for its item (a changed guide line needs its row re-read)", stale.Count == 0, string.Join(",", stale.Take(5)));
        var statuses = new[] { "implemented", "differs", "manual", "unknown", "deferred" };
        Check("research table: every status is one of implemented / differs / manual / unknown / deferred and every row says what the plugin does",
            rows.All(r => statuses.Contains(r.Status) && r.Behavior.Length > 0 && r.Where.Length > 0));
        Check("research table: every manual row says 'because' (the reason it is not automated)",
            rows.Where(r => r.Status == "manual").All(r => r.Behavior.StartsWith("manual because", StringComparison.Ordinal) || r.Behavior.Contains("because", StringComparison.Ordinal)),
            string.Join(",", rows.Where(r => r.Status == "manual" && !r.Behavior.Contains("because", StringComparison.Ordinal)).Select(r => r.Key).Take(5)));
        Check("research table: every unknown and deferred row names the log or run that settles it, and every deferred row names its owner task",
            rows.Where(r => r.Status is "unknown" or "deferred").All(r => r.Log.Contains("settled by", StringComparison.Ordinal))
            && rows.Where(r => r.Status == "deferred").All(r => r.Log.Contains("task: tasks-", StringComparison.Ordinal)),
            string.Join(",", rows.Where(r => r.Status is "unknown" or "deferred" && !r.Log.Contains("settled by", StringComparison.Ordinal)).Select(r => r.Key).Take(5)));

        var root = RepoRoot();
        if (root is null)
        {
            Check("research table: harness sources found for the proof check", false, "no repo root above " + AppContext.BaseDirectory);
            return;
        }
        var strings = HarnessStrings(root);
        var missing = new List<string>();
        foreach (var r in rows.Where(r => r.Status == "implemented"))
        {
            if (r.Proof.Length == 0)
                missing.Add($"{r.Key}: no proof");
            foreach (var p in r.Proof)
                if (!strings.Any(s => s.Contains(p, StringComparison.Ordinal)))
                    missing.Add($"{r.Key}: '{p[..Math.Min(60, p.Length)]}' is not a harness case");
        }
        Check($"research table: every implemented row ({rows.Count(r => r.Status == "implemented")}) names at least one harness case that exists", missing.Count == 0, string.Join(" | ", missing.Take(6)));
        Console.WriteLine($"   research table: {string.Join(", ", statuses.OrderBy(x => x, StringComparer.Ordinal).Select(st => $"{st} {rows.Count(r => r.Status == st)}"))}");
    }
}
