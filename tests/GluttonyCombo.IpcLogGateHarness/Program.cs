using GluttonyCombo.Services.IPC;

namespace GluttonyCombo.IpcLogGateHarness;

/// <summary>
///     Offline assertions on the IPC log-channel emit gate (GluttonyCombo v1.0.4.173).
///     Compiles the real <see cref="LogEmitGate" /> - no Dalamud, no game - and replays the
///     measured 2026-09-05 incident so the rate limit is proven, not reasoned about.
/// </summary>
internal static class Program
{
    private static int _pass;
    private static int _fail;

    /// <summary>The shipped configuration, mirrored from Services/IPC/Helper.cs Logging.</summary>
    private static readonly TimeSpan ShippedWindow = TimeSpan.FromMinutes(5);

    private const int ShippedBurst = 3;

    private const string IncludeShields =
        "[Gluttony IPC] [UIHelper.ShowIPCControlledCheckboxIfNeeded] Error in UIHelper.\n" +
        "System.ArgumentException: Requested value 'IncludeShields' was not found.";

    private const string Tankbusters =
        "[Gluttony IPC] [UIHelper.ShowIPCControlledIndicatorIfNeeded] Error in UIHelper.\n" +
        "System.ArgumentException: Requested value 'TankbustersBeyondParty' was not found.";

