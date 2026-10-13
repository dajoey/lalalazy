using System.Numerics;
using Lalalazy.Crucible;

namespace LazyCrucible.Policy;

/// <summary>
///     "Score: three familiars" roster mode (opt-in, <see cref="Configuration.ScoreThreeFamiliars"/>). PURE. A run roster of one to three
///     familiars scores Beast Squad III on every board, so with the mode on the run roster is the best three for the whole board
///     instead of the board's full cap. The trio is the best of every combination of the owned, healthy familiars: it covers the most
///     of the board's Required needs (interrupt, dispel, cleanse across its battles), then the mitigation the board calls for, then
///     three of one kin when that costs nothing above (the Gambit bonus), then the Useful needs and the advisor's points.
/// </summary>
internal static class ScoreRoster
{
    /// <summary> Familiars on a score roster. </summary>
    public const int Size = 3;

    /// <summary>
    ///     The run roster the writer asks for. Off: the board's full-cap need-first coverage picks, exactly as before the mode existed.
    ///     On: <see cref="Choose"/>.
    /// </summary>
    public static List<CrucibleBeastPick> RosterPicks(
        bool scoreMode, int board, IReadOnlyList<int> candidateRows, IReadOnlyDictionary<int, int> hpByRow, int boardCap) =>
        scoreMode
            ? Choose(board, candidateRows, hpByRow)
            : BST_CrucibleNeedFirst.SelectCoverage(board, candidateRows, hpByRow, boardCap);

    /// <summary>
    ///     Whether the horn pass leaves the screen alone: with the mode on, a party of three or fewer is the score roster, which the
    ///     game registers on the battlehorns itself (patch 7.57).
    /// </summary>
    public static bool HornsLeftToGame(bool scoreMode, int partyCount) => scoreMode && partyCount is > 0 and <= Size;

    /// <summary>
    ///     Whether a roster of one to three familiars already on the entry menu is to be rewritten: with the mode on, any such roster
    ///     is judged against the trio chosen for THIS board (the writer's plan is empty when it already matches). Off, a small roster
    ///     is the player's and is never touched.
    /// </summary>
    public static bool RewritesSmallRoster(bool scoreMode) => scoreMode;

    private readonly record struct Fight(CrucibleNeedModel Model, int Battle);

    private readonly record struct Cand(int Row, int Hp, bool HpKnown, CrucibleNeeds Answers, CrucibleMitigation Mit);

    private static List<Fight> Fights(int board) =>
        BST_CrucibleData.Battles.Where(b => b.Board == board).Select(b => new Fight(CrucibleNeedModel.For(board, b.Battle), b.Battle)).ToList();

    private static int Count(CrucibleNeeds m) => BitOperations.PopCount((uint)m);

    private static int Priority(CrucibleNeeds m) =>
        ((m & CrucibleNeeds.Interrupt) != 0 ? 4 : 0) + ((m & CrucibleNeeds.Dispel) != 0 ? 2 : 0) + ((m & CrucibleNeeds.Cleanse) != 0 ? 1 : 0);

    private static List<Cand> Candidates(IReadOnlyList<int> rows, IReadOnlyDictionary<int, int> hpByRow)
    {
        var result = new List<Cand>(rows.Count);
        var seen = new HashSet<int>();
        foreach (var row in rows.OrderBy(r => r))
        {
            if (row is < 1 or > BST_Beasts.Count || !seen.Add(row))
                continue;
            var known = hpByRow.TryGetValue(row, out var hp);
            if (!known)
                hp = 100;
            if (hp <= 0)
                continue;
            result.Add(new(row, hp, known, BST_CrucibleAdvisor.Answers(row), BST_CrucibleMitigation.Answers(row)));
        }
        return result;
    }

    private readonly record struct Key(int Req, int ReqPri, int Mit, int Kin, int Use, int UsePri, int Points, int Hp);

    private static int Compare(Key a, Key b)
    {
        var c = a.Req.CompareTo(b.Req);
        if (c != 0) return c;
        c = a.ReqPri.CompareTo(b.ReqPri);
        if (c != 0) return c;
        c = a.Mit.CompareTo(b.Mit);
        if (c != 0) return c;
        c = a.Kin.CompareTo(b.Kin);
        if (c != 0) return c;
        c = a.Use.CompareTo(b.Use);
        if (c != 0) return c;
        c = a.UsePri.CompareTo(b.UsePri);
        if (c != 0) return c;
        c = a.Points.CompareTo(b.Points);
        return c != 0 ? c : a.Hp.CompareTo(b.Hp);
    }

