using LazyCrucible.Policy;

namespace LazyCrucible.Harness;

/// <summary>
///     AutoDuty stall watch (0.1.7.0). Live 2026-09-29: AutoDuty's loop stopped stepping at 18:08:35 (board exit) and
///     kept reporting running until a plugin reload at 18:11:55, and LazyCrucible stood down for it the whole time,
///     including in a Second Master Board the player was running by hand. Every case below is either a line of that
///     evening (the defect: the old gate said "driving" for all of it) or a recorded healthy loop (21:40, the
///     control: a stalled verdict there would hand AutoDuty's screens to the plugin mid-run).
/// </summary>
internal static class AutoDutyWatchCases
{
    private const uint Lobby = 148;
    private const uint Board4 = 1342; // First Master's Board
    private const uint Board5 = 1343; // Second Master's Board

    private static void Check(string what, bool ok, string? detail = null) => Program.Check(what, ok, detail);

    /// <summary> One reading per second, like the plugin's poll. </summary>
    private sealed class Clock
    {
        public readonly AutoDutyWatch Watch = new();
        public double Now;

        public AutoDutyStatus Hold(double seconds, bool? looping, bool? navigating, uint territory, bool stopped = false)
        {
            var status = Watch.Status;
            for (var i = 0; i < seconds; i++)
            {
                Now += 1;
                status = Watch.Step(stopped, looping, navigating, territory, BoardOf(territory), Now);
            }
            return status;
        }

        /// <summary> Like Hold, but fails the moment any second is judged stalled. </summary>
        public bool NeverStalled(double seconds, bool? looping, bool? navigating, uint territory)
        {
            for (var i = 0; i < seconds; i++)
            {
                Now += 1;
                if (Watch.Step(false, looping, navigating, territory, BoardOf(territory), Now) == AutoDutyStatus.Stalled)
                    return false;
            }
            return true;
        }
    }

    private static int BoardOf(uint territory) => BST_CrucibleData.BoardOfTerritory(territory);

    public static void Run()
    {
        Console.WriteLine("-- AutoDuty stall watch (0.1.7.0; live 2026-09-29 18:08-18:14) --");
        IncidentNavigatingStuck();
        IncidentNavigatingDropped();
        IncidentFastBoardEntry();
        HealthyLoop();
        Lifecycle();
        IpcMissing();
    }

    /// <summary>
    ///     Variant A of the evening: AutoDuty leaves Navigating raised after the run's exit. The player reaches the
    ///     lobby at 18:08:37; the old gate read "running" from then until the reload at 18:11:40.
    /// </summary>
    private static void IncidentNavigatingStuck()
    {
        var c = new Clock();
        c.Hold(300, true, true, Board4); // the run in progress: driving
        Check("M1 run in progress (looping, navigating, board 4): driving",
            c.Watch.Status == AutoDutyStatus.Driving, $"{c.Watch.Status}/{c.Watch.Reason}");

        c.Hold(5, true, true, Lobby); // 18:08:37 back in the lobby, the first seconds are the healthy 7 s window
        Check("Lobby 5 s after the exit, still navigating: not yet judged (healthy loops clear it within ~7 s)",
            c.Watch.Status == AutoDutyStatus.Driving, $"{c.Watch.Status}/{c.Watch.Reason}");

        c.Hold(30, true, true, Lobby); // 18:09:11 the player opens the Second Master Board menu
        Check("Lobby 35 s after the exit, navigating never cleared: stalled (navigating_in_lobby)",
            c.Watch.Status == AutoDutyStatus.Stalled && c.Watch.Reason == "navigating_in_lobby", $"{c.Watch.Status}/{c.Watch.Reason}");

        var b5 = c.Hold(40, true, true, Board5); // 18:09:24 to 18:10:05 the player's own run, the stand-down that kept horns off
        Check("Player enters the Second Master Board by hand, AutoDuty still 'navigating': stays stalled, no stand-down",
            b5 == AutoDutyStatus.Stalled && c.Watch.Reason == "navigating_in_lobby", $"{b5}/{c.Watch.Reason}");
    }

    /// <summary>
    ///     Variant B: Navigating dropped, Looping stayed. The player opens the Second Master Board menu 34 s after the
    ///     exit (18:09:11, still inside the healthy window) and enters at 18:09:24; the in-board stand-down at 18:10:05
    ///     is 41 s into that run.
    /// </summary>
    private static void IncidentNavigatingDropped()
    {
        var c = new Clock();
        c.Hold(300, true, true, Board4);
        c.Hold(2, true, true, Lobby);
        c.Hold(35, true, false, Lobby); // 18:09:11: menu open in the lobby, 37 s after the exit
        Check("Lobby menu 37 s after the exit, looping but not navigating: still yielding (AutoDuty could be mid-queue)",
            c.Watch.Status == AutoDutyStatus.Driving, $"{c.Watch.Status}/{c.Watch.Reason}");

        c.Hold(10, true, false, Board5); // 18:09:24 to 18:09:34
        Check("Second Master Board, 10 s in by hand: inside the grace, still yielding",
            c.Watch.Status == AutoDutyStatus.Driving, $"{c.Watch.Status}/{c.Watch.Reason}");

        var at41 = c.Hold(31, true, false, Board5); // 18:10:05 the horn screen opens, 41 s in
        Check("Second Master Board, 41 s in by hand, AutoDuty not navigating: stalled (idle_in_board), the horn screen is free",
            at41 == AutoDutyStatus.Stalled && c.Watch.Reason == "idle_in_board", $"{at41}/{c.Watch.Reason}");

        // The lobby-only route: the player stays in the lobby with the menu open.
        var lobbyOnly = new Clock();
        lobbyOnly.Hold(10, true, false, Lobby);
        Check("Lobby 89 s with nothing changing: still inside the lobby grace",
            lobbyOnly.Hold(79, true, false, Lobby) == AutoDutyStatus.Driving);
        Check("Lobby 91 s with nothing changing: stalled (no_progress_in_lobby)",
            lobbyOnly.Hold(2, true, false, Lobby) == AutoDutyStatus.Stalled && lobbyOnly.Watch.Reason == "no_progress_in_lobby");
    }

