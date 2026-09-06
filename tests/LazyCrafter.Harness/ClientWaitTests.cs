using LazyCrafter.Core;

namespace LazyCrafter.Harness;

/// <summary>
/// A fake wall clock for the wait-and-resume hold (card t_ee6f7bf5). The phase machine's cap reads
/// "how long since Enter", which offline means the clock has to be fake.
/// </summary>
internal sealed class FakeClock(DateTime start) : ITimeSource
{
    private DateTime _now = start;

    public DateTime UtcNow => _now;

    /// <summary>Advance and return the new now.</summary>
    public DateTime Advance(TimeSpan by)
    {
        _now = _now.Add(by);
        return _now;
    }
}

/// <summary>
/// The hold-then-resume loop, driven exactly as <c>DispatchService</c>'s <c>Phase.WaitClientFree</c> will drive
/// it: <c>BusyBecause()</c> sampled once per poll, one entry Say, SetStatus per poll, resume / timeout at the
/// edges. Every check asserts on the RENDERED chat/status text - the card's whole deliverable is a behaviour
/// with specific words, and the previous card on this thread (t_0b4d8b2c) proved an internal-state assertion
/// stays green right through a rendering defect.
/// </summary>
internal sealed class HoldMachine
{
    private readonly ITimeSource _clock;
    private readonly List<string> _chat = new();
    private readonly List<string> _says = new();
    private string? _waitingOn;
    private bool _held;
    private DateTime _heldSince;

    public HoldMachine(ITimeSource clock) { _clock = clock; }

    public IReadOnlyList<string> Chat => _chat;          // every Say() the dispatcher would print
    public IReadOnlyList<string> Says => _says;          // Say() calls only (entry / switch / resume / timeout)
    public string Status { get; private set; } = "";
    public bool Ended { get; private set; }
    public string? EndedReason { get; private set; }

    /// <summary>Called once, in <c>Phase.Crafts</c>, with the fresh <c>BusyBecause()</c> sample - i.e. Enter(WaitClientFree).</summary>
    public void EnterWait(string busyBecause)
    {
        _held = true;
        _waitingOn = busyBecause;
        _heldSince = _clock.UtcNow;
        Say(ClientWaitPolicy.WaitLine(busyBecause));
        Status = ClientWaitPolicy.WaitStatus(busyBecause, TimeSpan.Zero);
    }

    /// <summary>One poll of <c>Phase.WaitClientFree</c>: <paramref name="busyBecause"/> is the fresh sample.</summary>
    public void Poll(string? busyBecause)
    {
        if (!_held) throw new InvalidOperationException("not held");
        var held = _clock.UtcNow - _heldSince;
        if (busyBecause is null)
        {
            _held = false;
            Say(ClientWaitPolicy.ResumedLine(_waitingOn));
            Status = ClientWaitPolicy.ResumedStatus();
            return;
        }
        // A different window taking over mid-hold re-says once - see the test for why silence there is a trap.
        if (_says.Count > 0 && !busyBecause.Equals(_waitingOn, StringComparison.Ordinal))
        {
            _waitingOn = busyBecause;
            Say(ClientWaitPolicy.WaitLine(busyBecause));
        }
        Status = ClientWaitPolicy.WaitStatus(busyBecause, held);
        if (ClientWaitPolicy.TimedOut(held))
        {
            _held = false;
            Ended = true;
            EndedReason = ClientWaitPolicy.TimeoutReason(_waitingOn, ClientWaitPolicy.WaitCap);
            _chat.Add(ClientWaitPolicy.TimeoutLine(_waitingOn, ClientWaitPolicy.WaitCap));   // error line
        }
    }

    private void Say(string line)
    {
        _says.Add(line);
        _chat.Add(line);
    }
}

internal static class ClientWaitTests
{
    private const string Board = "the market board";

