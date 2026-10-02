using System.Text.Json;
using System.Text.Json.Serialization;
using Lalalazy.Crucible;

namespace CruciblePlanner.Golden;

/// <summary>
///     Dumps the shared Crucible advisor's answers for fixed rosters and HP scenarios so the web port
///     (docs/crucible/advisor.js) can be proven identical. Deterministic: no randomness, no game reads.
///     Two parts: the point score (<see cref="BST_CrucibleAdvisor.Score"/>, still the tie-break inside the picker)
///     and the need-first horn picks (<see cref="BST_CrucibleNeedFirst"/> over <see cref="CrucibleNeedModel"/>, with the
///     LazyCrucible guide installed so Required / Useful tiers are the ones the plugin uses).
/// </summary>
internal static class Program
{
    private sealed record PickOut(int Row, int Score, bool Captured, string Why);

    private sealed record NeedOut(int Kind, int Tier, string What, string[] Src, int Row, string? WhyNot);

    private static PickOut Out(CrucibleBeastPick p) => new(p.Row, p.Score, p.Captured, p.Why);

    private static object SelectionOut(CrucibleSelection sel) => new
    {
        picks = sel.Picks.Select(Out).ToList(),
        needs = sel.Needs.Select(n => new NeedOut((int)n.Need.Kind, (int)n.Need.Tier, n.Need.What, [.. n.Need.Src], n.Row, n.WhyNot)).ToList(),
    };

