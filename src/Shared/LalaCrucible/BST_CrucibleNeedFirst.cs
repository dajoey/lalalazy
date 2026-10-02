using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace Lalalazy.Crucible;

/// <summary> How sure we are that a fight needs an ability. Required must be covered first; Useful is covered next. </summary>
public enum CrucibleNeedTier : byte
{
    Useful = 1,
    Required = 2,
}

/// <summary> One ability a battle calls for: the kind (interrupt, dispel, cleanse), its tier, what for, and who says so. </summary>
public readonly record struct CrucibleAbilityNeed(CrucibleNeeds Kind, CrucibleNeedTier Tier, string What, IReadOnlyList<string> Src);

/// <summary> One need of a selection: covered by <see cref="Row"/>, or <see cref="WhyNot"/> says why nothing covers it. </summary>
public readonly record struct CrucibleNeedStatus(CrucibleAbilityNeed Need, int Row, string? WhyNot)
{
    public bool Covered => Row != 0;
}

/// <summary> Horn picks for one battle plus the status of every need the fight has. </summary>
public sealed record CrucibleSelection(List<CrucibleBeastPick> Picks, List<CrucibleNeedStatus> Needs);

/// <summary>
///     What one battle needs, in ONE list: the abilities (interrupt, dispel, cleanse) with a tier, and the crowd
///     control its enemies are vulnerable to. The picker and the fight guide both read this, so they cannot disagree.
///     Tier rule: an ability is Required when the game's own enemy panel calls for it, when the guide marks it
///     mandatory, or when two or more guide sources agree and none disputes it; anything single-sourced or disputed
///     (the panel or another source says otherwise) is Useful. Crowd control and the elemental weakness are not
///     tiered: they never have to be answered, so they only rank familiars that cover the same abilities.
/// </summary>
public sealed class CrucibleNeedModel
{
    private static readonly Dictionary<(int, int), CrucibleNeedModel> Cache = [];
    private static Func<int, int, IReadOnlyList<CrucibleAbilityNeed>>? _extras;

    /// <summary> Vulnerability bit 3 is "interrupt" (a Soulkin need, not crowd control). </summary>
    public const ushort CrowdControlBits = 0x7FF & ~(1 << 3);

    public int Board { get; }
    public int Battle { get; }
    public IReadOnlyList<CrucibleAbilityNeed> Items { get; }

    /// <summary> Ability kinds the fight requires. </summary>
    public CrucibleNeeds Required { get; }

    /// <summary> Ability kinds that are only Useful (kinds already Required are not repeated). </summary>
    public CrucibleNeeds Useful { get; }

    /// <summary> Crowd-control statuses any enemy of the battle is vulnerable to (vulnerability bits 0-10 minus interrupt). </summary>
    public ushort CrowdControl { get; }

    private CrucibleNeedModel(int board, int battle, List<CrucibleAbilityNeed> items, ushort crowdControl)
    {
        Board = board;
        Battle = battle;
        Items = items;
        CrowdControl = crowdControl;
        var required = CrucibleNeeds.None;
        var any = CrucibleNeeds.None;
        foreach (var i in items)
        {
            any |= i.Kind;
            if (i.Tier == CrucibleNeedTier.Required)
                required |= i.Kind;
        }
        Required = required;
        Useful = any & ~required;
    }

    /// <summary>
    ///     Extra ability needs a host knows beyond the enemy panel (LazyCrucible: the fight guide's counters, tiered).
    ///     Unset, the model is the panel alone. Setting it clears the cache.
    /// </summary>
    public static Func<int, int, IReadOnlyList<CrucibleAbilityNeed>>? Extras
    {
        get => _extras;
        set
        {
            lock (Cache)
            {
                _extras = value;
                Cache.Clear();
            }
        }
    }