    public static readonly List<(string Name, Func<bool> Check)> Tests = new()
    {
        // ---------------------------------------------------------------- the cap and the phrases

        ("wait: the cap is exactly five minutes", () =>
            ClientWaitPolicy.WaitCap == TimeSpan.FromMinutes(5)),

        ("wait: the entry line is 'waiting - close the market board to continue'", () =>
            ClientWaitPolicy.WaitLine(Board) == "waiting - close the market board to continue"),

        ("wait: the entry line names whatever is actually blocking", () =>
            ClientWaitPolicy.WaitLine("the summoning bell") == "waiting - close the summoning bell to continue"),

        ("wait: an unnamed window gets the generic line, never a blank", () =>
            ClientWaitPolicy.WaitLine(CraftDiagnosis.UnknownWindow) == "waiting - close a game window to continue"
            && ClientWaitPolicy.WaitLine(null) == "waiting - close a game window to continue"
            && ClientWaitPolicy.WaitLine("") == "waiting - close a game window to continue"),

        ("wait: the persistent status line counts the hold", () =>
            ClientWaitPolicy.WaitStatus(Board, TimeSpan.FromSeconds(90)) == "waiting - the market board (1:30)"),

        ("wait: the timeout reason is the truthful one", () =>
        {
            var r = ClientWaitPolicy.TimeoutReason(Board, ClientWaitPolicy.WaitCap);
            return r.Contains("the market board") && r.Contains("blocked crafting for 5 minutes")
                && r.Contains("close it and press Resume");
        }),

        // ---------------------------------------------------------------- the hold -> resume path

        ("hold-and-resume: closing the window resumes with no user action and says so", () =>
        {
            var m = new HoldMachine(new FakeClock(new DateTime(2026, 9, 6, 12, 0, 0, DateTimeKind.Utc)));
            m.EnterWait(Board);
            m.Poll(Board);
            m.Poll(null);   // window closed
            return !m.Ended
                && m.Chat[0] == "waiting - close the market board to continue"
                && m.Chat[^1].Contains("the market board") && m.Chat[^1].Contains("was closed") && m.Chat[^1].Contains("resuming the cart");
        }),

        ("hold-and-resume: no timeout while the window is closed inside the cap", () =>
        {
            var clock = new FakeClock(new DateTime(2026, 9, 6, 12, 0, 0, DateTimeKind.Utc));
            var m = new HoldMachine(clock);
            m.EnterWait(Board);
            for (var i = 0; i < 55; i++) { clock.Advance(TimeSpan.FromSeconds(5)); m.Poll(Board); }
            m.Poll(null);   // closed at 4:35
            return !m.Ended && m.Chat.Count == 2;
        }),

        // ---------------------------------------------------------------- the hold -> 5-minute timeout path

        ("hold-and-resume: five minutes blocked stops cleanly with the truthful reason", () =>
        {
            var clock = new FakeClock(new DateTime(2026, 9, 6, 12, 0, 0, DateTimeKind.Utc));
            var m = new HoldMachine(clock);
            m.EnterWait(Board);
            for (var i = 0; i < 10 && !m.Ended; i++) { clock.Advance(TimeSpan.FromSeconds(30)); m.Poll(Board); }
            return m.Ended
                && m.EndedReason!.Contains("the market board") && m.EndedReason!.Contains("5 minutes")
                && m.Chat[^1].Contains("close it and press Resume");
        }),

        ("hold-and-resume: the hold never outlives the cap", () =>
        {
            var clock = new FakeClock(new DateTime(2026, 9, 6, 12, 0, 0, DateTimeKind.Utc));
            var m = new HoldMachine(clock);
            m.EnterWait(Board);
            for (var i = 0; i < 40 && !m.Ended; i++) { clock.Advance(TimeSpan.FromSeconds(15)); m.Poll(Board); }
            return m.Ended && !m.Chat[^1].Contains("waiting - close");
        }),

        // ---------------------------------------------------------------- the gate must not fire on a working craft

        ("wait: the blocking set is Artisan's refusal set plus the extended five, and never a crafting flag", () =>
        {
            // The first nine names are Artisan's own Occupied() gate, verbatim from its source: the game raises
            // one of them when it refuses a craft command ("Unable to execute command while occupied" - the
            // exact error in Joey's 11:58 log). The crafting conditions are what Artisan is IN while working:
            // blocking on them would deadlock the dispatcher against its own craft.
            var b = ClientWaitPolicy.BlockingConditionNames;
            return b.Length == 14
                && b.Take(9).SequenceEqual(new[] { "Occupied", "Occupied30", "Occupied33", "Occupied38", "Occupied39",
                    "OccupiedInEvent", "OccupiedInQuestEvent", "OccupiedInCutSceneEvent", "OccupiedSummoningBell" })
                && b.Skip(9).SequenceEqual(new[] { "TradeOpen", "WatchingCutscene", "WatchingCutscene78", "BetweenAreas", "BetweenAreas51" })
                && ClientWaitPolicy.CraftingConditionNames.SequenceEqual(
                    new[] { "Crafting", "PreparingToCraft", "ExecutingCraftingAction", "NormalConditions" })
                && !b.Intersect(ClientWaitPolicy.CraftingConditionNames).Any();
        }),

        ("wait: the blocking names are distinct - a name twice cannot widen the gate by accident", () =>
            ClientWaitPolicy.BlockingConditionNames.Distinct().Count() == ClientWaitPolicy.BlockingConditionNames.Length),

        // ---------------------------------------------------------------- the repeated Say and the churn traps

        ("wait: the repeat is a SetStatus, not a chat line every poll", () =>
        {
            var clock = new FakeClock(new DateTime(2026, 9, 6, 12, 0, 0, DateTimeKind.Utc));
            var m = new HoldMachine(clock);
            m.EnterWait(Board);
            for (var i = 0; i < 12; i++) { clock.Advance(TimeSpan.FromSeconds(10)); m.Poll(Board); }
            // two minutes of polling produced exactly one chat line: the entry Say
            return m.Chat.Count == 1 && !m.Ended;
        }),

        ("wait: a DIFFERENT window appearing mid-hold re-says once and never goes quiet", () =>
        {
            var clock = new FakeClock(new DateTime(2026, 9, 6, 12, 0, 0, DateTimeKind.Utc));
            var m = new HoldMachine(clock);
            m.EnterWait(Board);
            for (var i = 0; i < 30; i++) { clock.Advance(TimeSpan.FromSeconds(5)); m.Poll(Board); }   // 2:30
            m.Poll("the summoning bell");   // he closed the board and opened the bell menu instead
            return m.Chat.Count == 2
                && m.Chat[^1] == "waiting - close the summoning bell to continue";
        }),
    };
}
