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
    private static List<long> Corrections(IReadOnlyList<(long At, int Selected)> autoDutyEdits, long untilMs, long settleMs = 250,
        int fullTeamCount = FormationLogic.AutoDutyFullTeam, bool ignoreWhenEmpty = false)
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
            if (FormationLogic.AutoDutyCorrectionDue(editSeen, lastEdit, lastCorrection, corrections, t, settleMs, selected, fullTeamCount,
                ignoreWhenEmpty))
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

    /// <summary>
    ///     Replays a real AutoDuty-driven roster screen through the plugin's real data flow: the recorded events go
    ///     through <see cref="FormationLogic.IsSelectionEditEvent"/> (the notebook writes the build rides on are NOT
    ///     edit events), the membership timeline is what the plugin reads every frame, and the settle tracker plus
    ///     gate are the calls the PetSelect call site makes. The default gate is the 0.1.9.12 call site's: the
    ///     preentry roster correction under AutoDuty is withdrawn (never due). <paramref name="rosterGate01911"/>
    ///     replays the withdrawn 0.1.9.11 membership gate instead — past the close, to show why the correction was
    ///     withdrawn (due only after the screen closed on every real stream). With <paramref name="fullTeamCount"/>
    ///     set, the gate is the 0.1.9.10 edit-time call — replayed to show on the real streams why it was replaced.
    ///     1 ms tick; the simulation does not feed a correction's own write back into the membership.
    /// </summary>
    private static List<long> ReplayRoster(IReadOnlyList<(long At, string Agent, ulong Kind, uint Count, int First)> events,
        IReadOnlyList<(long At, int[] Rows)> membership, long untilMs, int fullTeamCount = 0, bool rosterGate01911 = false)
    {
        var made = new List<long>();
        var editSeen = false;
        long lastEdit = 0, lastCorrection = long.MinValue, changedMs = 0;
        string? sig = null;
        var corrections = 0;
        for (long t = 0; t <= untilMs; t++)
        {
            foreach (var (at, agent, kind, count, first) in events)
                if (at == t && FormationLogic.IsSelectionEditEvent(agent, kind, count, first))
                {
                    editSeen = true;
                    lastEdit = t;
                }
            var rows = membership.LastOrDefault(m => m.At <= t).Rows ?? [];
            var nextSig = string.Join(".", rows);
            changedMs = FormationLogic.AutoDutyRosterChangedMs(sig, changedMs, nextSig, t);
            sig = nextSig;
            var due = fullTeamCount > 0
                ? FormationLogic.AutoDutyCorrectionDue(editSeen, lastEdit, lastCorrection, corrections, t, 250, rows.Length, fullTeamCount,
                    ignoreWhenEmpty: true)
                : rosterGate01911 && FormationLogic.AutoDutyRosterCorrectionDue(editSeen, lastEdit, lastCorrection, corrections, t,
                    rows.Length, changedMs); // the withdrawn 0.1.9.11 gate, tombstoned; the 0.1.9.12 call site is never due
            if (due)
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

        Console.WriteLine("-- the preentry roster surface under AutoDuty never writes (replays of the real 2026-10-05 event streams, bounded at each screen's real close) --");

        // The 0.1.9.10 roster cases were removed: they hand-built the edit list, treating every AutoDuty add as an
        // edit event — exactly what the plugin never sees (IsSelectionEditEvent classifies the monster notebook's
        // kind-0 build writes as navigation), so a green hand-built harness proved nothing for the live path. These
        // cases replay the REAL streams: every ag=pp and ag=nb event of the graded screens at its real offset
        // (ffxivdb plugin_log_lines, context LazyCrucible, 2026-10-05, ms from the screen's first pp event), the
        // graded membership steps, the real edit detector, the real settle tracker, the real gate.

        // Live 11:35:26.595-29.726 ET (Fourth Board preentry roster, capacity 12): pp kind 0/7/8 edits at 0/100/218
        // open the screen; the build adds ten picks at 837-1770 (nb kind 0, not edits); pp kind 0/5 confirm pairs at
        // 2283-2792. The live 0.1.9.9 correction fired at ~1170 with four picks, mid-build, and was torn out.
        (long, string, ulong, uint, int)[] fight1135 =
        [
            (0, "pp", 0, 2, 2), (0, "pp", 0, 2, 2), (100, "pp", 7, 3, 0), (218, "pp", 8, 1, 0), (525, "pp", 0, 1, 5),
            (837, "nb", 0, 2, 7), (936, "nb", 0, 2, 7), (1038, "nb", 0, 2, 5), (1138, "nb", 0, 2, 7), (1248, "nb", 0, 2, 3),
            (1365, "nb", 0, 2, 5), (1468, "nb", 0, 2, 7), (1568, "nb", 0, 2, 7), (1670, "nb", 0, 2, 7), (1770, "nb", 0, 2, 7),
            (1875, "nb", 0, 2, 7), (2283, "pp", 0, 2, 2), (2383, "pp", 5, 3, 0), (2688, "pp", 0, 2, 2), (2792, "pp", 5, 3, 0),
            (3131, "pp", 0, 1, -2),
        ];
        (long, int[])[] build1135 =
        [
            (837, [5]), (936, [5, 8]), (1038, [5, 8, 19]), (1138, [5, 8, 19, 22]), (1248, [5, 8, 19, 22, 41]),
            (1365, [5, 8, 19, 22, 41, 45]), (1468, [5, 8, 19, 22, 41, 45, 46]), (1568, [5, 8, 19, 22, 41, 45, 46, 47]),
            (1670, [5, 8, 19, 22, 41, 45, 46, 47, 48]), (1770, [5, 8, 19, 22, 41, 45, 46, 47, 48, 50]),
        ];
        // The screen's real close is the last event (3131, pp kind 0 n=1 i=-2, matching gate=phase screen=0): each
        // replay runs twice — bounded at the close (nothing may be written while the screen is open) and 2 s past
        // it (the roster surface under AutoDuty never writes at all: AutoDuty's confirm passes remove unfamiliar
        // rows, and its accept follows the build's last add by ~0.43-0.54 s with no observable marker between, so
        // no correction can be placed there on evidence — it is withdrawn, not settled).
        var replay1135 = ReplayRoster(fight1135, build1135, 3131);
        Check("replay 11:35:26 (real stream, bounded at its real close 3131): no correction while the screen is open",
            replay1135.Count == 0, string.Join(",", replay1135));
        var pastClose1135 = ReplayRoster(fight1135, build1135, 5131);
        Check("replay 11:35:26 past the close: the roster surface under AutoDuty never writes — the correction is withdrawn",
            pastClose1135.Count == 0, string.Join(",", pastClose1135));
        var tombstone019111135 = ReplayRoster(fight1135, build1135, 5131, rosterGate01911: true);
        Check("replay 11:35:26 under the withdrawn 0.1.9.11 gate (tombstone): due only at 3270, after the real close 3131",
            tombstone019111135.SequenceEqual(new long[] { 3270 }), string.Join(",", tombstone019111135));

        // The 0.1.9.10 gate (roster capacity as the full-team threshold, edit times as the clock) on the SAME real
        // stream: the last edit is stale at 218, ten picks is short of twelve, so it fires at 1500-1718 — before the
        // build's last add at 1770, inside AutoDuty's confirm window. This is why the edit-time gate is unfixable.
        var tombstone1135 = ReplayRoster(fight1135, build1135, 3600, fullTeamCount: 12);
        Check("replay 11:35:26 under the 0.1.9.10 edit-time gate: still fires mid-build (why the gate was replaced)",
            tombstone1135.Count > 0 && tombstone1135[0] < 1770, string.Join(",", tombstone1135));

        // Live 12:14:41.080-44.329 ET (same board, same shape): edits at 0/102/216, ten adds at 861-1803, confirms at
        // 2363-2914. The live 0.1.9.9 correction fired mid-build at ~1175 with four picks.
        (long, string, ulong, uint, int)[] fight1214 =
        [
            (0, "pp", 0, 2, 2), (102, "pp", 7, 3, 0), (216, "pp", 8, 1, 0), (540, "pp", 0, 1, 5),
            (861, "nb", 0, 2, 7), (965, "nb", 0, 2, 5), (1073, "nb", 0, 2, 7), (1175, "nb", 0, 2, 7), (1281, "nb", 0, 2, 3),
            (1386, "nb", 0, 2, 7), (1491, "nb", 0, 2, 5), (1591, "nb", 0, 2, 7), (1703, "nb", 0, 2, 7), (1803, "nb", 0, 2, 5),
            (1930, "nb", 0, 2, 7), (2363, "pp", 0, 2, 2), (2480, "pp", 5, 3, 0), (2814, "pp", 0, 2, 2), (2914, "pp", 5, 3, 0),
            (3249, "nb", 0, 1, -2), (3249, "pp", 0, 1, -2),
        ];
        (long, int[])[] build1214 =
        [
            (861, [1]), (965, [1, 8]), (1073, [1, 8, 19]), (1175, [1, 8, 19, 22]), (1281, [1, 8, 19, 22, 34]),
            (1386, [1, 8, 19, 22, 34, 45]), (1491, [1, 8, 19, 22, 34, 45, 46]), (1591, [1, 8, 19, 22, 34, 45, 46, 47]),
            (1703, [1, 8, 19, 22, 34, 45, 46, 47, 48]), (1803, [1, 8, 19, 22, 34, 45, 46, 47, 48, 49]),
        ];
        var replay1214 = ReplayRoster(fight1214, build1214, 3249);
        Check("replay 12:14:41 (real stream, bounded at its real close 3249): no correction while the screen is open",
            replay1214.Count == 0, string.Join(",", replay1214));
        var pastClose1214 = ReplayRoster(fight1214, build1214, 5249);
        Check("replay 12:14:41 past the close: the roster surface under AutoDuty never writes — the correction is withdrawn",
            pastClose1214.Count == 0, string.Join(",", pastClose1214));
        var tombstone019111214 = ReplayRoster(fight1214, build1214, 5249, rosterGate01911: true);
        Check("replay 12:14:41 under the withdrawn 0.1.9.11 gate (tombstone): due only at 3303, after the real close 3249",
            tombstone019111214.SequenceEqual(new long[] { 3303 }), string.Join(",", tombstone019111214));

        // Live 11:48:34.981-40.006 ET (post-wipe shape): edits at 0/124/249 wipe the roster, which then sits EMPTY
        // until 3458 (the live 0.1.9.9 write landed on it at ~1759), pp edits on the empty roster at 2334-2975 change
        // nothing, then the build adds ten picks at 3458-4542. An empty selection is never a settled build.
        (long, string, ulong, uint, int)[] fight1148 =
        [
            (0, "pp", 0, 2, 2), (124, "pp", 7, 3, 0), (249, "pp", 8, 1, 0), (632, "pp", 0, 1, 5), (768, "pp", 0, 1, 5),
            (768, "nb", 0, 1, -2), (2334, "pp", 0, 2, 2), (2467, "pp", 5, 3, 0), (2850, "pp", 0, 2, 1), (2975, "pp", 5, 3, 0),
            (3458, "nb", 0, 2, 7), (3584, "nb", 0, 2, 7), (3708, "nb", 0, 2, 3), (3844, "nb", 0, 2, 7), (3967, "nb", 0, 2, 7),
            (4068, "nb", 0, 2, 7), (4192, "nb", 0, 2, 5), (4317, "nb", 0, 2, 7), (4417, "nb", 0, 2, 7), (4542, "nb", 0, 2, 5),
            (5025, "pp", 0, 1, -2), (5025, "nb", 0, 1, -2),
        ];
        (long, int[])[] build1148 =
        [
            (3458, [19]), (3584, [19, 8]), (3708, [19, 8, 22]), (3844, [19, 8, 22, 41]), (3967, [19, 8, 22, 41, 42]),
            (4068, [19, 8, 22, 41, 42, 43]), (4192, [19, 8, 22, 41, 42, 43, 45]), (4317, [19, 8, 22, 41, 42, 43, 45, 46]),
            (4417, [19, 8, 22, 41, 42, 43, 45, 46, 49]), (4542, [19, 8, 22, 41, 42, 43, 45, 46, 49, 50]),
        ];
        var replay1148 = ReplayRoster(fight1148, build1148, 5025);
        Check("replay 11:48:35 (real stream, bounded at its real close 5025): no correction on the empty sit or after the build",
            replay1148.Count == 0, string.Join(",", replay1148));
        var pastClose1148 = ReplayRoster(fight1148, build1148, 7025);
        Check("replay 11:48:35 past the close: the roster surface under AutoDuty never writes — the correction is withdrawn",
            pastClose1148.Count == 0, string.Join(",", pastClose1148));
        var tombstone019111148 = ReplayRoster(fight1148, build1148, 7025, rosterGate01911: true);
        Check("replay 11:48:35 under the withdrawn 0.1.9.11 gate (tombstone): due only at 6042, after the real close 5025",
            tombstone019111148.SequenceEqual(new long[] { 6042 }), string.Join(",", tombstone019111148));

        // Horn unchanged (the horn's AutoDuty toggles ARE edit events, so its edit-time gate is the right model):
        var hornMidBuild = Corrections([(0, 1), (110, 2), (220, 3), (330, 4)], 1200);
        Check("...the same shape under the horn's 3-pick threshold still settles at 250 ms (horn unchanged)",
            hornMidBuild.SequenceEqual(new long[] { 580 }), string.Join(",", hornMidBuild));
        var hornWiped = Corrections([(0, 0)], 2500);
        Check("...the horn keeps its empty-selection quiet path (default arguments unchanged)",
            hornWiped.SequenceEqual(new long[] { FormationLogic.AutoDutyShortTeamQuietMs }), string.Join(",", hornWiped));
    }
}