    public static CrucibleNeedModel For(int board, int battle)
    {
        lock (Cache)
        {
            if (Cache.TryGetValue((board, battle), out var cached))
                return cached;

            var items = new List<CrucibleAbilityNeed>();
            if (_extras?.Invoke(board, battle) is { } extra)
                items.AddRange(extra);

            var panel = BST_CrucibleData.BattleNeeds(board, battle);
            foreach (var kind in BST_CrucibleNeedFirst.Kinds)
            {
                if ((panel & kind) != 0 && !items.Any(i => i.Kind == kind && i.Tier == CrucibleNeedTier.Required))
                    items.Add(new(kind, CrucibleNeedTier.Required, "the enemy panel calls for it", ["panel"]));
            }

            ushort cc = 0;
            foreach (var e in BST_CrucibleData.Enemies)
                if (e.Board == board && e.Battle == battle)
                    cc |= (ushort)(e.Vulnerable & CrowdControlBits);

            var model = new CrucibleNeedModel(board, battle, items, cc);
            Cache[(board, battle)] = model;
            return model;
        }
    }
}

/// <summary>
///     Need-first horn picks. PURE shared SOURCE. The fight's ability needs (<see cref="CrucibleNeedModel"/>) decide
///     the three familiars: every Required need is covered by a healthy familiar first (one familiar covering several
///     is preferred), then the Useful needs; the point score (<see cref="BST_CrucibleAdvisor.Score"/>: elemental
///     weakness, crowd control, stats, scaled by remaining HP) only chooses between familiars that cover the same
///     needs and orders the final picks. Knocked-out familiars are never picked.
/// </summary>
internal static class BST_CrucibleNeedFirst
{
    /// <summary> Ability kinds in priority order. </summary>
    public static readonly CrucibleNeeds[] Kinds = [CrucibleNeeds.Interrupt, CrucibleNeeds.Dispel, CrucibleNeeds.Cleanse];

    private const CrucibleNeeds AllAbilities = CrucibleNeeds.Interrupt | CrucibleNeeds.Dispel | CrucibleNeeds.Cleanse;

    private readonly record struct Cand(int Row, int Hp, bool HpKnown);

    /// <summary> Higher is more important: interrupt, then dispel, then cleanse. </summary>
    private static int Priority(CrucibleNeeds mask) =>
        ((mask & CrucibleNeeds.Interrupt) != 0 ? 4 : 0) + ((mask & CrucibleNeeds.Dispel) != 0 ? 2 : 0) + ((mask & CrucibleNeeds.Cleanse) != 0 ? 1 : 0);

    private static int Count(CrucibleNeeds mask) => BitOperations.PopCount((uint)mask);

    /// <summary> The ability name for a familiar answering a kind (Soul Crush, Quelling Wave / Bloodcurdling Caw, Scouring Ash / Ultrasonics). </summary>
    public static string AbilityName(int row, CrucibleNeeds kind) => kind switch
    {
        CrucibleNeeds.Interrupt => "Soul Crush",
        CrucibleNeeds.Dispel => row == BST_CrucibleAdvisor.VultureRow ? "Bloodcurdling Caw" : "Quelling Wave",
        CrucibleNeeds.Cleanse => row == BST_CrucibleAdvisor.BatRow ? "Ultrasonics" : "Scouring Ash",
        _ => kind.ToString(),
    };

    /// <summary> Which familiars answer a kind, as a player would say it. </summary>
    public static string KinName(CrucibleNeeds kind) => kind switch
    {
        CrucibleNeeds.Interrupt => "Soulkin",
        CrucibleNeeds.Dispel => "Wavekin or Vulture",
        CrucibleNeeds.Cleanse => "Ashkin or Bat",
        _ => kind.ToString(),
    };

    private static List<Cand> Candidates(IReadOnlyList<int> rows, IReadOnlyDictionary<int, int> hpByRow, bool dropDead)
    {
        var result = new List<Cand>(rows.Count);
        var seen = new HashSet<int>();
        foreach (var row in rows)
        {
            if (row is < 1 or > BST_Beasts.Count || !seen.Add(row))
                continue;
            var known = hpByRow.TryGetValue(row, out var hp);
            if (!known)
                hp = 100;
            if (dropDead && hp <= 0)
                continue;
            result.Add(new(row, hp, known));
        }
        return result;
    }

    /// <summary> Points that rank familiars covering the same needs: Score with every ability need counted as already answered, scaled by HP. </summary>
    private static int Points(int board, int battle, Cand c, int weaknessPicks) =>
        BST_CrucibleAdvisor.EffectiveScore(BST_CrucibleAdvisor.Score(board, battle, c.Row, AllAbilities, null, weaknessPicks), c.Hp);

