namespace LazyCrucible;

/// <summary> Space type on a board (XBMContentStageEvent type). </summary>
public enum SpaceType : byte
{
    Start,
    Enemy,
    Elite,
    Boss,
    Shop,
    Campsite,
    Treasure,
    Random,
}

/// <summary> One outcome a random space can resolve to. </summary>
public readonly record struct RandomOutcome(SpaceType Type, int Battle, int CampFamiliars);

/// <summary>
///     One board space: its depth (row), the battle it leads to (-1 for none), how many familiars its campsite
///     rests (0 when not a campsite) and, for a random space, what it can resolve to.
/// </summary>
public readonly record struct CrucibleSpace(int Space, SpaceType Type, int Depth, int Battle, int CampFamiliars, RandomOutcome[]? Random);

/// <summary> A battle still ahead: <see cref="Certain"/> when every path from here to the boss passes it. </summary>
public readonly record struct UpcomingBattle(int Battle, bool Certain, bool RandomOnly);

/// <summary>
///     What is still ahead on a board, from the generated board graphs. PURE. The player's position is not read
///     from the game: it is the space of the last battle fought this run (the board start when none), so every
///     space reachable from there counts as ahead, both sides of an unchosen fork included.
/// </summary>
internal static partial class CrucibleBoards
{
    public static bool IsBoard(int board) => board is >= 1 and <= 5;

    /// <summary> The space a battle is fought on (a random space for random-only battles), -1 when unknown. </summary>
    public static int SpaceOfBattle(int board, int battle)
    {
        if (!IsBoard(board))
            return -1;
        foreach (var s in Spaces[board])
        {
            if (s.Battle == battle)
                return s.Space;
            if (s.Random is { } outs)
                foreach (var o in outs)
                    if (o.Battle == battle)
                        return s.Space;
        }
        return -1;
    }

    /// <summary> Spaces reachable from <paramref name="from"/> (not including it). </summary>
    public static HashSet<int> Reachable(int board, int from)
    {
        var seen = new HashSet<int>();
        if (!IsBoard(board))
            return seen;
        var stack = new Stack<int>();
        stack.Push(from);
        while (stack.Count > 0)
        {
            var at = stack.Pop();
            foreach (var (a, b) in Edges[board])
                if (a == at && seen.Add(b))
                    stack.Push(b);
        }
        return seen;
    }

    private static int BossSpace(int board)
    {
        foreach (var s in Spaces[board])
            if (s.Type == SpaceType.Boss)
                return s.Space;
        return -1;
    }

    /// <summary> Whether every path from <paramref name="from"/> to the boss passes <paramref name="space"/>. </summary>
    public static bool OnEveryPath(int board, int from, int space)
    {
        if (space == from)
            return true;
        var boss = BossSpace(board);
        if (space == boss)
            return true;
        // Remove the space and see whether the boss is still reachable.
        var seen = new HashSet<int> { from };
        var stack = new Stack<int>();
        stack.Push(from);
        while (stack.Count > 0)
        {
            var at = stack.Pop();
            foreach (var (a, b) in Edges[board])
            {
                if (a != at || b == space || !seen.Add(b))
                    continue;
                if (b == boss)
                    return false;
                stack.Push(b);
            }
        }
        return true;
    }

    /// <summary> Where the run stands: the space of the last battle fought, or the start space (0). </summary>
    public static int CurrentSpace(int board, int lastBattle) =>
        lastBattle < 0 ? 0 : Math.Max(0, SpaceOfBattle(board, lastBattle));

    /// <summary>
    ///     Battles still ahead after <paramref name="lastBattle"/> (-1 = none fought yet), boss last. A battle
    ///     behind a random space is <see cref="UpcomingBattle.RandomOnly"/> and never certain.
    /// </summary>
    public static List<UpcomingBattle> Upcoming(int board, int lastBattle)
    {
        var list = new List<UpcomingBattle>();
        if (!IsBoard(board))
            return list;
        var from = CurrentSpace(board, lastBattle);
        var ahead = Reachable(board, from);
        foreach (var s in Spaces[board])
        {
            if (!ahead.Contains(s.Space))
                continue;
            if (s.Battle >= 0)
                list.Add(new(s.Battle, OnEveryPath(board, from, s.Space), false));
            else if (s.Random is { } outs)
                foreach (var o in outs)
                    if (o.Battle >= 0)
                        list.Add(new(o.Battle, false, true));
        }
        list.Sort((x, y) =>
        {
            var bossFirst = (x.Battle == 0).CompareTo(y.Battle == 0); // boss last
            return bossFirst != 0 ? bossFirst : Depth(board, x.Battle).CompareTo(Depth(board, y.Battle));
        });
        return list;
    }

    private static int Depth(int board, int battle)
    {
        var space = SpaceOfBattle(board, battle);
        foreach (var s in Spaces[board])
            if (s.Space == space)
                return s.Depth;
        return int.MaxValue;
    }

    /// <summary> Familiar counts of the campsites still ahead (random-space campsites included). </summary>
    public static List<int> CampsAhead(int board, int lastBattle)
    {
        var list = new List<int>();
        if (!IsBoard(board))
            return list;
        var ahead = Reachable(board, CurrentSpace(board, lastBattle));
        foreach (var s in Spaces[board])
        {
            if (!ahead.Contains(s.Space))
                continue;
            if (s.Type == SpaceType.Campsite)
                list.Add(s.CampFamiliars);
            else if (s.Random is { } outs)
                foreach (var o in outs)
                    if (o.Type == SpaceType.Campsite)
                        list.Add(o.CampFamiliars);
        }
        return list;
    }

    /// <summary> Where a battle sits, as a short label ("row 3, elite"). </summary>
    public static string Where(int board, int battle)
    {
        if (!IsBoard(board))
            return "";
        foreach (var s in Spaces[board])
        {
            if (s.Battle == battle)
                return $"row {s.Depth}, {TypeName(s.Type)}";
            if (s.Random is { } outs)
                foreach (var o in outs)
                    if (o.Battle == battle)
                        return $"row {s.Depth}, random space ({TypeName(o.Type)})";
        }
        return "";
    }

    private static string TypeName(SpaceType t) => t switch
    {
        SpaceType.Elite => "elite",
        SpaceType.Boss => "boss",
        SpaceType.Enemy => "enemy",
        _ => t.ToString().ToLowerInvariant(),
    };
}
