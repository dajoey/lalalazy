using System.Text.Json;
using Lalalazy.Crucible;

namespace LazyCrucible.Harness;

/// <summary>
///     Horn picks are chosen by the abilities the fight needs (interrupt, dispel, cleanse), not by a point score
///     (task tasks-20261002-crucible-horn-picks-by-needed-abilities-01). The expectations here come from an ORACLE
///     that reads the guide JSON and the panel data on its own: what a fight REQUIRES is stated by the tier rule,
///     never by asking the picker. <see cref="Under"/> is the one function the picks come from.
/// </summary>
internal static class NeedFirstCases
{
    private static void Check(string what, bool ok, string? detail = null) => Program.Check(what, ok, detail);

    private static readonly CrucibleNeeds[] Kinds = [CrucibleNeeds.Interrupt, CrucibleNeeds.Dispel, CrucibleNeeds.Cleanse];

    /// <summary> The horn selection under test: candidate familiars with HP% (absent = full), the three horn rows in pick order. </summary>
    private static List<int> Under(int board, int battle, IReadOnlyList<int> candidates, IReadOnlyDictionary<int, int> hp, int slots = 3) =>
        BST_CrucibleAdvisor.PickSlots(board, battle, candidates, hp, slots).Select(p => p.Row).ToList();

    // ------------------------------------------------------------------ the oracle