    private static bool HitsWeakness(int board, int battle, int row)
    {
        var element = BST_CrucibleData.BeastProfiles[row].AutoElement;
        if (element == CrucibleWeakness.None)
            return false;
        foreach (var e in BST_CrucibleData.Enemies)
            if (e.Board == board && e.Battle == battle && e.Weakness == element)
                return true;
        return false;
    }

    /// <summary>
    ///     Horn picks for one battle from an explicit candidate roster and per-familiar HP%. Absent HP counts as full
    ///     (and is said in <c>Why</c>); 0% HP is never picked. <paramref name="pool"/> names where the candidates come
    ///     from for the "not available" reason ("captured", "in the run roster").
    /// </summary>
    public static CrucibleSelection Select(
        int board,
        int battle,
        IReadOnlyList<int> candidateRows,
        IReadOnlyDictionary<int, int> hpByRow,
        int slots = 3,
        string pool = "captured")
    {
        var model = CrucibleNeedModel.For(board, battle);
        var everyone = Candidates(candidateRows, hpByRow, dropDead: false);
        var cands = everyone.Where(c => c.Hp > 0).ToList();

        var chosen = new List<(Cand C, CrucibleNeeds New, int Weak)>(slots);
        var covered = CrucibleNeeds.None;
        var coveredBy = new Dictionary<CrucibleNeeds, int>();
        var taken = new HashSet<int>();
        var weaknessPicks = 0;

        for (var n = 0; n < slots; n++)
        {
            Cand? best = null;
            (int, int, int, int, int, int, int) bestKey = default;
            foreach (var c in cands)
            {
                if (taken.Contains(c.Row))
                    continue;
                var answers = BST_CrucibleAdvisor.Answers(c.Row);
                var req = answers & model.Required & ~covered;
                var use = answers & model.Useful & ~covered;
                var key = (Count(req), Count(use), Priority(req), Priority(use), Points(board, battle, c, weaknessPicks), c.Hp, -c.Row);
                if (best is null || key.CompareTo(bestKey) > 0)
                {
                    best = c;
                    bestKey = key;
                }
            }

            if (best is not { } pick)
                break;

            var now = BST_CrucibleAdvisor.Answers(pick.Row) & (model.Required | model.Useful) & ~covered;
            foreach (var kind in Kinds)
                if ((now & kind) != 0)
                    coveredBy[kind] = pick.Row;
            chosen.Add((pick, now, weaknessPicks));
            taken.Add(pick.Row);
            covered |= now;
            if (HitsWeakness(board, battle, pick.Row))
                weaknessPicks++;
        }

        var picks = new List<(CrucibleBeastPick Pick, int Hp)>(chosen.Count);
        foreach (var (c, now, weak) in chosen)
        {
            var why = new List<string>(6);
            foreach (var kind in Kinds)
            {
                if ((now & kind) == 0)
                    continue;
                var tag = (model.Required & kind) != 0 ? "" : " (useful)";
                why.Add(AbilityName(c.Row, kind) + tag);
            }
            BST_CrucibleAdvisor.Score(board, battle, c.Row, AllAbilities, why, weak);
            if (!c.HpKnown)
                why.Add("HP assumed full");
            else if (c.Hp < 100)
                why.Add($"HP {c.Hp}%");
            picks.Add((new(c.Row, Points(board, battle, c, 0), true, string.Join(", ", why)), c.Hp));
        }

        // Slot order: best fit first (HP-scaled), healthiest on a tie, then lowest row. The set itself is need-first.
        picks.Sort((a, b) =>
        {
            var byScore = b.Pick.Score.CompareTo(a.Pick.Score);
            if (byScore != 0)
                return byScore;
            var byHp = b.Hp.CompareTo(a.Hp);
            return byHp != 0 ? byHp : a.Pick.Row.CompareTo(b.Pick.Row);
        });

        var statuses = new List<CrucibleNeedStatus>(model.Items.Count);
        foreach (var item in model.Items)
        {
            if (coveredBy.TryGetValue(item.Kind, out var by))
                statuses.Add(new(item, by, null));
            else
                statuses.Add(new(item, 0, WhyUncovered(item.Kind, everyone, slots, pool)));
        }

        return new(picks.ConvertAll(p => p.Pick), statuses);
    }

