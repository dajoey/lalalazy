using Lalalazy.Crucible;

namespace LazyCrucible;

/// <summary> What a fight does to the party, from the fight guide (each flag is sourced there). </summary>
[Flags]
public enum CrucibleThreat : uint
{
    None = 0,
    Poison = 1 << 0,
    Paralysis = 1 << 1,
    Blind = 1 << 2,
    Petrify = 1 << 3,
    Sleep = 1 << 4,
    Doom = 1 << 5,
    Heavy = 1 << 6,
    Confusion = 1 << 7,
    Bind = 1 << 8,
    Knockback = 1 << 9,
    RoomWide = 1 << 10,
    Tankbuster = 1 << 11,
    Adds = 1 << 12,
    DontAttack = 1 << 13,
    CounterStance = 1 << 14,
    Invulnerable = 1 << 15,
}

/// <summary> Score bonuses the policies may chase. Default: none (survival first). </summary>
public enum ScoreGoal : byte
{
    /// <summary> Keep familiars alive, then win; score bonuses are never chased. </summary>
    SurvivalFirst = 0,
    /// <summary> "Starve the Fever": never feed a familiar. </summary>
    StarveTheFever = 1,
    /// <summary> "Feed the Bold": feed whenever a familiar can eat something that does no harm. </summary>
    FeedTheBold = 2,
}

/// <summary> One familiar as a selection screen shows it. </summary>
/// <param name="Row"> XBMPet row. </param>
/// <param name="Index"> Index in the screen's familiar list (the value a pick sends). </param>
/// <param name="HpPercent"> 0-100; 0 = knocked out. </param>
/// <param name="CannotEat"> The feed picker's own "cannot eat this feed" flag for the offered feed (null = not shown). </param>
public readonly record struct FamiliarState(
    int Row,
    int Index,
    int HpPercent,
    int SatietyUsed,
    int SatietyMax,
    IReadOnlyList<int> FeedsEaten,
    bool? CannotEat = null,
    bool RestSelected = false)
{
    public bool KnockedOut => HpPercent <= 0;
    public bool Hungry => SatietyUsed < SatietyMax;
    public bool Ate(int feedRow) => FeedsEaten.Contains(feedRow);
    public bool AteLily => FeedsEaten.Any(r => (CrucibleItems.Get(r).Flags & ItemFlags.KeepsFeedAtCamp) != 0);
    public bool AteLassi => FeedsEaten.Any(r => (CrucibleItems.Get(r).Flags & ItemFlags.FullHealAtCamp) != 0);
    public string Name => BeastName(Row);

    public static string BeastName(int row)
    {
        if (row is < 1 or > BST_Beasts.Count)
            return $"familiar {row}";
        var n = BST_Beasts.All[row].Name;
        return n.Length == 0 ? "?" : char.ToUpperInvariant(n[0]) + n[1..];
    }
}

/// <summary>
///     Everything the selection policies know about the run at a decision: the board, what is still ahead (from the
///     board graph and the last battle fought), what those fights threaten (the fight guide), which familiars the
///     Battlehorn ranking will want for them (<see cref="HornDemand"/>, from <see cref="BST_CrucibleAdvisor.PickSlots"/>
///     so feeding, resting and the horn picks never disagree), the player's HP and the score goal. PURE.
/// </summary>
internal sealed class RunContext
{
    public int Board { get; init; }
    public int LastBattle { get; init; } = -1;
    public IReadOnlyList<UpcomingBattle> Upcoming { get; init; } = [];
    public IReadOnlyList<int> CampsAhead { get; init; } = [];
    public int PlayerHpPercent { get; init; } = 100;
    public ScoreGoal Goal { get; init; } = ScoreGoal.SurvivalFirst;

    /// <summary> Familiar row → sum over fights ahead of (1 certain / 0.5 possible) where it is one of the three horn picks. </summary>
    public IReadOnlyDictionary<int, double> HornDemand { get; init; } = new Dictionary<int, double>();

    /// <summary> Threat → 2 when a fight on every path has it, 1 when only a possible fight has it. </summary>
    public IReadOnlyDictionary<CrucibleThreat, int> ThreatLevels { get; init; } = new Dictionary<CrucibleThreat, int>();

    /// <summary> Panel needs of the fights ahead (dispel matters for Fang of Water). </summary>
    public CrucibleNeeds NeedsAhead { get; init; }

    /// <summary> Weakness → number of fights ahead whose main enemy has it (elemental axes). </summary>
    public IReadOnlyDictionary<CrucibleWeakness, int> WeaknessAhead { get; init; } = new Dictionary<CrucibleWeakness, int>();

