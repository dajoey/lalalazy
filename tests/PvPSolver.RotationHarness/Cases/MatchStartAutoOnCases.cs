using System.Text.RegularExpressions;
using RotationSolver.Decisions;

namespace PvPSolver.RotationHarness.Cases;

/// <summary>
///     The 0.1.2.33 match-start auto-on fix ("Turn on when a match starts" never engaged: the
///     0.1.2.26-0.1.2.32 shape engaged during the match loading screen, when the player object does
///     not exist and the job role is unknown, so it silently no-oped and the rotation loaded but
///     never acted in a match).
///     T1: the pure <see cref="MatchStartAutoOn"/> core over its full boolean domain against an
///     independent reference model, the whole cycle (loading screen -&gt; latched -&gt; player loaded -&gt;
///     engaged) simulated for three jobs, and a canary proving the shipped shape - engaging during
///     the loading screen - can never engage.
///     T2: RSCommands_Actions.cs wires the core and no longer contains the dead inline auto-on, and
///     MajorUpdater still gates action emission on the activated flag and calls RSCommands.DoAction().
/// </summary>
internal static class MatchStartAutoOnCases
{
    public static void Run()
    {
        Console.WriteLine("-- match-start auto-on (T1 core + T2 wiring) --");

        // Independent reference models written from the intended semantics.
        static bool RefLatch(bool s, bool st, bool ba, bool bd, bool pvp) => s && !st && ba && bd && pvp;
        static bool RefEngage(bool s, bool l, bool st, bool pvp, bool ready) => s && l && !st && pvp && ready;
        static bool RefDrop(bool s, bool l, bool st, bool pvp) => l && (!s || st || !pvp);

        var latchBad = new List<string>();
        var engageBad = new List<string>();
        var dropBad = new List<string>();
        foreach (var s in new[] { false, true })
        foreach (var st in new[] { false, true })
        foreach (var ba in new[] { false, true })
        foreach (var bd in new[] { false, true })
        foreach (var pvp in new[] { false, true })
        {
            if (MatchStartAutoOn.ShouldLatch(s, st, ba, bd, pvp) != RefLatch(s, st, ba, bd, pvp))
                latchBad.Add($"s={s},st={st},ba={ba},bd={bd},pvp={pvp}");
        }

        foreach (var s in new[] { false, true })
        foreach (var l in new[] { false, true })
        foreach (var st in new[] { false, true })
        foreach (var pvp in new[] { false, true })
        foreach (var ready in new[] { false, true })
        {
            if (MatchStartAutoOn.ShouldEngage(s, l, st, pvp, ready) != RefEngage(s, l, st, pvp, ready))
                engageBad.Add($"s={s},l={l},st={st},pvp={pvp},ready={ready}");
        }

        foreach (var s in new[] { false, true })
        foreach (var l in new[] { false, true })
        foreach (var st in new[] { false, true })
        foreach (var pvp in new[] { false, true })
        {
            if (MatchStartAutoOn.ShouldDrop(s, l, st, pvp) != RefDrop(s, l, st, pvp))
                dropBad.Add($"s={s},l={l},st={st},pvp={pvp}");
        }

        Harness.Case("ShouldLatch matches the reference over its full domain", latchBad.Count == 0,
            string.Join("; ", latchBad.Take(5)) + (latchBad.Count > 5 ? " ..." : ""));
        Harness.Case("ShouldEngage matches the reference over its full domain", engageBad.Count == 0,
            string.Join("; ", engageBad.Take(5)) + (engageBad.Count > 5 ? " ..." : ""));
        Harness.Case("ShouldDrop matches the reference over its full domain", dropBad.Count == 0,
            string.Join("; ", dropBad.Take(5)) + (dropBad.Count > 5 ? " ..." : ""));

        // The shipped bug, modelled: the old inline block called DoStateCommandType during the loading
        // screen, which then no-ops without a player/job role. The player never exists while
        // BetweenAreas, so the old shape engaging is impossible - the canary asserts the opposite and
        // must fail, proving this harness would have caught the shipped build.
        static bool OldShapeEngages(bool s, bool st, bool ba, bool bd, bool pvp, bool playerReadyDuringLoad)
            => s && !st && ba && bd && pvp && playerReadyDuringLoad;
        Harness.Canary("the shipped inline auto-on engages during the loading screen (no player exists then)",
            OldShapeEngages(true, false, true, true, true, playerReadyDuringLoad: false));

        // Whole-cycle simulation, the same cycle for three rotations (the core is job-independent;
        // running it per rotation mirrors how every rotation reaches it through UpdateRotationState).
        foreach (var job in new[] { "AST", "RPR", "MCH" })
        {
            var latched = false;
            var state = false;
            var c1 = Step(ref latched, ref state, betweenAreas: true, boundByDuty: true, pvp: true, playerReady: false);
            var c2 = Step(ref latched, ref state, betweenAreas: false, boundByDuty: true, pvp: true, playerReady: true);
            var c3 = Step(ref latched, ref state, betweenAreas: false, boundByDuty: true, pvp: true, playerReady: true);
            Harness.Case($"{job}: load screen latches, first valid cycle engages, next cycle is quiet",
                c1 == StepResult.Latched && c2 == StepResult.Engaged && c3 == StepResult.None && state,
                $"c1={c1}, c2={c2}, c3={c3}, state={state}");
        }

        // Leaving the territory / turning the setting off drops the latch.
        var latch = true;
        var st2 = false;
        var left = Step(ref latch, ref st2, betweenAreas: false, boundByDuty: false, pvp: false, playerReady: true, settingOn: true);
        Harness.Case("a latch left behind in a non-PvP territory is dropped", left == StepResult.None && !latch && !st2,
            $"step={left}, latch={latch}");

        T2();
    }