    private static string WhyUncovered(CrucibleNeeds kind, List<Cand> everyone, int slots, string pool)
    {
        var answerers = everyone.Where(c => (BST_CrucibleAdvisor.Answers(c.Row) & kind) != 0).ToList();
        if (answerers.Count == 0)
            return $"no {KinName(kind)} {pool}";
        if (answerers.All(c => c.Hp <= 0))
            return answerers.Count == 1 ? $"the {KinName(kind)} is knocked out" : $"every {KinName(kind)} is knocked out";
        return $"the {slots} horn slots went to other needs";
    }

    /// <summary> The fixed-roster view with every captured familiar at full HP (the advisor panel, a preview with no run). </summary>
    public static CrucibleSelection SelectCaptured(int board, int battle, Func<int, bool> captured, int count = 3)
    {
        var rows = new List<int>();
        for (var row = 1; row <= BST_Beasts.Count; row++)
            if (captured(row))
                rows.Add(row);
        return Select(board, battle, rows, new Dictionary<int, int>(), count);
    }

    /// <summary> Picks only, for callers that do not show needs. </summary>
    public static List<CrucibleBeastPick> Pick(int board, int battle, Func<int, bool> captured, int count = 3) =>
        SelectCaptured(board, battle, captured, count).Picks;

    /// <summary>
    ///     A roster for the whole board (the beast-selection limit) from the need-first picks: familiars that appear in
    ///     battles' picks, most battles first, then by total score. Battles only a random space leads to count half.
    ///     Same tally as <see cref="BST_CrucibleAdvisor.BoardRoster"/>, over <see cref="Pick"/> instead of the point score.
    /// </summary>
    public static List<(int Row, int Battles)> BoardRoster(int board, Func<int, bool> captured)
    {
        var info = BST_CrucibleData.Boards[board - 1];
        var tally = new Dictionary<int, (double Weight, int Battles, int Score)>();

        foreach (var battle in BST_CrucibleData.Battles)
        {
            if (battle.Board != board)
                continue;
            foreach (var pick in Pick(board, battle.Battle, captured))
            {
                var t = tally.GetValueOrDefault(pick.Row);
                tally[pick.Row] = (t.Weight + (battle.RandomOnly ? 0.5 : 1.0), t.Battles + 1, t.Score + pick.Score);
            }
        }

        return tally
            .OrderByDescending(kv => kv.Value.Weight)
            .ThenByDescending(kv => kv.Value.Score)
            .Take(info.Roster)
            .Select(kv => (kv.Key, kv.Value.Battles))
            .ToList();
    }

