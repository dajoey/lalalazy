using System.Text.Json;
using System.Text.Json.Serialization;
using Lalalazy.Crucible;

namespace CruciblePlanner.Golden;

/// <summary>
///     Dumps the shared Crucible advisor's answers for fixed rosters and HP scenarios so the web port
///     (docs/crucible/advisor.js) can be proven identical. Deterministic: no randomness, no game reads.
/// </summary>
internal static class Program
{
    private sealed record PickOut(int Row, int Score, bool Captured, string Why);

    private static PickOut Out(CrucibleBeastPick p) => new(p.Row, p.Score, p.Captured, p.Why);

    private static int Main(string[] args)
    {
        var outPath = args.Length > 0 ? args[0] : "golden.json";

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

        var pick = new List<object>();
        var pickSlots = new List<object>();
        var worth = new List<object>();
        var coverage = new List<object>();
        var boardRoster = new List<object>();
        var answers = Enumerable.Range(0, BST_Beasts.Count + 2).Select(r => (int)BST_CrucibleAdvisor.Answers(r)).ToArray();
        var hpFactor = new[] { -5, 0, 1, 14, 15, 16, 27, 50, 99, 100, 101 }.Select(h => new { hp = h, f = BST_CrucibleAdvisor.HpFactor(h), eff = BST_CrucibleAdvisor.EffectiveScore(37, h) }).ToList();
        var mixedPets = new List<(int Row, uint Current, uint Max)> { (1, 0, 500), (2, 500, 500), (3, 1, 500), (4, 250, 500), (5, 600, 500), (0, 10, 10), (51, 10, 10), (6, 10, 0), (7, 333, 1000) };
        var hpByRow = BST_CrucibleAdvisor.HpPercentByRow(mixedPets);

        foreach (var (name, rows) in rosters)
        {
            var set = new HashSet<int>(rows);
            bool Captured(int r) => set.Contains(r);
            foreach (var (board, battles) in battlesByBoard.OrderBy(kv => kv.Key))
            {
                foreach (var battle in battles)
                {
                    var p3 = BST_CrucibleAdvisor.Pick(board, battle, Captured);
                    pick.Add(new { roster = name, board, battle, count = 3, picks = p3.Select(Out).ToList() });
                    if (board == 5 && battle == 0)
                        pick.Add(new { roster = name, board, battle, count = 5, picks = BST_CrucibleAdvisor.Pick(board, battle, Captured, 5).Select(Out).ToList() });
                    worth.Add(new { roster = name, board, battle, count = 2, picks = BST_CrucibleAdvisor.WorthCapturing(board, battle, Captured, p3).Select(Out).ToList() });
                    worth.Add(new { roster = name, board, battle, count = 5, picks = BST_CrucibleAdvisor.WorthCapturing(board, battle, Captured, p3, 5).Select(Out).ToList() });
                    foreach (var (sname, hp) in scenarios)
                    {
                        pickSlots.Add(new { roster = name, board, battle, scenario = sname, slots = 3, picks = BST_CrucibleAdvisor.PickSlots(board, battle, rows, hp).Select(Out).ToList() });
                        if (battle == 1)
                            pickSlots.Add(new { roster = name, board, battle, scenario = sname, slots = 1, picks = BST_CrucibleAdvisor.PickSlots(board, battle, rows, hp, 1).Select(Out).ToList() });
                    }
                }
                foreach (var (sname, hp) in scenarios)
                {
                    coverage.Add(new { roster = name, board, scenario = sname, slots = 3, picks = BST_CrucibleAdvisor.PickSlotsCoverage(board, rows, hp).Select(Out).ToList() });
                    coverage.Add(new { roster = name, board, scenario = sname, slots = 10, picks = BST_CrucibleAdvisor.PickSlotsCoverage(board, rows, hp, 10).Select(Out).ToList() });
                }
                boardRoster.Add(new { roster = name, board, rows = BST_CrucibleAdvisor.BoardRoster(board, Captured).Select(t => new { row = t.Row, battles = t.Battles }).ToList() });
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
            pick,
            pickSlots,
            worth,
            coverage,
            boardRoster,
        };
        var json = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = false, PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        File.WriteAllText(outPath, json + "\n");
        Console.WriteLine($"{outPath}: {json.Length} chars; scoreMatrix {scoreMatrix.Count}, pick {pick.Count}, pickSlots {pickSlots.Count}, worth {worth.Count}, coverage {coverage.Count}, boardRoster {boardRoster.Count}");
        return 0;
    }
}