    private enum StepResult
    {
        None,
        Latched,
        Engaged,
    }

    /// <summary>One UpdateRotationState auto-on evaluation against the pure core, in the caller's order.</summary>
    private static StepResult Step(ref bool latched, ref bool state, bool betweenAreas, bool boundByDuty, bool pvp,
        bool playerReady, bool settingOn = true)
    {
        if (MatchStartAutoOn.ShouldLatch(settingOn, state, betweenAreas, boundByDuty, pvp))
        {
            latched = true;
            return StepResult.Latched;
        }

        if (MatchStartAutoOn.ShouldEngage(settingOn, latched, state, pvp, playerReady))
        {
            latched = false;
            state = true; // DoStateCommandType(PvP) flips the rotation state on
            return StepResult.Engaged;
        }

        if (MatchStartAutoOn.ShouldDrop(settingOn, latched, state, pvp))
        {
            latched = false;
        }

        return StepResult.None;
    }

    private static void T2()
    {
        var actionsPath = Path.Combine(Program.SrcRoot, "PvPSolver", "Commands", "RSCommands_Actions.cs");
        if (!File.Exists(actionsPath))
        {
            Harness.Case("UpdateRotationState wires the MatchStartAutoOn core", false, actionsPath + " not found");
            return;
        }

        var actions = File.ReadAllText(actionsPath);
        Harness.Case("UpdateRotationState wires the MatchStartAutoOn core (latch, engage, drop)",
            actions.Contains("MatchStartAutoOn.ShouldLatch") && actions.Contains("MatchStartAutoOn.ShouldEngage")
            && actions.Contains("MatchStartAutoOn.ShouldDrop"),
            "one of the three calls is missing");
        Harness.Case("the dead inline loading-screen auto-on is gone",
            !actions.Contains("auto-on when match starts (loading screen)"),
            "the old block is still present");

        var majorPath = Path.Combine(Program.SrcRoot, "PvPSolver", "Updaters", "MajorUpdater.cs");
        var major = File.Exists(majorPath) ? File.ReadAllText(majorPath) : "";
        var gateIdx = major.IndexOf("if (!_isActivatedThisCycle)", StringComparison.Ordinal);
        var doIdx = major.IndexOf("RSCommands.DoAction();", StringComparison.Ordinal);
        Harness.Case("MajorUpdater still gates action emission on the activated flag and calls DoAction",
            gateIdx >= 0 && doIdx > gateIdx,
            $"activated gate@{gateIdx}, DoAction@{doIdx}");
    }
}
