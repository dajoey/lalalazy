using LazyFishSitter.Core;

namespace LazyFishSitter.Harness;

/// <summary>
/// Offline replay tests for the v0.1.3.0 "sit once and stay" policy.
///
/// Every one of these is a case a previous release got wrong in game. Cases 1-3 are the
/// regressions (v0.1.0.0 yo-yo, v0.1.1.0 swallowed mid-cast send, v0.1.2.0 re-sit per catch);
/// the rest are the guards. Case 3 in particular is the NEGATIVE CONTROL: it replays the shape
/// of the real v0.1.1.0 ffxivdb trace and must produce zero sends, so a green run means
/// something.
/// </summary>
internal static class Program
{
    private static int _pass, _fail;

    private static void Check(string name, bool ok, string detail = "")
    {
        if (ok) { _pass++; Console.WriteLine($"PASS  {name}"); }
        else { _fail++; Console.WriteLine($"FAIL  {name}{(detail.Length > 0 ? "  -- " + detail : "")}"); }
    }

    private static int Main()
    {
        Console.WriteLine("LazyFishSitter policy harness (Core/SitPolicy.cs, no Dalamud)");
        Console.WriteLine(new string('-', 72));

        Case01_SitsOnceAtTheStandbyBeat();
        Case02_NeverSendsWhileTheLineIsInTheWater();
        Case03_NegativeControl_TheV0110Trace();
        Case04_NoResitAfterEveryCatch();
        Case05_NeverSendsWhileSeated();
        Case06_AlreadySeatedOnArrivalSendsNothing();
        Case07_HookTransientStandIsIgnored();
        Case08_CapIsThreePerTripNotPerCast();
        Case09_TripDoesNotResetBetweenCasts();
        Case10_MinimumSpacingBetweenSends();
        Case11_SwallowedSitIsRetriedAtTheNextBeat();
        Case12_DisabledAndBlockedNeverSend();
        Case13_LeavingTheHoleEndsTheTripAndLogsTheOpenQuestion();
        Case14_CustomSitCommandIsUsed();
        Case15_TransitionLogIsRateLimited();
        Case16_EitherSeatedSignalAloneBlocksTheSend();
        Case17_NeverSendsWhileChangingPosition();

        Console.WriteLine(new string('-', 72));
        Console.WriteLine($"{_pass} passed, {_fail} failed");
        if (_fail == 0) Console.WriteLine("OK");
        return _fail == 0 ? 0 : 1;
    }

    // ---------------------------------------------------------------------------------
    // 1. The headline behaviour: one /sit, at the standby beat, before the line goes out.
    // ---------------------------------------------------------------------------------
    private static void Case01_SitsOnceAtTheStandbyBeat()
    {
        var s = new Session().ArriveAtHole().PoleReady(10).CastNoBite().CastNoBite().LeaveHole();
        var r = Runner.Run(s);

        Check("01a sends exactly one /sit for the whole trip", r.Count == 1, $"sent {r.Count}");
        if (r.Count >= 1)
        {
            Check("01b the send happens at the standby beat, not with a line out",
                r.Sends[0].Snap.State == FishState.PoleReady, $"state was {r.Sends[0].Snap.State}");
            Check("01c the send happens while standing",
                !r.Sends[0].Snap.Seated);
        }
    }

    // ---------------------------------------------------------------------------------
    // 2. v0.1.1.0's defect: a send with the line already in the water is swallowed by the
    //    game. The policy must never pick that moment.
    // ---------------------------------------------------------------------------------
    private static void Case02_NeverSendsWhileTheLineIsInTheWater()
    {
        var s = new Session().ArriveAtHole().PoleReady(10).CastNoBite(20).CastAndCatch().LeaveHole();
        var r = Runner.Run(s, sitTakes: false); // worst case: nothing ever takes, so it keeps trying

        var badStates = r.Sends.Where(x => x.Snap.State != FishState.PoleReady).ToList();
        Check("02 no send in any state other than PoleReady", badStates.Count == 0,
            badStates.Count == 0 ? "" : string.Join(", ", badStates.Select(b => b.Snap.State.ToString())));
    }

