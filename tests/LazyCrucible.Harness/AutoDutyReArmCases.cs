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

    /// <summary> Runs the scheduler over a 10 ms tick clock and returns the times a correction was made. </summary>
    private static List<long> Corrections(IReadOnlyList<long> autoDutyEdits, long untilMs, long settleMs = 250)
    {
        var made = new List<long>();
        var editSeen = false;
        long lastEdit = 0, lastCorrection = long.MinValue;
        var corrections = 0;
        for (long t = 0; t <= untilMs; t += 10)
        {
            foreach (var e in autoDutyEdits)
                if (e == t)
                {
                    editSeen = true;
                    lastEdit = t;
                }
            if (FormationLogic.AutoDutyCorrectionDue(editSeen, lastEdit, lastCorrection, corrections, t, settleMs))
            {
                made.Add(t);
                corrections++;
                lastCorrection = t;
            }
        }
        return made;
    }

    public static void Run()
    {
        Console.WriteLine("-- AutoDuty re-arm (0.1.9.6; live 2026-10-03 fight 1843 and 16 more) --");

        // Fight 1843's own timeline, relative to AutoDuty's first toggle: toggles at 0, 516, 1033, 1541; confirm at 2050.
        long[] edits = [0, 516, 1033, 1541];
        const long confirm = 2050;
        var made = Corrections(edits, 3000);
        Check("fight 1843 shape: the correction is made again after each AutoDuty toggle (4 writes, the cap)",
            made.Count == FormationLogic.AutoDutyMaxCorrections, string.Join(",", made));
        Check("... the first one is 250 ms after the first toggle, as before", made.Count > 0 && made[0] == 250, string.Join(",", made));
        Check("... the last one lands after AutoDuty's last toggle and before its confirm (so it is the selection that is confirmed)",
            made.Count > 0 && made[^1] > edits[^1] && made[^1] < confirm, string.Join(",", made));
        Check("nothing is written between corrections without a new AutoDuty toggle",
            !made.Any(m => m > 250 && m < 766 && m != 766), string.Join(",", made));

        // No edit seen: never due (the pass waits for AutoDuty to write something this open).
        Check("AutoDuty never wrote this open: never due", Corrections([], 5000).Count == 0);

        // AutoDuty keeps rewriting for 20 s: capped, no write storm.
        var storm = Enumerable.Range(0, 40).Select(i => i * 500L).ToArray();
        var stormMade = Corrections(storm, 21000);
        Check("AutoDuty toggling every 500 ms for 20 s: corrections capped", stormMade.Count == FormationLogic.AutoDutyMaxCorrections,
            $"{stormMade.Count}: {string.Join(",", stormMade)}");

        // A single early toggle then silence: exactly one correction (the old behaviour for a screen AutoDuty writes once).
        Check("one AutoDuty toggle then silence: exactly one correction", Corrections([100], 3000).Count == 1);

        // The settle time still protects the confirm window: a toggle then a correction 250 ms later.
        var single = Corrections([0], 1000);
        Check("settle 250 ms after the toggle", single.Count == 1 && single[0] == 250, string.Join(",", single));
    }
}