    /// <summary> The player is in another board before the lobby grace ends, with Navigating stuck on. </summary>
    private static void IncidentFastBoardEntry()
    {
        var c = new Clock();
        c.Hold(120, true, true, Board4); // AutoDuty's own run: its board is 4
        c.Hold(3, true, true, Lobby);
        var status = c.Hold(2, true, true, Board5); // 18:08:37 exit, in board 5 five seconds later
        Check("In a different board than the one AutoDuty took in (Navigating stuck on): stalled (other_board)",
            status == AutoDutyStatus.Stalled && c.Watch.Reason == "other_board", $"{status}/{c.Watch.Reason}");
    }

    /// <summary>
    ///     The recorded healthy loop (21:40-21:41): exit at 21:40:18, Goto to territory 144 at :25 (7 s), back in 148 at
    ///     :45, Goto finished :59, queue, team ready :03, Starting Navigation :10, board entry :19. Never stalled.
    /// </summary>
    private static void HealthyLoop()
    {
        var c = new Clock();
        var ok = c.NeverStalled(240, true, true, Board4); // 4 min in the run
        ok &= c.NeverStalled(7, true, true, Lobby); // exit, Navigating cleared by the loop tasks
        ok &= c.NeverStalled(20, true, false, 144); // between-loop action in another territory
        ok &= c.NeverStalled(34, true, false, Lobby); // goto, queue, team, challenge
        ok &= c.NeverStalled(9, true, true, Lobby); // Starting Navigation raised before the board opens
        ok &= c.NeverStalled(300, true, true, Board4); // next run
        Check("Recorded healthy loop (run, exit, between-loop goto, queue, next run): never judged stalled", ok, $"{c.Watch.Status}/{c.Watch.Reason}");

        // Two full boards back to back, different boards (a playlist): the second board is not 'other_board'.
        var p = new Clock();
        var pl = p.NeverStalled(200, true, true, Board4);
        pl &= p.NeverStalled(6, true, true, Lobby);
        pl &= p.NeverStalled(40, true, false, Lobby);
        pl &= p.NeverStalled(9, true, true, Lobby);
        pl &= p.NeverStalled(200, true, true, Board5);
        Check("Loop that moves on to another board after a fresh Navigating raise: driving, not other_board", pl, $"{p.Watch.Status}/{p.Watch.Reason}");
    }

    private static void Lifecycle()
    {
        // A stall ends when AutoDuty stops.
        var c = new Clock();
        c.Hold(50, true, true, Board4);
        c.Hold(40, true, true, Lobby);
        Check("Stalled before the reload", c.Watch.Status == AutoDutyStatus.Stalled);
        Check("AutoDuty reloaded (IsStopped true): stopped, stall cleared",
            c.Hold(1, null, null, Lobby, stopped: true) == AutoDutyStatus.Stopped && c.Watch.Reason is null);
        Check("Next AutoDuty run starts clean (driving)",
            c.Hold(1, true, false, Lobby) == AutoDutyStatus.Driving);

        // A stall ends when AutoDuty raises Navigating again (it recovered and started the next run).
        var r = new Clock();
        r.Hold(50, true, true, Board4);
        r.Hold(40, true, true, Lobby);
        Check("Stalled before it recovers", r.Watch.Status == AutoDutyStatus.Stalled);
        r.Hold(1, true, false, Lobby);
        Check("Still stalled while Navigating is merely dropped (no new run yet)", r.Watch.Status == AutoDutyStatus.Stalled);
        Check("Navigating raised again (a new run started): driving again",
            r.Hold(1, true, true, Lobby) == AutoDutyStatus.Driving && r.Watch.Reason is null);

        // AutoDuty's own definition of running (Looping or Navigating) is false: not driving anything.
        var n = new Clock();
        n.Hold(10, true, true, Board4);
        Check("Looping and Navigating both off for 4 s: not judged yet", n.Hold(4, false, false, Lobby) == AutoDutyStatus.Driving);
        Check("Looping and Navigating both off for 6 s while IsStopped is false: stalled (not_running)",
            n.Hold(2, false, false, Lobby) == AutoDutyStatus.Stalled && n.Watch.Reason == "not_running");
    }

    /// <summary> An older AutoDuty without IsLooping / IsNavigating keeps the old behaviour: driving while not stopped. </summary>
    private static void IpcMissing()
    {
        var c = new Clock();
        Check("IsNavigating / IsLooping unavailable: driving while IsStopped is false, however long the lobby",
            c.Hold(600, null, null, Lobby) == AutoDutyStatus.Driving);
        Check("IsNavigating / IsLooping unavailable, in a board: driving",
            c.Hold(600, null, null, Board5) == AutoDutyStatus.Driving);
    }
}