    public bool CampAhead => CampsAhead.Count > 0;
    public int FightsAhead => Upcoming.Count;
    public int CertainFightsAhead => Upcoming.Count(u => u.Certain);

    public int ThreatLevel(CrucibleThreat t) => ThreatLevels.TryGetValue(t, out var l) ? l : 0;

    public double Demand(int row) => HornDemand.TryGetValue(row, out var d) ? d : 0;

    /// <summary> Short "for the fights ahead" phrase for reasons. </summary>
    public string DemandPhrase(int row)
    {
        var d = Demand(row);
        if (d <= 0)
            return "not a horn pick for the fights ahead";
        var n = (int)Math.Ceiling(d);
        return n == 1 ? "a horn pick for 1 fight ahead" : $"a horn pick for {n} fights ahead";
    }

    /// <summary>
    ///     Build the context. <paramref name="threatsOf"/> is the fight guide's threat set per (board, battle);
    ///     <paramref name="aliveRows"/>/<paramref name="hpByRow"/> are the run roster and its HP (knocked-out
    ///     familiars have 0 and are never picked).
    /// </summary>
    public static RunContext Build(
        int board,
        int lastBattle,
        Func<int, int, CrucibleThreat> threatsOf,
        IReadOnlyList<int> rosterRows,
        IReadOnlyDictionary<int, int> hpByRow,
        int playerHpPercent = 100,
        ScoreGoal goal = ScoreGoal.SurvivalFirst)
    {
        var upcoming = CrucibleBoards.Upcoming(board, lastBattle);
        var demand = new Dictionary<int, double>();
        var threats = new Dictionary<CrucibleThreat, int>();
        var weakness = new Dictionary<CrucibleWeakness, int>();
        var needs = CrucibleNeeds.None;
        foreach (var u in upcoming)
        {
            var weight = u.Certain ? 1.0 : 0.5;
            if (rosterRows.Count > 0)
                foreach (var p in BST_CrucibleAdvisor.PickSlots(board, u.Battle, rosterRows, hpByRow, 3))
                    demand[p.Row] = demand.GetValueOrDefault(p.Row) + weight;

            var t = threatsOf(board, u.Battle);
            for (var bit = 0; bit < 32; bit++)
            {
                var flag = (CrucibleThreat)(1u << bit);
                if ((t & flag) == 0)
                    continue;
                threats[flag] = Math.Max(threats.GetValueOrDefault(flag), u.Certain ? 2 : 1);
            }

            needs |= BST_CrucibleData.BattleNeeds(board, u.Battle);
            foreach (var e in BST_CrucibleData.Enemies)
                if (e.Board == board && e.Battle == u.Battle && e.Sub == 0 && e.Weakness != CrucibleWeakness.None)
                    weakness[e.Weakness] = weakness.GetValueOrDefault(e.Weakness) + 1;
        }

        return new RunContext
        {
            Board = board,
            LastBattle = lastBattle,
            Upcoming = upcoming,
            CampsAhead = CrucibleBoards.CampsAhead(board, lastBattle),
            PlayerHpPercent = Math.Clamp(playerHpPercent, 0, 100),
            Goal = goal,
            HornDemand = demand,
            ThreatLevels = threats,
            NeedsAhead = needs,
            WeaknessAhead = weakness,
        };
    }

    /// <summary> The threat a resisted/cured status protects against. </summary>
    public static CrucibleThreat ThreatOf(CrucibleStatus s) => s switch
    {
        CrucibleStatus.Poison => CrucibleThreat.Poison,
        CrucibleStatus.Paralysis => CrucibleThreat.Paralysis,
        CrucibleStatus.Blind => CrucibleThreat.Blind,
        CrucibleStatus.Petrify => CrucibleThreat.Petrify,
        CrucibleStatus.Sleep => CrucibleThreat.Sleep,
        CrucibleStatus.Doom => CrucibleThreat.Doom,
        _ => CrucibleThreat.None,
    };

    public static IEnumerable<CrucibleStatus> Each(CrucibleStatus s)
    {
        for (var bit = 0; bit < 8; bit++)
        {
            var f = (CrucibleStatus)(1 << bit);
            if ((s & f) != 0)
                yield return f;
        }
    }

    public static string StatusName(CrucibleStatus s) => s switch
    {
        CrucibleStatus.Petrify => "petrification",
        _ => s.ToString().ToLowerInvariant(),
    };
}