    // ---------------------------------------------------------------------------------
    // 3. NEGATIVE CONTROL. The real v0.1.1.0 trace from ffxivdb: the plugin woke up with the
    //    Fishing flag already on and a line already in the water, and fired its one /sit
    //    there - ten times across ten sessions, with no effect. Same input shape here must
    //    produce zero sends, otherwise this harness proves nothing.
    // ---------------------------------------------------------------------------------
    private static void Case03_NegativeControl_TheV0110Trace()
    {
        var s = new Session()
            .Set(x => { x.Fishing = true; x.CanFish = true; x.State = FishState.LineInWater; x.ModeName = "Gathering"; x.ModeValue = 6; })
            .Hold(25)
            .Set(x => x.State = FishState.PullingPoleIn).Hold(2)
            .Set(x => { x.Fishing = false; x.State = FishState.None; }).Hold(3);
        var r = Runner.Run(s, sitTakes: false);

        Check("03 the v0.1.1.0 trace (join mid-cast, line in water) sends nothing", r.Count == 0,
            $"sent {r.Count}");
    }

    // ---------------------------------------------------------------------------------
    // 4. v0.1.2.0's defect: it re-sat after every catch. Joey: the game does that for you.
    // ---------------------------------------------------------------------------------
    private static void Case04_NoResitAfterEveryCatch()
    {
        var s = new Session().ArriveAtHole().PoleReady(10);
        for (var i = 0; i < 6; i++) s.CastAndCatch();
        s.LeaveHole();
        var r = Runner.Run(s);

        Check("04 six catches produce no extra sends beyond the first sit", r.Count == 1,
            $"sent {r.Count} across 6 catches");
    }

    // ---------------------------------------------------------------------------------
    // 5. The one that must never break: /sit on a seated character STANDS him.
    // ---------------------------------------------------------------------------------
    private static void Case05_NeverSendsWhileSeated()
    {
        var s = new Session().ArriveAtHole().PoleReady(6).Set(x => x.Seated = true).PoleReady(30);
        for (var i = 0; i < 4; i++) s.CastAndCatch(gameStandsYou: false);
        s.LeaveHole();
        var r = Runner.Run(s, sitTakes: false);

        var whileSeated = r.Sends.Count(x => x.Snap.Seated);
        Check("05 never sends while any seated signal reads seated", whileSeated == 0, $"{whileSeated} send(s) while seated");
    }

    private static void Case06_AlreadySeatedOnArrivalSendsNothing()
    {
        var s = new Session().Set(x => x.Seated = true).ArriveAtHole().PoleReady(10);
        for (var i = 0; i < 4; i++) s.CastAndCatch();
        s.LeaveHole();
        var r = Runner.Run(s);

        Check("06 a player who sat down himself is never touched", r.Count == 0, $"sent {r.Count}");
    }

    // ---------------------------------------------------------------------------------
    // 7. The transient stand around a hook is the game's animation, not the player.
    // ---------------------------------------------------------------------------------
    private static void Case07_HookTransientStandIsIgnored()
    {
        // Seated the whole time except a 2 s stand across the hook, exactly as Joey describes.
        var s = new Session().Set(x => x.Seated = true).ArriveAtHole().PoleReady(10)
            .Set(x => { x.Fishing = true; x.State = FishState.LineInWater; }).Hold(8)
            .Set(x => x.State = FishState.Bite).Hold(0.5)
            .Set(x => { x.State = FishState.Hooking; x.Seated = false; }).Hold(2)
            .Set(x => { x.State = FishState.PoleReady; x.Seated = true; }).Hold(10)
            .LeaveHole();
        var r = Runner.Run(s);

        Check("07 a brief stand across a hook produces no send", r.Count == 0, $"sent {r.Count}");
    }

