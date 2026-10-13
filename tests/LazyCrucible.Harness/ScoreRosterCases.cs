using Lalalazy.Crucible;
using LazyCrucible.Policy;

namespace LazyCrucible.Harness;

/// <summary>
///     "Score: three familiars" roster mode (opt-in, off by default). Beast Squad III (a roster of one to three) scores on every board, so with
///     the mode on the run roster is the best three for the whole board instead of the board's cap. Off, everything is as before. The mode
///     never reaches a screen AutoDuty drives, and a saved three-familiar team chosen for another board is rewritten.
/// </summary>
internal static class ScoreRosterCases
{
    private static void Check(string what, bool ok, string? detail = null) => Program.Check(what, ok, detail);

    private static readonly IReadOnlyDictionary<int, int> NoHp = new Dictionary<int, int>();

    private static List<int> All() => Enumerable.Range(1, BST_Beasts.Count).ToList();

    private static string Show(IEnumerable<int> rows) => string.Join(".", rows);

    private static List<int> Answering(CrucibleNeeds kind) => All().Where(r => (BST_CrucibleAdvisor.Answers(r) & kind) != 0).ToList();

    private static CrucibleNeeds RequiredOnBoard(int board)
    {
        var need = CrucibleNeeds.None;
        foreach (var b in BST_CrucibleData.Battles.Where(b => b.Board == board))
            need |= CrucibleNeedModel.For(board, b.Battle).Required;
        return need;
    }