    private static int Main(string[] args)
    {
        var outPath = args.Length > 0 ? args[0] : "golden.json";

        // The plugin installs the guide's tiered counters as the need model's extras; the planner's advisor.json carries the same.
        var guide = LazyCrucible.CrucibleGuide.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "CrucibleGuide.json")));
        guide.InstallNeedModel();

        // Rosters (XBMPet rows). "seed" rosters use a tiny LCG so they are reproducible in any language.
        var all = Enumerable.Range(1, BST_Beasts.Count).ToArray();
        int[] ByLevel(int max) => all.Where(r => BST_Beasts.All[r].CaptureLevel <= max).ToArray();
        int[] Lcg(uint seed, int count)
        {
            var set = new SortedSet<int>();
            var x = seed;
            while (set.Count < count)
            {
                x = unchecked(x * 1664525u + 1013904223u);
                set.Add((int)(x % (uint)BST_Beasts.Count) + 1);
            }
            return set.ToArray();
        }
        var rosters = new Dictionary<string, int[]>
        {
            ["all"] = all,
            ["level30"] = ByLevel(30),
            ["level40"] = ByLevel(40),
            ["seed20"] = Lcg(20260922u, 20),
            ["starter6"] = [1, 10, 6, 7, 5, 20],
            ["empty"] = [],
        };

        // HP scenarios for PickSlots (row -> hp%). Absent rows are "assumed full".
        var scenarios = new Dictionary<string, Dictionary<int, int>>
        {
            ["full"] = new(),
            ["mixed"] = new() { [1] = 0, [10] = 0, [20] = 0, [5] = 40, [7] = 40, [6] = 15, [2] = 100, [11] = 60, [33] = 1 },
        };

        var battlesByBoard = new Dictionary<int, List<int>>();
        foreach (var b in BST_CrucibleData.Battles)
        {
            if (!battlesByBoard.TryGetValue(b.Board, out var list))
                battlesByBoard[b.Board] = list = [];
            list.Add(b.Battle);
        }

        var scoreMatrix = new List<object>();
        foreach (var (board, battles) in battlesByBoard.OrderBy(kv => kv.Key))
            foreach (var battle in battles)
                for (var row = 1; row <= BST_Beasts.Count; row++)
                {
                    var why0 = new List<string>();
                    var s0 = BST_CrucibleAdvisor.Score(board, battle, row, CrucibleNeeds.None, why0, 0);
                    var why1 = new List<string>();
                    var s1 = BST_CrucibleAdvisor.Score(board, battle, row, CrucibleNeeds.Interrupt | CrucibleNeeds.Dispel | CrucibleNeeds.Cleanse, why1, 1);
                    var why2 = new List<string>();
                    var s2 = BST_CrucibleAdvisor.Score(board, battle, row, CrucibleNeeds.Dispel, why2, 2);
                    scoreMatrix.Add(new { board, battle, row, s0, why0 = string.Join(", ", why0), s1, why1 = string.Join(", ", why1), s2, why2 = string.Join(", ", why2) });
                }

        var needModels = new List<object>();
        var needFirst = new List<object>();
        var needFirstSlots = new List<object>();
        var needFirstWorth = new List<object>();
        var needFirstBoardRoster = new List<object>();
        var answers = Enumerable.Range(0, BST_Beasts.Count + 2).Select(r => (int)BST_CrucibleAdvisor.Answers(r)).ToArray();
        var hpFactor = new[] { -5, 0, 1, 14, 15, 16, 27, 50, 99, 100, 101 }.Select(h => new { hp = h, f = BST_CrucibleAdvisor.HpFactor(h), eff = BST_CrucibleAdvisor.EffectiveScore(37, h) }).ToList();
        var mixedPets = new List<(int Row, uint Current, uint Max)> { (1, 0, 500), (2, 500, 500), (3, 1, 500), (4, 250, 500), (5, 600, 500), (0, 10, 10), (51, 10, 10), (6, 10, 0), (7, 333, 1000) };
        var hpByRow = BST_CrucibleAdvisor.HpPercentByRow(mixedPets);

        foreach (var (board, battles) in battlesByBoard.OrderBy(kv => kv.Key))
            foreach (var battle in battles)
            {
                var model = CrucibleNeedModel.For(board, battle);
                needModels.Add(new
                {
                    board,
                    battle,
                    items = model.Items.Select(i => new { kind = (int)i.Kind, tier = (int)i.Tier, what = i.What, src = i.Src.ToArray() }).ToList(),
                    required = (int)model.Required,
                    useful = (int)model.Useful,
                    crowdControl = (int)model.CrowdControl,
                });
            }

        foreach (var (name, rows) in rosters)
        {
            var set = new HashSet<int>(rows);
            bool Captured(int r) => set.Contains(r);
            foreach (var (board, battles) in battlesByBoard.OrderBy(kv => kv.Key))
            {
                foreach (var battle in battles)
                {
                    var sel3 = BST_CrucibleNeedFirst.SelectCaptured(board, battle, Captured);
                    needFirst.Add(new { roster = name, board, battle, count = 3, selection = SelectionOut(sel3) });
                    if (board == 5 && battle == 0)
                        needFirst.Add(new { roster = name, board, battle, count = 5, selection = SelectionOut(BST_CrucibleNeedFirst.SelectCaptured(board, battle, Captured, 5)) });
                    needFirstWorth.Add(new { roster = name, board, battle, count = 2, picks = BST_CrucibleNeedFirst.WorthCapturing(board, battle, Captured, sel3).Select(Out).ToList() });
                    needFirstWorth.Add(new { roster = name, board, battle, count = 5, picks = BST_CrucibleNeedFirst.WorthCapturing(board, battle, Captured, sel3, 5).Select(Out).ToList() });
                    foreach (var (sname, hp) in scenarios)
                    {
                        needFirstSlots.Add(new { roster = name, board, battle, scenario = sname, slots = 3, selection = SelectionOut(BST_CrucibleNeedFirst.Select(board, battle, rows, hp, 3, "in the run roster")) });
                        if (battle == 1)
                            needFirstSlots.Add(new { roster = name, board, battle, scenario = sname, slots = 1, selection = SelectionOut(BST_CrucibleNeedFirst.Select(board, battle, rows, hp, 1, "in the run roster")) });
                    }
                }
                needFirstBoardRoster.Add(new { roster = name, board, rows = BST_CrucibleNeedFirst.BoardRoster(board, Captured).Select(t => new { row = t.Row, battles = t.Battles }).ToList() });
            }
        }

        var result = new
        {
            generated = "CruciblePlanner.Golden (shared advisor source, deterministic)",
            beastCount = BST_Beasts.Count,
            rosters,
            scenarios,
            answers,
            hpFactor,
            hpByRow = hpByRow.OrderBy(kv => kv.Key).Select(kv => new { row = kv.Key, hp = kv.Value }).ToList(),
            scoreMatrix,
            needModels,
            needFirst,
            needFirstSlots,
            needFirstWorth,
            needFirstBoardRoster,
        };
        var json = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = false, PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        File.WriteAllText(outPath, json + "\n");
        Console.WriteLine($"{outPath}: {json.Length} chars; scoreMatrix {scoreMatrix.Count}, needModels {needModels.Count}, needFirst {needFirst.Count}, needFirstSlots {needFirstSlots.Count}, needFirstWorth {needFirstWorth.Count}, needFirstBoardRoster {needFirstBoardRoster.Count}");
        return 0;
    }
}