    // ---------------------------------------------------------------------------------
    // 8-10. Hard loop bounds.
    // ---------------------------------------------------------------------------------
    private static void Case08_CapIsThreePerTripNotPerCast()
    {
        // Nothing ever takes: the policy will keep trying at every beat, and must stop at 3.
        var s = new Session().ArriveAtHole().PoleReady(10);
        for (var i = 0; i < 12; i++) s.CastNoBite();
        s.LeaveHole();
        var r = Runner.Run(s, sitTakes: false);

        Check("08a a never-accepted sit is capped at 3 per trip", r.Count == SitPolicy.MaxSendsPerTrip, $"sent {r.Count}");
        Check("08b the cap logs a line so it is visible in ffxivdb",
            r.Logs.Any(l => l.Contains("cap reached", StringComparison.Ordinal)));
    }

    private static void Case09_TripDoesNotResetBetweenCasts()
    {
        // ConditionFlag.Fishing drops between casts. If the trip reset on that, the cap would
        // become 3 per cast - which is how v0.1.1.0 counted ten "sessions" in 21 minutes.
        var s = new Session().ArriveAtHole().PoleReady(10);
        for (var i = 0; i < 8; i++)
        {
            s.Set(x => { x.Fishing = true; x.State = FishState.CastingOut; }).Hold(1)
             .Set(x => x.State = FishState.LineInWater).Hold(6)
             // Fishing flag drops, but CanFish stays true - still at the hole.
             .Set(x => { x.Fishing = false; x.State = FishState.PoleReady; }).Hold(4);
        }
        s.LeaveHole();
        var r = Runner.Run(s, sitTakes: false);

        Check("09 the fishing flag dropping between casts does not reset the cap",
            r.Count <= SitPolicy.MaxSendsPerTrip, $"sent {r.Count}, cap is {SitPolicy.MaxSendsPerTrip}");
    }

    private static void Case10_MinimumSpacingBetweenSends()
    {
        var s = new Session().ArriveAtHole().PoleReady(120).LeaveHole();
        var r = Runner.Run(s, sitTakes: false);

        var tooClose = new List<double>();
        for (var i = 1; i < r.Count; i++)
        {
            var gap = (r.Sends[i].At - r.Sends[i - 1].At).TotalSeconds;
            if (gap < SitPolicy.MinSendSpacing.TotalSeconds - 0.001) tooClose.Add(gap);
        }
        Check("10a sends are at least 10 s apart", tooClose.Count == 0,
            tooClose.Count == 0 ? "" : string.Join(", ", tooClose.Select(g => $"{g:F2}s")));
        Check("10b a long unaccepted standby beat still stops at the cap",
            r.Count == SitPolicy.MaxSendsPerTrip, $"sent {r.Count}");
    }

    // ---------------------------------------------------------------------------------
    // 11. A swallowed /sit must not strand the player standing for the whole trip.
    // ---------------------------------------------------------------------------------
    private static void Case11_SwallowedSitIsRetriedAtTheNextBeat()
    {
        var s = new Session().ArriveAtHole().PoleReady(10).CastNoBite().CastNoBite().LeaveHole();
        var r = Runner.Run(s, sitTakes: false);

        Check("11 a swallowed sit is retried at a later standby beat", r.Count >= 2, $"sent {r.Count}");
    }

    private static void Case12_DisabledAndBlockedNeverSend()
    {
        var s1 = new Session().ArriveAtHole().PoleReady(60).LeaveHole();
        Check("12a disabled sends nothing", Runner.Run(s1, sitTakes: false, enabled: false).Count == 0);

        var s2 = new Session().ArriveAtHole().PoleReady(60).LeaveHole();
        Check("12b a blocking condition sends nothing",
            Runner.Run(s2, sitTakes: false, blockReason: "in combat").Count == 0);
    }

