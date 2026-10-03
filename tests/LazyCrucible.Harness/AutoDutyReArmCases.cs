namespace LazyCrucible.Harness;

/// <summary>
///     AutoDuty re-arm (0.1.9.6). Live 2026-10-03: the need-coverage correction logged <c>needs=autoduty+row</c> 17 times across
///     the AutoDuty-driven fights and the added answerer reached the fight's horn slots in none of them. AutoDuty sets its horn
///     picks one toggle every ~0.5 s and confirms ~0.5 s after the last toggle; the correction fired once, 250 ms after the FIRST
///     toggle, and the pass was then closed, so every later AutoDuty toggle overwrote it (fight 1843: our write at 43.486,
///     AutoDuty's own at 43.752, 44.269 and 44.777, confirm at 45.286; the fight ran Diremite, Mantis, Opo-opo and no Soul Crush).
///     These cases replay that shape through the pure scheduler: the correction repeats after each new AutoDuty edit, the last
///     one lands after AutoDuty's last toggle and before its confirm, nothing is written without a new edit, and a screen that
///     AutoDuty keeps rewriting is capped.
/// </summary>
internal static class AutoDutyReArmCases
{
    private static void Check(string what, bool ok, string? detail = null) => Program.Check(what, ok, detail);

    /// <summary> Runs the scheduler over a 1 ms tick clock and returns the times a correction was made. Each edit carries the picks selected after it. </summary>
    private static List<long> Corrections(IReadOnlyList<(long At, int Selected)> autoDutyEdits, long untilMs, long settleMs = 250)
    {
        var made = new List<long>();
        var editSeen = false;
        long lastEdit = 0, lastCorrection = long.MinValue;
        var corrections = 0;
        var selected = 0;
        for (long t = 0; t <= untilMs; t++)
        {
            foreach (var (at, count) in autoDutyEdits)
                if (at == t)
                {
                    editSeen = true;
                    lastEdit = t;
                    selected = count;
                }
            if (FormationLogic.AutoDutyCorrectionDue(editSeen, lastEdit, lastCorrection, corrections, t, settleMs, selected))
            {
                made.Add(t);
                corrections++;
                lastCorrection = t;
            }
        }
        return made;
    }

    private static List<long> Corrections(IReadOnlyList<long> autoDutyEdits, long untilMs, long settleMs = 250) =>
        Corrections(autoDutyEdits.Select(e => (e, FormationLogic.AutoDutyFullTeam)).ToList(), untilMs, settleMs);

    public static void Run()
    {
        Console.WriteLine("-- AutoDuty re-arm (0.1.9.6; live 2026-10-03 fight 1843 and 16 more; 0.1.9.7 waits for the full team) --");

        // Fight 1843's own timeline, relative to AutoDuty's first toggle: toggles at 0, 516, 1033, 1541; confirm at 2050.
        // The team is full (three picks) after the third toggle; the fourth is a swap.
        (long, int)[] edits = [(0, 1), (516, 2), (1033, 3), (1541, 3)];
        const long confirm = 2050;
        var made = Corrections(edits, 3000);
        Check("fight 1843 shape: the first correction waits for the full team (no write on the 1-pick and 2-pick selections)",
            made.Count > 0 && made[0] >= 1033, string.Join(",", made));
        Check("... the last one lands after AutoDuty's last toggle and before its confirm (so it is the selection that is confirmed)",
            made.Count > 0 && made[^1] > edits[^1].Item1 && made[^1] < confirm, string.Join(",", made));
        Check("... the corrections are 250 ms after each toggle that leaves a full team and nowhere else",
            made.SequenceEqual(new long[] { 1283, 1791 }), string.Join(",", made));

        // Live 2026-10-03 18:22:37 (First Master's Board, Strix Piece, five fights alike): AutoDuty's first toggle left ONE pick, the
        // correction added the Wavekin to that one, the selection reverted within 250 ms, and four writes went by in 1.5 s with
        // the cap spent before AutoDuty had built its team (it finished at 1.2.0 two seconds later). Replay: toggles at 0 (1 pick),
        // 520 (2), 1040 (3); nothing is written before the team is full.
        var strix = Corrections([(0, 1), (520, 2), (1040, 3)], 3000);
        Check("Strix shape: no correction on a 1-pick or 2-pick selection, one on the full team before the confirm",
            strix.SequenceEqual(new long[] { 1290 }), string.Join(",", strix));

        // AutoDuty stops short of three (a small pool): a correction still comes, after a long quiet, not at the settle time.
        var shortTeam = Corrections([(0, 1), (520, 2)], 5000);
        Check("AutoDuty leaves two picks and goes quiet: one correction after the long quiet",
            shortTeam.Count == 1 && shortTeam[0] == 520 + FormationLogic.AutoDutyShortTeamQuietMs, string.Join(",", shortTeam));

        // No edit seen: never due (the pass waits for AutoDuty to write something this open).
        Check("AutoDuty never wrote this open: never due", Corrections(Array.Empty<long>(), 5000).Count == 0);

        // AutoDuty keeps rewriting for 20 s: capped, no write storm.
        var storm = Enumerable.Range(0, 40).Select(i => i * 500L).ToArray();
        var stormMade = Corrections(storm, 21000);
        Check("AutoDuty toggling every 500 ms for 20 s: corrections capped", stormMade.Count == FormationLogic.AutoDutyMaxCorrections,
            $"{stormMade.Count}: {string.Join(",", stormMade)}");

        // A single early toggle that leaves a full team, then silence: exactly one correction.
        Check("one AutoDuty toggle (full team) then silence: exactly one correction", Corrections([100], 3000).Count == 1);

        // The settle time still protects the confirm window: a toggle then a correction 250 ms later.
        var single = Corrections([0], 1000);
        Check("settle 250 ms after the toggle", single.Count == 1 && single[0] == 250, string.Join(",", single));
    }
}
