using System.Numerics;
using Lalalazy.Crucible;

namespace LazyCrucible.Policy;

/// <summary> One need-coverage correction: the rows after the fix, the delta ("<c>+11-49</c>") and why anything stayed uncovered. </summary>
public readonly record struct AutoDutyFix(List<int> Rows, string Delta, List<string> Miss);

/// <summary>
///     Need-coverage correction for a run another driver owns (AutoDuty). AutoDuty picks its own familiars — a
///     leveling team at the battlehorn screen and the roster rebuild — and its picks are blind to what the fights
///     call for, so on the 2026-10-02 evening every unanswered Strix dispel was a fight where no dispeller was on
///     the horn even though one sat in the roster, and the healer fight often had no Soulkin in the roster at all.
///     The correction is MINIMAL: the driver's rows are kept except where a need is uncovered, the least-covering
///     row makes way for the answerer, and a fight whose needs are already covered is never touched. The needs are
///     the effective ones the rotation answers too (<see cref="EffectiveNeeds"/>: Required, plus Useful non-dispel —
///     the game's own interruptible/cleansable flag decides those at run time). PURE: the live pass
///     (PetSelect) reads the driver's selection and writes the corrected rows through its own readback machinery.
/// </summary>
internal static class AutoDutyNeedFix
{
    /// <summary> The battlehorn slots (the horn fix never leaves more rows than the driver had room for). </summary>
    public const int HornSlots = 3;

    /// <summary> The needs that matter live: the same set the rotation answers (a Useful dispel the panel disputes never counts). </summary>
    public static CrucibleNeeds EffectiveNeeds(CrucibleNeedModel model) => model.Required | (model.Useful & ~CrucibleNeeds.Dispel);

    private static int Hp(IReadOnlyDictionary<int, int> hpByRow, int row) => hpByRow.TryGetValue(row, out var hp) ? hp : 100;

    private static CrucibleNeeds Answers(int row) => BST_CrucibleAdvisor.Answers(row);

    private static string KindWord(CrucibleNeeds kind) => kind switch
    {
        CrucibleNeeds.Interrupt => "interrupt",
        CrucibleNeeds.Dispel => "dispel",
        _ => "cleanse",
    };

    /// <summary>
    ///     Answerer ranking: covers more newly-uncovered kinds first; for the same coverage the rotation's own answer
    ///     preference (vulture before Wavekin, bat before Ashkin — their Tempered Release answers without a Kinship);
    ///     then the healthier familiar; then the lower row. Deterministic.
    /// </summary>
    private static (int, int, int, int) AnswerRank(int row, CrucibleNeeds newly, int hp)
    {
        var preferred = (newly & CrucibleNeeds.Dispel) != 0 && row == BST_CrucibleAdvisor.VultureRow
                        || (newly & CrucibleNeeds.Cleanse) != 0 && row == BST_CrucibleAdvisor.BatRow ? 1 : 0;
        return (BitOperations.PopCount((uint)newly), preferred, hp, -row);
    }

    private static List<int> ValidRows(IReadOnlyList<int> rows)
    {
        var result = new List<int>(rows.Count);
        var seen = new HashSet<int>();
        foreach (var row in rows)
            if (row is >= 1 and <= BST_Beasts.Count && seen.Add(row))
                result.Add(row);
        return result;
    }