    // ---------------------------------------------------------------------------------
    // 13. The trip summary is how the still-open question gets answered from ffxivdb.
    // ---------------------------------------------------------------------------------
    private static void Case13_LeavingTheHoleEndsTheTripAndLogsTheOpenQuestion()
    {
        var s = new Session().ArriveAtHole().PoleReady(10).CastAndCatch().LeaveHole(30);
        var r = Runner.Run(s);

        Check("13a the trip end is logged", r.Logs.Any(l => l.Contains("fishing trip ended", StringComparison.Ordinal)));
        Check("13b the summary carries the seated-read verdict",
            r.Logs.Any(l => l.Contains("seatedReadWorksWithRodOut=", StringComparison.Ordinal)));
        Check("13c a seated read with the rod out is called out when it happens",
            r.Logs.Any(l => l.Contains("seated read PROVEN with the rod out", StringComparison.Ordinal)));
    }

    private static void Case14_CustomSitCommandIsUsed()
    {
        var s = new Session().ArriveAtHole().PoleReady(10).LeaveHole();
        var r = Runner.Run(s, sitCommand: "/groundsit");
        Check("14 the configured command is what gets sent",
            r.Count == 1 && r.Sends[0].Cmd == "/groundsit",
            r.Count == 0 ? "nothing sent" : r.Sends[0].Cmd);
    }

    private static void Case15_TransitionLogIsRateLimited()
    {
        // Flap the fishing state every frame for a minute, then leave the hole so the trip ends.
        var s = new Session().ArriveAtHole();
        for (var i = 0; i < 240; i++)
            s.Set(x => x.State = i % 2 == 0 ? FishState.PoleReady : FishState.LineInWater).Hold(Session.FrameSeconds);
        s.LeaveHole();
        var r = Runner.Run(s, sitTakes: false);

        var transitions = r.Logs.Count(l => l.StartsWith("state ", StringComparison.Ordinal));
        // 60 s of frames = one full window, plus the partial window the run starts in.
        Check("15 transition logging is rate limited", transitions <= SitPolicy.MaxTransitionLogsPerMinute * 2,
            $"{transitions} transition lines");
        Check("15b suppressed lines are reported, even when the trip ends mid-window",
            r.Logs.Any(l => l.Contains("not logged", StringComparison.Ordinal)));
    }

    // ---------------------------------------------------------------------------------
    // 16. Either seated signal alone is enough. Mode and GetPosture disagree in the real
    //     traces (v0.1.1.0 read mode=Gathering posture=Normal at the hole), so the policy
    //     must treat them as OR - a /sit on a seated character STANDS him.
    // ---------------------------------------------------------------------------------
    private static void Case16_EitherSeatedSignalAloneBlocksTheSend()
    {
        // Mode says seated (EmoteLoop), the game's posture read does not.
        var modeOnly = new Session()
            .Set(x => { x.ModeSeatedOverride = true; x.ModeName = "EmoteLoop"; x.ModeValue = 3; })
            .ArriveAtHole().PoleReady(60).LeaveHole();
        var rm = Runner.Run(modeOnly, sitTakes: false);
        Check("16a Character.Mode alone reading seated blocks every send", rm.Count == 0, $"sent {rm.Count}");

        // GetPosture says seated, Mode reads Gathering - the shape seen in the real logs.
        var postureOnly = new Session()
            .Set(x => { x.Seated = true; x.ModeName = "Gathering"; x.ModeValue = 6; })
            .ArriveAtHole().PoleReady(60).LeaveHole();
        var rp = Runner.Run(postureOnly, sitTakes: false);
        Check("16b GetPosture alone reading seated blocks every send", rp.Count == 0, $"sent {rp.Count}");
    }

    // ---------------------------------------------------------------------------------
    // 17. ChangingPosition means the character is mid sit-down/stand-up animation. Sending
    //     into that is how you get a stand, so it must be refused outright.
    // ---------------------------------------------------------------------------------
    private static void Case17_NeverSendsWhileChangingPosition()
    {
        var s = new Session().ArriveAtHole()
            .Set(x => { x.Fishing = true; x.State = FishState.PoleReady; x.ChangingPosition = true; }).Hold(60)
            .LeaveHole();
        var r = Runner.Run(s, sitTakes: false);
        Check("17 never sends while sitting down / standing up", r.Count == 0, $"sent {r.Count}");
    }
}