    private static int Main()
    {
        var t0 = new DateTime(2026, 9, 5, 23, 50, 0, DateTimeKind.Utc);

        // ---------------------------------------------------------------------------
        // 1. The incident, replayed. ffxivdb measured 3,461 lines in the single minute
        //    2026-09-05 19:50, i.e. the config window was open and drawing at ~58 fps
        //    with the throw on every frame. Replay 26 minutes of that.
        // ---------------------------------------------------------------------------
        var gate = new LogEmitGate(ShippedWindow, ShippedBurst);
        var emitted = 0;
        var totalFrames = 0;
        const int fps = 58;
        const int minutes = 26;

        for (var second = 0; second < minutes * 60; second++)
        for (var frame = 0; frame < fps; frame++)
        {
            totalFrames++;
            var now = t0.AddSeconds(second).AddMilliseconds(frame * (1000.0 / fps));
            if (gate.ShouldEmit(IncludeShields, now, out _))
                emitted++;
        }

        Check("replay covers the real incident length (26 min at 58 fps)",
            totalFrames == 90_480, totalFrames.ToString());

        // 26 minutes / 5-minute window = 6 windows (5 full + the opening one), 3 lines each.
        Check("incident replay emits 18 lines, not 4,257", emitted == 18, emitted.ToString());

        var reduction = 100.0 - emitted * 100.0 / 4257.0;
        Check("that is a >99% reduction against the 4,257 lines actually written",
            reduction > 99.0, $"{reduction:0.00}%");

        Check("post-fix rate is under 1 line/minute",
            emitted / (double)minutes < 1.0, $"{emitted / (double)minutes:0.00}/min");

        // ---------------------------------------------------------------------------
        // 2. The burst itself is not swallowed - the first occurrences always get through,
        //    so a genuinely new error is never invisible. This is the negative control for
        //    the gate: a gate that suppressed everything would pass test 1 and be useless.
        // ---------------------------------------------------------------------------
        var g2 = new LogEmitGate(ShippedWindow, ShippedBurst);
        Check("1st occurrence emits", g2.ShouldEmit(IncludeShields, t0, out _));
        Check("2nd occurrence emits", g2.ShouldEmit(IncludeShields, t0.AddMilliseconds(17), out _));
        Check("3rd occurrence emits", g2.ShouldEmit(IncludeShields, t0.AddMilliseconds(34), out _));
        Check("4th occurrence is suppressed",
            !g2.ShouldEmit(IncludeShields, t0.AddMilliseconds(51), out _));

        // ---------------------------------------------------------------------------
        // 3. Distinct messages are limited independently. Both 1.0.4.171 errors fired 1:1
        //    per frame; neither may hide the other.
        // ---------------------------------------------------------------------------
        var g3 = new LogEmitGate(ShippedWindow, ShippedBurst);
        for (var i = 0; i < 10; i++)
            g3.ShouldEmit(IncludeShields, t0.AddMilliseconds(i * 17), out _);
        Check("a different message still gets its own burst",
            g3.ShouldEmit(Tankbusters, t0.AddMilliseconds(200), out _));
        Check("two distinct messages are tracked separately", g3.TrackedKeys == 2,
            g3.TrackedKeys.ToString());

        // ---------------------------------------------------------------------------
        // 4. The suppressed count is reported on the next emit, so the FREQUENCY stays
        //    visible even though the volume does not. This is the property that makes the
        //    gate honest rather than a cover-up.
        // ---------------------------------------------------------------------------
        var g4 = new LogEmitGate(ShippedWindow, ShippedBurst);
        for (var i = 0; i < 3; i++)
            g4.ShouldEmit(IncludeShields, t0.AddMilliseconds(i * 17), out _);
        for (var i = 0; i < 500; i++)
            g4.ShouldEmit(IncludeShields, t0.AddSeconds(1).AddMilliseconds(i * 17), out _);

        var reopened = g4.ShouldEmit(IncludeShields, t0.Add(ShippedWindow).AddSeconds(1), out var dropped);
        Check("window reopens after the interval", reopened);
        Check("suppressed count is handed back on the next emit", dropped == 500, dropped.ToString());
        Check("suppressed note names the number and the window",
            g4.SuppressedNote(dropped).Contains("+500 identical", StringComparison.Ordinal) &&
            g4.SuppressedNote(dropped).Contains("300s", StringComparison.Ordinal),
            g4.SuppressedNote(dropped));
        Check("no note when nothing was dropped", g4.SuppressedNote(0) == string.Empty);

        // ---------------------------------------------------------------------------
        // 5. An appended stack trace must not defeat the key. Logging.Error appends a fresh
        //    StackTrace to every line; if the key covered it, frame-dependent text could make
        //    each line unique and every one would emit - the exact bug being fixed.
        // ---------------------------------------------------------------------------
        var g5 = new LogEmitGate(ShippedWindow, ShippedBurst);
        for (var i = 0; i < 3; i++)
            g5.ShouldEmit(IncludeShields + "\n   at Frame" + i + "()", t0.AddMilliseconds(i * 17), out _);
        Check("same message with differing trailing stack text is still one key",
            !g5.ShouldEmit(IncludeShields + "\n   at Frame99()", t0.AddMilliseconds(100), out _));
        Check("...and is tracked as exactly one key", g5.TrackedKeys == 1, g5.TrackedKeys.ToString());

        // Negative control for the rule above: collapsing trailing lines must NOT collapse
        // messages that genuinely differ. The offending value lives on line 2, so two errors
        // that differ only there have to stay distinct - otherwise a real second bug would be
        // silently hidden behind the first one's budget.
        var g5b = new LogEmitGate(ShippedWindow, ShippedBurst);
        const string sameLine1 = "Error in UIHelper.";
        for (var i = 0; i < 5; i++)
            g5b.ShouldEmit(sameLine1 + "\nRequested value 'A' was not found.\n   at X()",
                t0.AddMilliseconds(i * 17), out _);
        Check("a message differing only on line 2 still gets its own budget",
            g5b.ShouldEmit(sameLine1 + "\nRequested value 'B' was not found.\n   at X()",
                t0.AddMilliseconds(200), out _));
        Check("...and is tracked as a second key", g5b.TrackedKeys == 2, g5b.TrackedKeys.ToString());

        // ---------------------------------------------------------------------------
        // 6. Unbounded distinct messages must not grow the map without bound.
        // ---------------------------------------------------------------------------
        var g6 = new LogEmitGate(ShippedWindow, ShippedBurst, maxTrackedKeys: 8);
        for (var i = 0; i < 200; i++)
            g6.ShouldEmit("distinct message number " + i, t0.AddMilliseconds(i), out _);
        Check("tracked keys stay bounded", g6.TrackedKeys <= 8, g6.TrackedKeys.ToString());

        // ---------------------------------------------------------------------------
        // 7. Degenerate inputs.
        // ---------------------------------------------------------------------------
        var g7 = new LogEmitGate(ShippedWindow, burst: 0);
        Check("burst is clamped to at least 1", g7.Burst == 1, g7.Burst.ToString());
        Check("empty message does not throw and emits once",
            g7.ShouldEmit(string.Empty, t0, out _));
        Check("empty message is then gated", !g7.ShouldEmit(string.Empty, t0.AddMilliseconds(1), out _));

        Console.WriteLine();
        Console.WriteLine($"{_pass} passed, {_fail} failed");
        if (_fail == 0) Console.WriteLine("OK");
        return _fail == 0 ? 0 : 1;
    }

    private static void Check(string what, bool ok, string? actual = null)
    {
        if (ok)
        {
            _pass++;
            Console.WriteLine($"PASS  {what}");
        }
        else
        {
            _fail++;
            Console.WriteLine($"FAIL  {what}" + (actual is null ? "" : $"  (actual: {actual})"));
        }
    }
}