    /// <summary>
    ///     The best three familiars for the whole board from <paramref name="candidateRows"/> (knocked-out and unknown rows dropped;
    ///     fewer than three when fewer qualify). Deterministic: equal trios resolve to the lowest rows.
    /// </summary>
    public static List<CrucibleBeastPick> Choose(int board, IReadOnlyList<int> candidateRows, IReadOnlyDictionary<int, int> hpByRow)
    {
        var picks = new List<CrucibleBeastPick>(Size);
        var fights = Fights(board);
        var cands = Candidates(candidateRows, hpByRow);
        if (fights.Count == 0 || cands.Count == 0)
            return picks;

        // Per candidate and fight: the advisor's points (every ability need counted answered, scaled by HP), as in the coverage picker.
        var points = new int[cands.Count][];
        for (var i = 0; i < cands.Count; i++)
        {
            points[i] = new int[fights.Count];
            for (var f = 0; f < fights.Count; f++)
            {
                var raw = BST_CrucibleAdvisor.Score(board, fights[f].Battle, cands[i].Row,
                    CrucibleNeeds.Interrupt | CrucibleNeeds.Dispel | CrucibleNeeds.Cleanse, null, 0);
                points[i][f] = raw == int.MinValue ? 0 : BST_CrucibleAdvisor.EffectiveScore(raw, cands[i].Hp);
            }
        }

        var size = Math.Min(Size, cands.Count);
        var best = new int[size];
        var bestKey = default(Key);
        var have = false;
        var idx = new int[size];
        Search(0, 0);
        return Describe(board, fights, cands, best);

        void Search(int slot, int from)
        {
            if (slot == size)
            {
                var key = Rate(idx);
                if (!have || Compare(key, bestKey) > 0)
                {
                    have = true;
                    bestKey = key;
                    Array.Copy(idx, best, size);
                }
                return;
            }
            for (var i = from; i < cands.Count; i++)
            {
                idx[slot] = i;
                Search(slot + 1, i + 1);
            }
        }

        Key Rate(int[] trio)
        {
            int req = 0, reqPri = 0, mit = 0, use = 0, usePri = 0, pts = 0, hp = 0;
            var answers = CrucibleNeeds.None;
            var mitAll = CrucibleMitigation.None;
            foreach (var i in trio)
            {
                answers |= cands[i].Answers;
                mitAll |= cands[i].Mit;
                hp += cands[i].Hp;
            }
            for (var f = 0; f < fights.Count; f++)
            {
                var m = fights[f].Model;
                var r = answers & m.Required;
                var u = answers & m.Useful;
                req += Count(r);
                reqPri += Priority(r);
                use += Count(u);
                usePri += Priority(u);
                mit += BitOperations.PopCount((uint)(mitAll & m.Mitigation));
                foreach (var i in trio)
                    pts += points[i][f];
            }
            var kin = trio.Length == Size && trio.All(i => BST_Beasts.All[cands[i].Row].Kin == BST_Beasts.All[cands[trio[0]].Row].Kin) ? 1 : 0;
            return new Key(req, reqPri, mit, kin, use, usePri, pts, hp);
        }
    }

    private static List<CrucibleBeastPick> Describe(int board, List<Fight> fights, List<Cand> cands, int[] chosen)
    {
        var result = new List<CrucibleBeastPick>(chosen.Length);
        foreach (var i in chosen)
        {
            var c = cands[i];
            var why = new List<string> { $"score roster board {board}" };
            foreach (var kind in BST_CrucibleNeedFirst.Kinds)
            {
                if ((c.Answers & kind) == 0)
                    continue;
                var fightsNeeding = fights.Count(f => ((f.Model.Required | f.Model.Useful) & kind) != 0);
                if (fightsNeeding > 0)
                    why.Add($"{BST_CrucibleNeedFirst.AbilityName(c.Row, kind)} x{fightsNeeding} fights");
            }
            if (!c.HpKnown)
                why.Add("HP assumed full");
            else if (c.Hp < 100)
                why.Add($"HP {c.Hp}%");
            result.Add(new(c.Row, 0, true, string.Join(", ", why)));
        }
        return result;
    }

    /// <summary>
    ///     The Required needs the trio cannot answer on the board, one entry per kind naming its fights ("interrupt (fights 2, 5)").
    ///     Empty when every Required need of every fight is covered.
    /// </summary>
    public static List<string> Uncovered(int board, IReadOnlyList<int> rows)
    {
        var answers = CrucibleNeeds.None;
        foreach (var row in rows)
            if (row is >= 1 and <= BST_Beasts.Count)
                answers |= BST_CrucibleAdvisor.Answers(row);
        var result = new List<string>();
        foreach (var kind in BST_CrucibleNeedFirst.Kinds)
        {
            if ((answers & kind) != 0)
                continue;
            var battles = Fights(board).Where(f => (f.Model.Required & kind) != 0).Select(f => f.Battle.ToString()).ToList();
            if (battles.Count > 0)
                result.Add($"{KindWord(kind)} (fight{(battles.Count == 1 ? "" : "s")} {string.Join(", ", battles)})");
        }
        return result;
    }

    private static string KindWord(CrucibleNeeds kind) => kind switch
    {
        CrucibleNeeds.Interrupt => "interrupt",
        CrucibleNeeds.Dispel => "dispel",
        CrucibleNeeds.Cleanse => "cleanse",
        _ => kind.ToString().ToLowerInvariant(),
    };

    /// <summary> The one quiet line for the guide / summary: what the chosen three cannot answer on the board, or null when nothing is missing. </summary>
    public static string? UncoveredLine(int board, IReadOnlyList<int> rows)
    {
        var missing = Uncovered(board, rows);
        return missing.Count == 0 ? null : $"Score roster cannot answer: {string.Join("; ", missing)}.";
    }
}