    /// <summary>
    ///     Add answerers for every uncovered need (greedy, best rank first), then name what stayed uncovered and why
    ///     (nobody in <paramref name="poolRows"/> answers it, or every answerer there is knocked out).
    /// </summary>
    private static (List<int> Rows, List<string> Miss) CoverNeeds(List<int> rows, CrucibleNeeds needs, IReadOnlyList<int> poolRows, IReadOnlyDictionary<int, int> hpByRow, string pool)
    {
        var miss = new List<string>();
        if (needs == CrucibleNeeds.None)
            return (rows, miss);

        var covered = CrucibleNeeds.None;
        foreach (var row in rows)
            if (Hp(hpByRow, row) > 0)
                covered |= Answers(row) & needs;
        var uncovered = needs & ~covered;

        var cands = new List<int>();
        var inRows = new HashSet<int>(rows);
        foreach (var row in ValidRows(poolRows))
            if (!inRows.Contains(row) && Hp(hpByRow, row) > 0 && (Answers(row) & uncovered) != 0)
                cands.Add(row);

        while (uncovered != CrucibleNeeds.None && cands.Count > 0)
        {
            var best = cands[0];
            var bestKey = AnswerRank(best, Answers(best) & uncovered, Hp(hpByRow, best));
            for (var i = 1; i < cands.Count; i++)
            {
                var key = AnswerRank(cands[i], Answers(cands[i]) & uncovered, Hp(hpByRow, cands[i]));
                if (key.CompareTo(bestKey) > 0)
                {
                    best = cands[i];
                    bestKey = key;
                }
            }

            rows.Add(best);
            cands.Remove(best);
            uncovered &= ~Answers(best);
        }

        foreach (var kind in BST_CrucibleNeedFirst.Kinds)
        {
            if ((uncovered & kind) == 0)
                continue;
            var answerers = ValidRows(poolRows).FindAll(r => (Answers(r) & kind) != 0);
            if (answerers.Count == 0)
                miss.Add($"{KindWord(kind)}: no {BST_CrucibleNeedFirst.KinName(kind)} {pool}");
            else if (answerers.All(r => Hp(hpByRow, r) <= 0))
                miss.Add($"{KindWord(kind)}: every {BST_CrucibleNeedFirst.KinName(kind)} {pool} is knocked out");
        }

        return (rows, miss);
    }

    /// <summary> Remove rows down to <paramref name="limit"/>: the row whose removal loses the fewest need kinds nobody else covers, then the hurt one, then the highest row (the driver's late leveling additions). Never removes a row that uniquely covers a need while a redundant one exists. </summary>
    private static List<string> Trim(List<int> rows, CrucibleNeeds needs, IReadOnlyDictionary<int, int> hpByRow, int limit)
    {
        var removed = new List<string>();
        while (rows.Count > limit)
        {
            var victim = -1;
            var victimKey = default((int, int, int));
            for (var i = 0; i < rows.Count; i++)
            {
                var unique = Answers(rows[i]) & needs;
                for (var j = 0; j < rows.Count; j++)
                    if (j != i)
                        unique &= ~Answers(rows[j]);
                var key = (BitOperations.PopCount((uint)unique), Hp(hpByRow, rows[i]), -rows[i]);
                if (victim < 0 || key.CompareTo(victimKey) <= 0)
                {
                    victim = i;
                    victimKey = key;
                }
            }

            removed.Add($"-{rows[victim]}");
            rows.RemoveAt(victim);
        }

        return removed;
    }

    /// <summary>
    ///     The horn correction for one battle: AutoDuty's picks stay except where the battle's effective needs are
    ///     uncovered and a healthy roster member answers. A smaller driver team gets the answerer as an extra pick.
    /// </summary>
    public static AutoDutyFix Horn(int board, int battle, IReadOnlyList<int> driverRows, IReadOnlyList<int> rosterRows, IReadOnlyDictionary<int, int> hpByRow)
    {
        var rows = ValidRows(driverRows);
        var needs = EffectiveNeeds(CrucibleNeedModel.For(board, battle));
        var (grown, miss) = CoverNeeds(rows, needs, rosterRows, hpByRow, "in the run roster");
        var delta = new List<string>();
        for (var i = driverRows.Count; i < grown.Count; i++)
            delta.Add($"+{grown[i]}");
        delta.AddRange(Trim(grown, needs, hpByRow, HornSlots));
        return new(grown, string.Join("", delta), miss);
    }

    /// <summary>
    ///     The roster correction for a whole board: AutoDuty's roster stays except where a need of any of the board's
    ///     battles has no healthy answerer in it; the answerer is appended when the roster has room, else swapped in
    ///     for a leveling pick that answers nothing.
    /// </summary>
    public static AutoDutyFix Roster(int board, IReadOnlyList<int> driverRows, IReadOnlyList<int> capturedRows, IReadOnlyDictionary<int, int> hpByRow, int cap)
    {
        var rows = ValidRows(driverRows);
        var needs = CrucibleNeeds.None;
        foreach (var battle in BST_CrucibleData.Battles)
            if (battle.Board == board)
                needs |= EffectiveNeeds(CrucibleNeedModel.For(board, battle.Battle));

        var (grown, miss) = CoverNeeds(rows, needs, capturedRows, hpByRow, "captured");
        var delta = new List<string>();
        for (var i = driverRows.Count; i < grown.Count; i++)
            delta.Add($"+{grown[i]}");
        delta.AddRange(Trim(grown, needs, hpByRow, cap));
        return new(grown, string.Join("", delta), miss);
    }
}
