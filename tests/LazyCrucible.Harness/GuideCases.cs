using Lalalazy.Crucible;
using LazyCrucible;

namespace LazyCrucible.Harness;

/// <summary>
///     The fight guide (src/LazyCrucible/Guide/CrucibleGuide.json): complete, sourced, readable at a glance, and never in
///     disagreement with the horn ranking (every panel need is a counter; no counter claims the panel falsely; no
///     hand-written familiar team competes with the live picks).
/// </summary>
internal static class GuideCases
{
    private static void Check(string what, bool ok, string? detail = null) => Program.Check(what, ok, detail);

    public static void Run()
    {
        Console.WriteLine("-- fight guide --");
        var path = Path.Combine(AppContext.BaseDirectory, "CrucibleGuide.json");
        var guide = CrucibleGuide.Parse(File.ReadAllText(path));
        var problems = guide.Validate();
        Check("Guide validates: 45 battles once each, known threats and sources, counters agree with the enemy panel",
            problems.Count == 0, string.Join(" | ", problems.Take(5)));
        Check("Guide covers every battle the ranking knows (45)",
            BST_CrucibleData.Battles.All(b => guide.Fight(b.Board, b.Battle) is not null) && guide.File.Fights.Count == 45);

        var fights = guide.File.Fights;
        Check("Every mechanic, hit, counter and bring line cites a source",
            fights.All(f => f.Mechanics.Concat(f.Hits).All(m => m.Src.Count > 0) && f.Counters.All(c => c.Src.Count > 0) && f.Bring.All(b => b.Src.Count > 0)));
        Check("Readable at a glance: summary ≤ 90, tell ≤ 70 and do ≤ 80 characters",
            fights.All(f => f.Summary.Length <= 90 && f.Mechanics.Concat(f.Hits).All(m => m.Tell.Length <= 70 && m.Do.Length <= 80)),
            string.Join(" | ", fights.SelectMany(f => f.Mechanics.Concat(f.Hits).Where(m => m.Tell.Length > 70 || m.Do.Length > 80).Select(m => $"{f.Board}:{f.Battle} {m.Name}"))));
        Check("No hand-written familiar team (the window shows the live horn picks instead)",
            fights.All(f => f.Bring.All(b => !b.Text.StartsWith("Team", StringComparison.OrdinalIgnoreCase))));
        Check("No links in the text", fights.All(f => !System.Text.Json.JsonSerializer.Serialize(f).Contains("http", StringComparison.OrdinalIgnoreCase)));

        Check("Threats: First Master's boss (Borgny) poisons; Second Master's boss paralyses; B2 elite 1 dooms",
            (guide.ThreatsOf(4, 0) & CrucibleThreat.Poison) != 0 && (guide.ThreatsOf(5, 0) & CrucibleThreat.Paralysis) != 0
            && (guide.ThreatsOf(2, 2) & CrucibleThreat.Doom) != 0);
        Check("Threats feed the policies: on board 4 from the start, poison is a certain threat (Borgny is on every path)",
            RunContext.Build(4, -1, guide.ThreatsOf, [], new Dictionary<int, int>()).ThreatLevel(CrucibleThreat.Poison) == 2);
        var b1 = guide.Fight(1, 1)!;
        Check("B1 move 1: interrupt, dispel and cleanse counters, all from the panel (the ranking scores the same three)",
            b1.Counters.Where(c => c.FromPanel).Select(c => c.AsNeed).Aggregate(CrucibleNeeds.None, (a, n) => a | n)
            == BST_CrucibleData.BattleNeeds(1, 1));
    }
}