    public static void Run()
    {
        Console.WriteLine("-- score roster mode: three familiars for the whole board (opt-in) --");
        CrucibleNeedModel.Extras = null;
        CrucibleNeedModel.MitigationExtras = null;

        // ---- AC1: off by default, nothing changes
        Check("the option is off by default (the configuration property carries no initializer)", ConfigurationDefaultsOff());
        var caps = new[] { 10, 12, 14, 12, 15 };
        for (var board = 1; board <= 5; board++)
        {
            var cap = BST_CrucibleData.Boards[board - 1].Roster;
            var off = ScoreRoster.RosterPicks(false, board, All(), NoHp, cap);
            var before = BST_CrucibleNeedFirst.SelectCoverage(board, All(), NoHp, cap);
            Check($"mode off, board {board}: the roster picks are the board-cap coverage picks, byte for byte ({cap} familiars)",
                cap == caps[board - 1] && off.SequenceEqual(before), $"{Show(off.Select(p => p.Row))} vs {Show(before.Select(p => p.Row))}");
        }
        Check("mode off: a one-to-three roster on the entry menu still waits (a small roster is the player's)",
            Gate(selected: [1, 2, 3], scoreMode: false) == (FormationLogic.FormationWrite.Wait, "wait_roster_settle"));
        Check("mode off: the horn pass is never left to the game for score reasons",
            !ScoreRoster.HornsLeftToGame(false, 3) && !ScoreRoster.RewritesSmallRoster(false));

        // ---- AC2: on, exactly three on every board, every coverable required need covered
        var allRows = All();
        var partial = allRows.Take(20).ToList();
        for (var board = 1; board <= 5; board++)
        {
            foreach (var (label, pool) in new[] { ("every familiar owned", allRows), ("the first twenty owned", partial) })
            {
                var cap = BST_CrucibleData.Boards[board - 1].Roster;
                var picks = ScoreRoster.RosterPicks(true, board, pool, NoHp, cap).Select(p => p.Row).ToList();
                var required = RequiredOnBoard(board);
                var team = picks.Aggregate(CrucibleNeeds.None, (a, r) => a | BST_CrucibleAdvisor.Answers(r));
                var coverable = pool.Aggregate(CrucibleNeeds.None, (a, r) => a | BST_CrucibleAdvisor.Answers(r)) & required;
                Check($"mode on, board {board}, {label}: exactly three distinct owned familiars ({Show(picks)})",
                    picks.Count == 3 && picks.Distinct().Count() == 3 && picks.All(pool.Contains));
                Check($"mode on, board {board}, {label}: every required need an owned familiar can answer is covered (need {coverable}, team {team})",
                    (team & coverable) == coverable);
                var missing = ScoreRoster.Uncovered(board, picks);
                var unanswerable = new[] { CrucibleNeeds.Interrupt, CrucibleNeeds.Dispel, CrucibleNeeds.Cleanse }
                    .Count(k => (required & k) != 0 && (coverable & k) == 0);
                Check($"mode on, board {board}, {label}: the uncovered list names a need only when the pool has no answerer ({string.Join("; ", missing)})",
                    missing.Count == unanswerable && (missing.Count == 0) == (ScoreRoster.UncoveredLine(board, picks) is null));
            }
        }

        // ---- required need nobody owned can answer: still three, and the line says which
        var interrupt = Answering(CrucibleNeeds.Interrupt);
        var boardNeedingInterrupt = Enumerable.Range(1, 5).FirstOrDefault(b => (RequiredOnBoard(b) & CrucibleNeeds.Interrupt) != 0);
        Check("test data: some board requires an interrupt and some familiars answer it", boardNeedingInterrupt != 0 && interrupt.Count > 0);
        if (boardNeedingInterrupt != 0 && interrupt.Count > 0)
        {
            var noInterrupt = allRows.Except(interrupt).ToList();
            var picks = ScoreRoster.RosterPicks(true, boardNeedingInterrupt, noInterrupt, NoHp, 10).Select(p => p.Row).ToList();
            var line = ScoreRoster.UncoveredLine(boardNeedingInterrupt, picks);
            Check("no owned interrupt: still three familiars, and one line names the interrupt the team cannot answer",
                picks.Count == 3 && line is not null && line.Contains("interrupt"), line);
            var koHp = interrupt.ToDictionary(r => r, _ => 0);
            var koPicks = ScoreRoster.RosterPicks(true, boardNeedingInterrupt, allRows, koHp, 10).Select(p => p.Row).ToList();
            Check("every interrupt familiar knocked out: none is picked, the line says interrupt is uncovered",
                koPicks.Count == 3 && !koPicks.Any(interrupt.Contains) && ScoreRoster.UncoveredLine(boardNeedingInterrupt, koPicks)?.Contains("interrupt") == true);
            Check("every familiar owned: a trio covering the board has no uncovered line",
                ScoreRoster.UncoveredLine(boardNeedingInterrupt, ScoreRoster.Choose(boardNeedingInterrupt, allRows, NoHp).Select(p => p.Row).ToList()) is null);
        }
        Check("fewer than three owned: the roster is what is owned", ScoreRoster.Choose(1, [5, 9], NoHp).Count == 2 && ScoreRoster.Choose(1, [], NoHp).Count == 0);
        Check("the same inputs choose the same trio every time",
            Show(ScoreRoster.Choose(2, allRows, NoHp).Select(p => p.Row)) == Show(ScoreRoster.Choose(2, Enumerable.Reverse(allRows).ToList(), NoHp).Select(p => p.Row)));

        // ---- three of one kin only when it costs no required coverage (Gambit)
        var plain = allRows.Where(r => BST_CrucibleAdvisor.Answers(r) == CrucibleNeeds.None && BST_CrucibleMitigation.Answers(r) == CrucibleMitigation.None).ToList();
        var kinWithThree = plain.GroupBy(r => BST_Beasts.All[r].Kin).FirstOrDefault(g => g.Count() >= 3);
        Check("test data: a kin has three familiars that answer nothing", kinWithThree is not null && plain.Select(r => BST_Beasts.All[r].Kin).Distinct().Count() >= 2);
        if (kinWithThree is not null)
        {
            var kin = BST_Beasts.All[kinWithThree.First()].Kin;
            var trio = ScoreRoster.Choose(1, plain, NoHp).Select(p => p.Row).ToList();
            Check($"no required coverage at stake: three of one kin are chosen (Gambit) ({Show(trio)})",
                trio.Count == 3 && trio.Select(r => BST_Beasts.All[r].Kin).Distinct().Count() == 1);
        }
        var soul = Answering(CrucibleNeeds.Interrupt);
        var wave = Answering(CrucibleNeeds.Dispel);
        var ash = Answering(CrucibleNeeds.Cleanse);
        var needAll = Enumerable.Range(1, 5).FirstOrDefault(b => RequiredOnBoard(b) == (CrucibleNeeds.Interrupt | CrucibleNeeds.Dispel | CrucibleNeeds.Cleanse));
        if (needAll != 0 && soul.Count > 0 && wave.Count > 0 && ash.Count > 0 && kinWithThree is not null)
        {
            var pool = kinWithThree.Concat([soul[0], wave[0], ash[0]]).Distinct().ToList();
            var picks = ScoreRoster.Choose(needAll, pool, NoHp).Select(p => p.Row).ToList();
            var team = picks.Aggregate(CrucibleNeeds.None, (a, r) => a | BST_CrucibleAdvisor.Answers(r));
            Check($"a same-kin trio never costs a required need: interrupt, dispel and cleanse are all covered ({Show(picks)})",
                (team & (CrucibleNeeds.Interrupt | CrucibleNeeds.Dispel | CrucibleNeeds.Cleanse)) == (CrucibleNeeds.Interrupt | CrucibleNeeds.Dispel | CrucibleNeeds.Cleanse));
        }
        else
            Check("test data: a board requires all three abilities", needAll != 0);

        // ---- wiring pieces (pure)
        Check("mode on: a one-to-three roster on the entry menu is judged against this board's trio, not left waiting",
            Gate(selected: [3, 5, 6], scoreMode: true) == (FormationLogic.FormationWrite.Roster, "roster"));
        Check("mode on: a roster the player edited by hand is still left alone",
            Gate(selected: [3, 5, 6], scoreMode: true, playerOwned: true) == (FormationLogic.FormationWrite.None, "player_owned"));
        Check("mode on: the open's first frames (party set, nothing selected yet) still wait for the vectors to agree",
            Gate(selected: [], scoreMode: true) == (FormationLogic.FormationWrite.Wait, "wait_roster_settle"));
        Check("mode on: a ten-familiar saved roster is rewritten as before",
            Gate(selected: Enumerable.Range(1, 10).ToList(), scoreMode: true) == (FormationLogic.FormationWrite.Roster, "roster"));
        Check("mode on: the horn screen is the horn gate's, unchanged by the mode",
            FormationLogic.DecideFormationWrite(new(true, true, false, 2, 1, [0, 1, 2], 3, ScoreMode: true)).Write == FormationLogic.FormationWrite.Horn);

        var stale = ScoreRoster.Choose(1, allRows, NoHp).Select(p => p.Row).ToList();
        var thisBoard = ScoreRoster.Choose(4, allRows, NoHp).Select(p => p.Row).ToList();
        var plan = FormationLogic.PlanRosterChanges(stale, ScoreRoster.Choose(4, allRows, NoHp));
        var same = FormationLogic.PlanRosterChanges(thisBoard, ScoreRoster.Choose(4, allRows, NoHp));
        Check($"a saved trio chosen for board 1 ({Show(stale)}) is rewritten for board 4 ({Show(thisBoard)}): removes and adds only the difference",
            stale.SequenceEqual(thisBoard) ? plan.Count == 0 : plan.Count > 0 && plan.Count(c => c.FromRow != 0) == stale.Except(thisBoard).Count()
                && plan.Count(c => c.ToRow != 0) == thisBoard.Except(stale).Count());
        Check("a saved trio already right for this board is not rewritten (no churn)", same.Count == 0);

        Check("horns: with the mode on a party of one to three is left to the game's own registration",
            ScoreRoster.HornsLeftToGame(true, 3) && ScoreRoster.HornsLeftToGame(true, 1));
        Check("horns: a bigger party, an empty party, or the mode off keeps the usual horn pass",
            !ScoreRoster.HornsLeftToGame(true, 10) && !ScoreRoster.HornsLeftToGame(true, 0) && !ScoreRoster.HornsLeftToGame(false, 3));

        // ---- AC3: nothing is written on a surface AutoDuty drives (the mode is not an input to that decision)
        var driven = AutoDutyCrucible.Driven(null);
        Check("AutoDuty running: LazyCrucible yields the run roster whatever the score mode says",
            AutoDutyCrucible.Yields(true, driven, CrucibleSurface.Roster)
            && AutoDutyCrucible.Yields(true, AutoDutyCrucible.Driven(new AutoDutyCrucibleConfig("p", "Leveling", "Carry_Minus_2", "First", true)), CrucibleSurface.Roster));
        Check("AutoDuty stopped: the surface is the player's again (the mode may write)", !AutoDutyCrucible.Yields(false, driven, CrucibleSurface.Roster));
    }

    private static bool ConfigurationDefaultsOff()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var file = Path.Combine(dir.FullName, "src", "LazyCrucible", "Configuration.cs");
            if (!File.Exists(file))
                continue;
            var line = File.ReadAllLines(file).FirstOrDefault(l => l.Contains("ScoreThreeFamiliars"));
            return line is not null && line.Contains("{ get; set; }") && !line.Contains('=');
        }
        return false;
    }

    private static (FormationLogic.FormationWrite Write, string Reason) Gate(List<int> selected, bool scoreMode, bool playerOwned = false) =>
        FormationLogic.DecideFormationWrite(new(true, false, true, 0, 1, selected, 14, RosterPlayerOwned: playerOwned, ScoreMode: scoreMode));
}