    /// <summary>
    ///     Required abilities per fight, independent of the production code: the panel's needs, plus guide counters that
    ///     cite the panel or are marked mandatory, plus counters two or more distinct sources agree on that nobody disputes.
    /// </summary>
    private static Dictionary<(int, int), CrucibleNeeds> RequiredByFight()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "CrucibleGuide.json");
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var result = new Dictionary<(int, int), CrucibleNeeds>();
        foreach (var f in doc.RootElement.GetProperty("fights").EnumerateArray())
        {
            var key = (f.GetProperty("board").GetInt32(), f.GetProperty("battle").GetInt32());
            var required = BST_CrucibleData.BattleNeeds(key.Item1, key.Item2);
            foreach (var c in f.GetProperty("counters").EnumerateArray())
            {
                var kind = c.GetProperty("need").GetString() switch
                {
                    "interrupt" => CrucibleNeeds.Interrupt,
                    "dispel" => CrucibleNeeds.Dispel,
                    "cleanse" => CrucibleNeeds.Cleanse,
                    _ => CrucibleNeeds.None,
                };
                var src = c.GetProperty("src").EnumerateArray().Select(s => s.GetString()!.Split(':')[0]).ToList();
                var mandatory = c.TryGetProperty("mandatory", out var m) && m.GetBoolean();
                var disputed = c.TryGetProperty("disputed", out var d) && d.GetBoolean();
                if (src.Contains("panel") || mandatory || (!disputed && src.Distinct().Count() >= 2))
                    required |= kind;
            }
            result[key] = required;
        }
        return result;
    }

    // ------------------------------------------------------------------ fixtures

    private static int[] RowsOfKin(BeastmasterKinType kin) =>
        Enumerable.Range(1, BST_Beasts.Count).Where(r => BST_Beasts.All[r].Kin == kin).ToArray();

    private static bool Answers(int row, CrucibleNeeds kind) => (BST_CrucibleAdvisor.Answers(row) & kind) != 0;

    public static void Run()
    {
        Console.WriteLine("-- need-first horn picks --");
        var required = RequiredByFight();
        var allRows = Enumerable.Range(1, BST_Beasts.Count).ToList();
        var fullHp = new Dictionary<int, int>();
        var soulkin = RowsOfKin(BeastmasterKinType.Soulkin);
        var wavekin = RowsOfKin(BeastmasterKinType.Wavekin);

        // The screenshot: board 4, Strix Piece (piercing weakness). The guide listed an interrupt (Aero III) the picker never saw.
        var strix = Under(4, 1, allRows, fullHp);
        Check("screenshot fight (board 4 Strix Piece), every familiar captured: a Soulkin AND a dispeller are picked",
            strix.Any(r => Answers(r, CrucibleNeeds.Interrupt)) && strix.Any(r => Answers(r, CrucibleNeeds.Dispel)),
            string.Join(",", strix.Select(r => BST_Beasts.All[r].Name)));

        // The same fight with a realistic small roster: one Soulkin, one Wavekin and three piercing hitters (the weakness).
        var piercing = allRows.Where(r => BST_CrucibleData.BeastProfiles[r].AutoElement == CrucibleWeakness.Piercing
                                          && BST_CrucibleAdvisor.Answers(r) == CrucibleNeeds.None).Take(3).ToList();
        var roster = new List<int> { soulkin[0], wavekin[0] };
        roster.AddRange(piercing);
        var small = Under(4, 1, roster, fullHp);
        Check("screenshot fight, roster of one Soulkin, one Wavekin and three piercing hitters: Soulkin and Wavekin both picked",
            small.Contains(soulkin[0]) && small.Contains(wavekin[0]),
            string.Join(",", small.Select(r => BST_Beasts.All[r].Name)));

        // A weakness match never displaces the only familiar covering a Required need: bone knight + bishop (blunt weakness,
        // all three abilities required) with three strong blunt hitters beside one Soulkin, one Wavekin, one Ashkin.
        var blunt = allRows.Where(r => BST_CrucibleData.BeastProfiles[r].AutoElement == CrucibleWeakness.Blunt
                                       && BST_CrucibleAdvisor.Answers(r) == CrucibleNeeds.None).Take(3).ToList();
        var ashkin = RowsOfKin(BeastmasterKinType.Ashkin);
        var bone = new List<int> { soulkin[0], wavekin[0], ashkin[0] };
        bone.AddRange(blunt);
        var boneKnight = Under(1, 1, bone, fullHp);
        Check("bone knight + bishop: three blunt hitters never displace the only Soulkin, Wavekin and Ashkin (all three required)",
            boneKnight.Contains(soulkin[0]) && boneKnight.Contains(wavekin[0]) && boneKnight.Contains(ashkin[0]),
            string.Join(",", boneKnight.Select(r => BST_Beasts.All[r].Name)));

        // Every battle on every board, three rosters: no Required need is left uncovered when the roster could cover it.
        var rosters = new Dictionary<string, List<int>>
        {
            ["every familiar"] = allRows,
            ["level 30"] = allRows.Where(r => BST_Beasts.All[r].CaptureLevel <= 30).ToList(),
            ["hitters plus one of each answer"] = allRows.Where(r => BST_CrucibleAdvisor.Answers(r) == CrucibleNeeds.None).Take(7)
                .Concat([soulkin[0], wavekin[0], ashkin[0]]).ToList(),
        };
        foreach (var (name, rows) in rosters)
        {
            var misses = new List<string>();
            var fights = 0;
            foreach (var b in BST_CrucibleData.Battles)
            {
                fights++;
                var picks = Under(b.Board, b.Battle, rows, fullHp);
                foreach (var kind in Kinds)
                {
                    if ((required[(b.Board, b.Battle)] & kind) == 0 || !rows.Any(r => Answers(r, kind)))
                        continue;
                    if (!picks.Any(r => Answers(r, kind)))
                        misses.Add($"{b.Board}:{b.Battle} {kind}");
                }
            }
            Check($"every battle ({fights}), roster '{name}': every Required need the roster can cover is covered",
                misses.Count == 0, $"{misses.Count} uncovered: {string.Join(" | ", misses.Take(8))}");
        }

        // Knocked-out and hurt familiars behave as before.
        var dead = new Dictionary<int, int> { [soulkin[0]] = 0 };
        var withoutSoulkin = Under(1, 1, bone, dead);
        Check("a knocked-out Soulkin is never picked", !withoutSoulkin.Contains(soulkin[0]));
        var hurt = new Dictionary<int, int> { [soulkin[0]] = 15 };
        var twoSoulkins = Under(1, 1, [soulkin[0], soulkin[1], wavekin[0], ashkin[0]], hurt);
        Check("a badly hurt Soulkin loses the interrupt slot to a healthy one",
            twoSoulkins.Contains(soulkin[1]) && !twoSoulkins.Contains(soulkin[0]),
            string.Join(",", twoSoulkins.Select(r => BST_Beasts.All[r].Name)));
        Check("same inputs, same picks in the same order",
            Under(1, 1, allRows, fullHp).SequenceEqual(Under(1, 1, allRows, fullHp)));
    }
}