    /// <summary>
    ///     Horn picks when the battle is not identified but the board is (pre-entry): the same need-first rule, with
    ///     every battle of the board counted. Each pick maximises the Required (fight, ability) pairs it newly covers,
    ///     then the Useful ones, then points. Every <c>Why</c> starts with <c>coverage board N, battle unidentified</c>.
    /// </summary>
    public static List<CrucibleBeastPick> SelectCoverage(
        int board,
        IReadOnlyList<int> candidateRows,
        IReadOnlyDictionary<int, int> hpByRow,
        int slots = 3)
    {
        var battles = BST_CrucibleData.Battles.Where(b => b.Board == board).Select(b => b.Battle).ToList();
        var picks = new List<CrucibleBeastPick>(slots);
        if (battles.Count == 0)
            return picks;

        var models = battles.ToDictionary(b => b, b => CrucibleNeedModel.For(board, b));
        var coveredIn = battles.ToDictionary(b => b, _ => CrucibleNeeds.None);
        var cands = Candidates(candidateRows, hpByRow, dropDead: true);
        var taken = new HashSet<int>();
        var weaknessPicks = 0;

        for (var n = 0; n < slots; n++)
        {
            Cand? best = null;
            (int, int, int, int, int, int) bestKey = default;
            foreach (var c in cands)
            {
                if (taken.Contains(c.Row))
                    continue;
                var answers = BST_CrucibleAdvisor.Answers(c.Row);
                int req = 0, use = 0, reqPri = 0, usePri = 0, raw = 0;
                var any = false;
                foreach (var b in battles)
                {
                    var r = answers & models[b].Required & ~coveredIn[b];
                    var u = answers & models[b].Useful & ~coveredIn[b];
                    req += Count(r);
                    use += Count(u);
                    reqPri += Priority(r);
                    usePri += Priority(u);
                    var s = BST_CrucibleAdvisor.Score(board, b, c.Row, AllAbilities, null, weaknessPicks);
                    if (s == int.MinValue)
                        continue;
                    raw += s;
                    any = true;
                }
                if (!any)
                    continue;
                var key = (req, use, reqPri, usePri, BST_CrucibleAdvisor.EffectiveScore(raw, c.Hp), c.Hp);
                var better = best is null || key.CompareTo(bestKey) > 0 || (key.CompareTo(bestKey) == 0 && c.Row < best.Value.Row);
                if (better)
                {
                    best = c;
                    bestKey = key;
                }
            }

            if (best is not { } pick)
                break;

            var why = new List<string>(6) { $"coverage board {board}, battle unidentified" };
            var fightsNeeding = new Dictionary<CrucibleNeeds, int>();
            var answered = BST_CrucibleAdvisor.Answers(pick.Row);
            foreach (var b in battles)
            {
                var now = answered & (models[b].Required | models[b].Useful) & ~coveredIn[b];
                foreach (var kind in Kinds)
                    if ((now & kind) != 0)
                        fightsNeeding[kind] = fightsNeeding.GetValueOrDefault(kind) + 1;
                coveredIn[b] |= now;
            }
            foreach (var kind in Kinds)
                if (fightsNeeding.TryGetValue(kind, out var fights))
                    why.Add($"{AbilityName(pick.Row, kind)} x{fights} fights");
            var sampleBattle = battles.Contains(1) ? 1 : battles[0];
            BST_CrucibleAdvisor.Score(board, sampleBattle, pick.Row, AllAbilities, why, weaknessPicks);
            if (!pick.HpKnown)
                why.Add("HP assumed full");
            else if (pick.Hp < 100)
                why.Add($"HP {pick.Hp}%");

            picks.Add(new(pick.Row, bestKey.Item5, true, string.Join(", ", why)));
            taken.Add(pick.Row);
            if (HitsWeakness(board, sampleBattle, pick.Row))
                weaknessPicks++;
        }

        return picks;
    }

    /// <summary>
    ///     Uncaptured familiars (capturable at the board's level) worth going for: first those that answer a need the
    ///     picks leave uncovered (Required before Useful), then any that beat the weakest pick on points by 3 or more.
    /// </summary>
    public static List<CrucibleBeastPick> WorthCapturing(int board, int battle, Func<int, bool> captured, CrucibleSelection selection, int count = 2)
    {
        var level = BST_CrucibleData.Boards[board - 1].Level;
        var result = new List<CrucibleBeastPick>();
        var listed = new HashSet<int>();

        var open = selection.Needs.Where(s => !s.Covered).OrderByDescending(s => s.Need.Tier).ThenByDescending(s => Priority(s.Need.Kind)).ToList();
        foreach (var status in open)
        {
            for (var row = 1; row <= BST_Beasts.Count; row++)
            {
                if (captured(row) || BST_Beasts.All[row].CaptureLevel > level || listed.Contains(row)
                    || (BST_CrucibleAdvisor.Answers(row) & status.Need.Kind) == 0)
                    continue;
                listed.Add(row);
                var tag = status.Need.Tier == CrucibleNeedTier.Required ? "" : " (useful)";
                result.Add(new(row, Points(board, battle, new(row, 100, false), 0), false, AbilityName(row, status.Need.Kind) + tag));
            }
        }

        var bar = selection.Picks.Count < 3 ? int.MinValue : selection.Picks.Min(p => p.Score) + 3;
        var weaknessPicks = selection.Picks.Count(p => HitsWeakness(board, battle, p.Row));
        for (var row = 1; row <= BST_Beasts.Count; row++)
        {
            if (captured(row) || BST_Beasts.All[row].CaptureLevel > level || listed.Contains(row))
                continue;
            var why = new List<string>(4);
            var s = BST_CrucibleAdvisor.Score(board, battle, row, AllAbilities, why, Math.Max(0, weaknessPicks - 1));
            if (s >= bar)
                result.Add(new(row, s, false, string.Join(", ", why)));
        }

        // Need-answering familiars come first in listed order; the points ones follow, best first.
        var needFirst = result.Take(listed.Count);
        var rest = result.Skip(listed.Count).OrderByDescending(p => p.Score);
        return needFirst.Concat(rest).Take(count).ToList();
    }
}
