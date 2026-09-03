using LazyCrafter.Core.Model;

namespace LazyCrafter.Core;

/// <summary>Leveling worth of one recipe for one job (Plan §Phase 2 task 4).</summary>
public sealed record LevelingEstimate(
    uint RecipeId,
    uint JobId,
    int RecipeLevel,
    int JobLevel,
    /// <summary>Clamped <c>jobLevel - recipeLevel</c> (0..21) that picks the modifier.</summary>
    int LevelDifference,
    /// <summary>Base synthesis EXP (NQ, no bonuses, no first-craft bonus) per craft.</summary>
    int ExpPerCraft,
    /// <summary>EXP the first successful craft of this recipe would add on top (the crafting-log bonus).</summary>
    int FirstCraftBonus,
    /// <summary>Materials tier - only tier &lt;= 1 recipes are "leveling material".</summary>
    EffortTier Tier,
    /// <summary>Crafts possible from stock right now.</summary>
    int HowMany)
{
    public bool Eligible => Tier <= EffortTier.Easy && ExpPerCraft > 0;
    /// <summary>EXP from everything craftable now, excluding the one-time bonus.</summary>
    public long ExpFromStock => (long)ExpPerCraft * HowMany;
}

/// <summary>
/// EXP per craft from recipe level vs job level (Plan §Phase 2 task 4, Scope §5.3).
/// <para>
/// Uses the community-derived synthesis formula (r/ffxiv "FFXIV Crafting Exp Formula is here!", 2022-08, verified
/// against in-game values for every level difference): <c>craftExp = floor(floor(base[rlvl] / 3) x mod[diff] / 100)</c>
/// where <c>base</c> is the first-time-completion EXP per recipe level and <c>mod</c> a 0..21 level-difference
/// modifier (recipes above your level count as diff 0; 21+ below is the floor). Quality/HQ, food, FC and manual
/// bonuses are applied on top by the game and are deliberately not modelled: this is a ranking, not a prediction.
/// The plan names <c>ParamGrow</c>; that sheet gives EXP-to-next-level (useful for "crafts to level"), not
/// synthesis EXP, which is why the LUT is used for the per-craft figure.
/// </para>
/// </summary>
public sealed class LevelingScore
{
    /// <summary>Level-difference modifier, index = clamp(jobLevel - recipeLevel, 0, 21), in percent.</summary>
    public static readonly int[] LevelDiffModifier =
        [100, 96, 92, 88, 84, 80, 75, 70, 65, 60, 55, 45, 35, 25, 20, 18, 16, 15, 14, 13, 12, 10];

    /// <summary>First-time-completion EXP per recipe level, index = level (0 unused), levels 1..100.</summary>
    public static readonly int[] FirstCraftExp =
    [
        0,
        540, 582, 630, 795, 996, 1050, 1176, 1263, 1356, 1437,
        1629, 1725, 1875, 1917, 2067, 2241, 2409, 2556, 2700, 2841,
        3045, 3240, 3429, 3612, 3783, 4383, 4683, 5199, 5511, 5745,
        6216, 6948, 7452, 7980, 8568, 9492, 10164, 10773, 11502, 12555,
        13203, 13851, 14499, 15147, 15795, 17334, 18549, 19764, 20979, 27786,
        31500, 34800, 37791, 41571, 45198, 48669, 51969, 52200, 52680, 52992,
        55875, 58656, 61689, 65724, 66498, 66693, 66900, 67410, 67530, 68244,
        68250, 70074, 72552, 77865, 83079, 89211, 95982, 103551, 111990, 134820,
        139553, 154280, 157261, 175221, 182593, 202532, 208955, 226719, 230926, 239004,
        305279, 340725, 350860, 383995, 397220, 438995, 449528, 489453, 499569, 522414,
    ];

    public const int MaxLevel = 100;

    private readonly RecipeGraph _graph;
    private readonly Tiering _tiering;

    public LevelingScore(RecipeGraph graph, Tiering tiering)
    {
        _graph = graph;
        _tiering = tiering;
    }

    public static int LevelDifference(int jobLevel, int recipeLevel) => Math.Clamp(jobLevel - recipeLevel, 0, 21);

    /// <summary>Base synthesis EXP for one craft; 0 for out-of-range levels.</summary>
    public static int ExpPerCraft(int jobLevel, int recipeLevel)
    {
        if (recipeLevel < 1 || recipeLevel > MaxLevel || jobLevel < 1) return 0;
        var basePerCraft = FirstCraftExp[recipeLevel] / 3;                        // floor
        return basePerCraft * LevelDiffModifier[LevelDifference(jobLevel, recipeLevel)] / 100;   // floor
    }

    /// <summary>Score one recipe for the job at <paramref name="jobLevel"/>; <c>null</c> when the recipe is unknown or not for that job.</summary>
    public LevelingEstimate? Evaluate(uint recipeId, uint jobId, int jobLevel, IInventory inv)
    {
        var row = _graph.Row(recipeId);
        if (row is null || row.JobId != jobId) return null;
        var a = _tiering.Assess(recipeId, inv);
        var exp = ExpPerCraft(jobLevel, row.Level);
        var first = row.Level is >= 1 and <= MaxLevel ? FirstCraftExp[row.Level] : 0;
        return new LevelingEstimate(recipeId, jobId, row.Level, jobLevel, LevelDifference(jobLevel, row.Level),
            exp, first, a.Tier, a.HowMany);
    }

    /// <summary>All of a job's recipes that are leveling material (tier &lt;= 1), best EXP per craft first.</summary>
    public IEnumerable<LevelingEstimate> Rank(uint jobId, int jobLevel, IInventory inv, bool includeAboveLevel = false)
    {
        var list = new List<LevelingEstimate>();
        foreach (var id in _graph.RecipeIds)
        {
            var row = _graph.Row(id)!;
            if (row.JobId != jobId) continue;
            if (!includeAboveLevel && row.Level > jobLevel) continue;
            var e = Evaluate(id, jobId, jobLevel, inv);
            if (e is { Eligible: true }) list.Add(e);
        }
        return list.OrderByDescending(e => e.ExpPerCraft).ThenBy(e => e.Tier).ThenBy(e => e.RecipeId);
    }
}
