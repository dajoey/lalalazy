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
        BST_CrucibleNeedFirst.Select(board, battle, candidates, hp, slots, "in the roster").Picks.Select(p => p.Row).ToList();

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
        var guide = CrucibleGuide.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "CrucibleGuide.json")));
        guide.InstallNeedModel();
        try
        {
            RunCases(guide);
            NewBehaviour(guide);
        }
        finally
        {
            CrucibleNeedModel.Extras = null;
        }
    }

    private static void RunCases(CrucibleGuide guide)
    {
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
    private static CrucibleSelection Sel(int board, int battle, IReadOnlyList<int> candidates, IReadOnlyDictionary<int, int>? hp = null, int slots = 3) =>
        BST_CrucibleNeedFirst.Select(board, battle, candidates, hp ?? new Dictionary<int, int>(), slots, "in the roster");

    private static void NewBehaviour(CrucibleGuide guide)
    {
        var allRows = Enumerable.Range(1, BST_Beasts.Count).ToList();
        var soulkin = RowsOfKin(BeastmasterKinType.Soulkin);
        var wavekin = RowsOfKin(BeastmasterKinType.Wavekin);
        var ashkin = RowsOfKin(BeastmasterKinType.Ashkin);
        var hitters = allRows.Where(r => BST_CrucibleAdvisor.Answers(r) == CrucibleNeeds.None).ToList();

        // ---- the tier rule
        GuideCounter C(string need, params string[] src) => new() { Need = need, What = "x", Src = [.. src] };
        Check("tier: a counter citing the game panel is Required", C("dispel", "panel").Tier == CrucibleNeedTier.Required);
        Check("tier: a single-source counter is Useful", C("interrupt", "AS").Tier == CrucibleNeedTier.Useful);
        Check("tier: two agreeing sources make it Required", C("cleanse", "AS", "NT").Tier == CrucibleNeedTier.Required);
        Check("tier: two sources that are the same source do not", C("cleanse", "AS", "AS:2").Tier == CrucibleNeedTier.Useful);
        var disputed = C("dispel", "AS", "NT");
        disputed.Disputed = true;
        Check("tier: a disputed counter stays Useful whatever the source count", disputed.Tier == CrucibleNeedTier.Useful);
        var mandatory = C("interrupt", "AS");
        mandatory.Mandatory = true;
        Check("tier: a counter the guide marks mandatory is Required", mandatory.Tier == CrucibleNeedTier.Required);

        // ---- the guide's data is the model's data: the guide window and the picker cannot see different needs
        var problems = guide.Validate();
        Check("guide validates with the dispute rules (disputed never claims the panel; 'panel says' counters are marked)", problems.Count == 0, string.Join(" | ", problems.Take(4)));
        var mismatch = new List<string>();
        foreach (var b in BST_CrucibleData.Battles)
        {
            var model = CrucibleNeedModel.For(b.Board, b.Battle);
            var fromGuide = guide.NeedsOf(b.Board, b.Battle);
            foreach (var g in fromGuide)
                if (!model.Items.Any(i => i.Kind == g.Kind && i.Tier == g.Tier && i.What == g.What))
                    mismatch.Add($"{b.Board}:{b.Battle} guide '{g.What}' missing from the model");
            var panel = BST_CrucibleData.BattleNeeds(b.Board, b.Battle);
            if ((model.Required & panel) != panel)
                mismatch.Add($"{b.Board}:{b.Battle} panel needs {panel} not all Required");
            var rows = GuideNeeds.Rows(Sel(b.Board, b.Battle, allRows));
            if (rows.Count != model.Items.Count || rows.Any(r => !model.Items.Any(i => i.Kind == r.Kind && i.Tier == r.Tier && i.What == r.What)))
                mismatch.Add($"{b.Board}:{b.Battle} guide rows differ from the model items");
            if (guide.Fight(b.Board, b.Battle)!.Counters.Any(c => c.AsNeed != CrucibleNeeds.None && !model.Items.Any(i => i.What == c.What)))
                mismatch.Add($"{b.Board}:{b.Battle} a guide counter the picker never sees");
        }
        Check("every battle: the picker's need list holds every guide counter and every panel need; the guide rows ARE that list",
            mismatch.Count == 0, string.Join(" | ", mismatch.Take(5)));

        // ---- the screenshot fight shows no warning when covered, and says why when not
        var strixCovered = Sel(4, 1, [wavekin[0], soulkin[0], .. hitters.Take(3)]);
        var coveredRows = GuideNeeds.Rows(strixCovered);
        Check("screenshot fight, Wavekin and Soulkin in the roster: both picked and every need row covered, no '!'",
            strixCovered.Picks.Select(p => p.Row).Contains(soulkin[0]) && strixCovered.Picks.Select(p => p.Row).Contains(wavekin[0])
            && coveredRows.Count == 2 && coveredRows.All(r => r.Covered && !r.Warn),
            string.Join(" | ", coveredRows.Select(r => $"{r.Kind}:{r.Tier}:{r.CoveredBy}:{r.WhyNot}")));
        var strixNoSoul = GuideNeeds.Rows(Sel(4, 1, [wavekin[0], .. hitters.Take(4)]));
        var open = strixNoSoul.FirstOrDefault(r => r.Kind == CrucibleNeeds.Interrupt);
        Check("screenshot fight, no Soulkin: the interrupt row says why, and as a Useful need it is not a warning",
            open is { Covered: false, Warn: false } && open.WhyNot == "no Soulkin in the roster" && strixNoSoul.First(r => r.Kind == CrucibleNeeds.Dispel).Covered,
            string.Join(" | ", strixNoSoul.Select(r => $"{r.Kind}:{r.Tier}:{r.CoveredBy}:{r.WhyNot}")));

        // ---- a Required need that cannot be covered is a warning with a named reason
        var boneNoSoul = GuideNeeds.Rows(Sel(1, 1, [wavekin[0], ashkin[0], .. hitters.Take(3)]));
        var warn = boneNoSoul.FirstOrDefault(r => r.Warn);
        Check("bone knight, no Soulkin in the roster: the interrupt is a '!' with the reason 'no Soulkin in the roster'",
            warn is { Kind: CrucibleNeeds.Interrupt } && warn.WhyNot == "no Soulkin in the roster" && boneNoSoul.Count(r => r.Warn) == 1,
            string.Join(" | ", boneNoSoul.Select(r => $"{r.Kind}:{r.Tier}:{r.CoveredBy}:{r.WhyNot}")));
        var boneDead = GuideNeeds.Rows(Sel(1, 1, [soulkin[0], wavekin[0], ashkin[0]], new Dictionary<int, int> { [soulkin[0]] = 0 }));
        Check("bone knight, the only Soulkin knocked out: the reason says it is knocked out",
            boneDead.First(r => r.Kind == CrucibleNeeds.Interrupt) is { Warn: true, WhyNot: "the Soulkin is knocked out" },
            string.Join(" | ", boneDead.Select(r => $"{r.Kind}:{r.WhyNot}")));
        var boneOutranked = GuideNeeds.Rows(Sel(1, 1, [soulkin[0], wavekin[0], ashkin[0]], slots: 2));
        Check("bone knight with only two horn slots: the need left over says the slots went to other needs",
            boneOutranked.Count(r => r.Warn) == 1 && boneOutranked.First(r => r.Warn).WhyNot!.Contains("horn slots went to other needs"),
            string.Join(" | ", boneOutranked.Select(r => $"{r.Kind}:{r.WhyNot}")));

        // ---- weakness and points only break ties
        var strongBlunt = hitters.Where(r => BST_CrucibleData.BeastProfiles[r].AutoElement == CrucibleWeakness.Blunt).Take(3).ToList();
        var oneSoulkin = Sel(1, 1, [soulkin[0], wavekin[0], ashkin[0], .. strongBlunt]);
        Check("weakness never outranks a Required need: blunt hitters (weakness 12 on the knight) cannot push out a Soulkin, Wavekin or Ashkin",
            new[] { soulkin[0], wavekin[0], ashkin[0] }.All(r => oneSoulkin.Picks.Any(p => p.Row == r)) && oneSoulkin.Needs.All(n => n.Covered));
        var twoWave = Sel(1, 1, [soulkin[0], wavekin[0], wavekin[1], ashkin[0]]);
        Check("two familiars cover the same need: the points choose between them, the other need-covering picks are untouched",
            twoWave.Picks.Count == 3 && twoWave.Picks.Count(p => Answers(p.Row, CrucibleNeeds.Dispel)) >= 1
            && twoWave.Picks.Any(p => p.Row == soulkin[0]) && twoWave.Picks.Any(p => p.Row == ashkin[0]));
        var forward = Sel(2, 0, allRows).Picks.Select(p => p.Row).ToList();
        var reversed = Sel(2, 0, Enumerable.Reverse(allRows).ToList()).Picks.Select(p => p.Row).ToList();
        Check("ties break deterministically: candidate order never changes the picks", forward.SequenceEqual(reversed), $"{string.Join(",", forward)} vs {string.Join(",", reversed)}");

        // ---- HP and knocked-out rules
        var onlyHurt = Sel(1, 1, [soulkin[0], wavekin[0], ashkin[0]], new Dictionary<int, int> { [soulkin[0]] = 15 });
        Check("the only Soulkin at 15% HP is still picked for the Required interrupt (and the pick says its HP)",
            onlyHurt.Picks.Any(p => p.Row == soulkin[0] && p.Why.Contains("HP 15%")));

        // ---- the log carries the coverage
        var log = GuideNeeds.Log(Sel(4, 1, [wavekin[0], .. hitters.Take(4)]));
        Check("telemetry: PS| need coverage names kind, tier and the covering row or the reason",
            log == $"D:R:{wavekin[0]};I:U:miss=no Soulkin in the roster", log);

        // ---- no ability need at all: the points choose, and picks stay valid
        var arch = Sel(1, 2, allRows);
        Check("a fight with no ability needs still gets three picks and no need rows", arch.Picks.Count == 3 && arch.Needs.Count == 0);

        // ---- board known, fight not: the same rule over the board's fights
        var cov = BST_CrucibleNeedFirst.SelectCoverage(4, allRows, new Dictionary<int, int>());
        Check("coverage (fight unidentified, board 4): interrupt, dispel and cleanse are each covered, every Why carries the coverage tag",
            cov.Count == 3 && cov.Any(p => Answers(p.Row, CrucibleNeeds.Interrupt)) && cov.Any(p => Answers(p.Row, CrucibleNeeds.Dispel))
            && cov.Any(p => Answers(p.Row, CrucibleNeeds.Cleanse))
            && cov.All(p => p.Why.StartsWith("coverage board 4, battle unidentified", StringComparison.Ordinal)),
            string.Join(" | ", cov.Select(p => $"{BST_Beasts.All[p.Row].Name}: {p.Why}")));

        // ---- worth capturing: a Soulkin when the fight needs one and none is captured
        var captured = new HashSet<int>([wavekin[0], .. hitters.Take(4)]);
        var sel = BST_CrucibleNeedFirst.SelectCaptured(1, 1, captured.Contains);
        var worth = BST_CrucibleNeedFirst.WorthCapturing(1, 1, captured.Contains, sel);
        Check("worth capturing: a fight with an uncovered Required interrupt lists a capturable Soulkin first",
            worth.Count > 0 && Answers(worth[0].Row, CrucibleNeeds.Interrupt) && !captured.Contains(worth[0].Row),
            string.Join(" | ", worth.Select(w => $"{BST_Beasts.All[w.Row].Name}: {w.Why}")));

        foreach (var b in BST_CrucibleData.Battles)
        {
            var all = Sel(b.Board, b.Battle, allRows);
            Console.WriteLine($"   B{b.Board}:{b.Battle,-2} {BST_CrucibleData.BattleLabel(b.Board, b.Battle),-22} {string.Join(" | ", all.Picks.Select(p => $"{BST_Beasts.All[p.Row].Name} ({p.Why})"))}   needs {GuideNeeds.Log(all)}");
        }

        // ---- without the guide the model is the panel alone
        CrucibleNeedModel.Extras = null;
        var panelOnly = CrucibleNeedModel.For(4, 1);
        Check("no guide installed: the model is the game panel alone (Strix: dispel only)",
            panelOnly.Required == CrucibleNeeds.Dispel && panelOnly.Useful == CrucibleNeeds.None);
        guide.InstallNeedModel();
        Check("guide installed: the same fight also carries the Useful interrupt",
            CrucibleNeedModel.For(4, 1) is { Required: CrucibleNeeds.Dispel, Useful: CrucibleNeeds.Interrupt });
    }
}
